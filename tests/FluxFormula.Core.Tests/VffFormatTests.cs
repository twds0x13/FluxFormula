using System;
using System.Runtime.InteropServices;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// VffFormat 完整测试：Magic 常量、IsVff 检测、Resolve 解析（含 override 和嵌套 VFF）。
/// </summary>
public unsafe class VffFormatTests
{
    [SetUp]
    public void SetUp()
    {
        FormulaCache.Reset();
    }

    // ═══════════════════════════════════════════════════════
    // Magic & Constants
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Magic_IsVff()
    {
        byte[] expected = { (byte)'V', (byte)'F', (byte)'F', 0 };
        Assert.That(VffFormat.Magic, Is.EqualTo(expected));
    }

    [Test]
    public void Constants_AreCorrect()
    {
        Assert.That(VffFormat.HeaderSize, Is.EqualTo(8));
        Assert.That(VffFormat.LinkEntrySize, Is.EqualTo(22));
        Assert.That(VffFormat.FlagHasConstants, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // IsVff
    // ═══════════════════════════════════════════════════════

    [Test]
    public void IsVff_Valid_ReturnsTrue()
    {
        byte[] vff = { (byte)'V', (byte)'F', (byte)'F', 0, 1, 0, 0, 0 };
        Assert.That(VffFormat.IsVff(vff), Is.True);
    }

    [Test]
    public void IsVff_WrongMagic_ReturnsFalse()
    {
        byte[] notVff = { (byte)'F', (byte)'F', (byte)'V', 0 };
        Assert.That(VffFormat.IsVff(notVff), Is.False);
    }

    [Test]
    public void IsVff_TooShort_ReturnsFalse()
    {
        Assert.That(VffFormat.IsVff(new byte[3]), Is.False);
    }

    [Test]
    public void IsVff_Empty_ReturnsFalse()
    {
        Assert.That(VffFormat.IsVff(ReadOnlySpan<byte>.Empty), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // 辅助：构造 VFF 字节码
    // ═══════════════════════════════════════════════════════

    private static byte[] BuildVffBytes(VffLinkEntry[] links)
    {
        return BuildVffBytes(links, Array.Empty<(int globalSlot, VffOverrideKind kind, float value)>());
    }

    private static byte[] BuildVffBytes(VffLinkEntry[] links,
        (int globalSlot, VffOverrideKind kind, float value)[] overrides)
    {
        int linkTableLen = links.Length * VffFormat.LinkEntrySize;
        int ovrdTableLen = 0;
        int dataLen = sizeof(float);

        foreach (var ov in overrides)
        {
            ovrdTableLen += 3;
            if (ov.kind == VffOverrideKind.Constant)
                ovrdTableLen += 1 + dataLen;
        }

        int ovrdTableStart = VffFormat.HeaderSize + linkTableLen;
        int totalLen = ovrdTableStart + ovrdTableLen;
        var buf = new byte[totalLen];

        buf[0] = (byte)'V'; buf[1] = (byte)'F'; buf[2] = (byte)'F'; buf[3] = 0;
        buf[4] = 1; buf[5] = (byte)links.Length;
        buf[6] = (byte)overrides.Length; buf[7] = 0;

        for (int i = 0; i < links.Length; i++)
        {
            int off = VffFormat.HeaderSize + i * VffFormat.LinkEntrySize;
            var e = links[i];
            BitConverter.TryWriteBytes(buf.AsSpan(off + 0), e.Hash.XxHash64);
            BitConverter.TryWriteBytes(buf.AsSpan(off + 8), e.Hash.FnvHash64);
            buf[off + 16] = e.ImmCount;
            buf[off + 17] = (byte)(e.InstCount & 0xFF);
            buf[off + 18] = (byte)((e.InstCount >> 8) & 0xFF);
            buf[off + 19] = (byte)e.Type;
            buf[off + 20] = (byte)(e.VarSlotCount & 0xFF);
            buf[off + 21] = (byte)((e.VarSlotCount >> 8) & 0xFF);
        }

        int ovOff = ovrdTableStart;
        foreach (var ov in overrides)
        {
            buf[ovOff] = (byte)(ov.globalSlot & 0xFF);
            buf[ovOff + 1] = (byte)((ov.globalSlot >> 8) & 0xFF);
            buf[ovOff + 2] = (byte)ov.kind;
            ovOff += 3;

            if (ov.kind == VffOverrideKind.Constant)
            {
                buf[ovOff] = (byte)dataLen;
                ovOff++;
                var valBytes = BitConverter.GetBytes(ov.value);
                Array.Copy(valBytes, 0, buf, ovOff, dataLen);
                ovOff += dataLen;
            }
        }

        return buf;
    }

    private static (DualHash64 hash, GCHandle handle) PutInCache(byte[] data)
    {
        var hash = DualHash64.Compute(data);
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        FormulaCache.Instance.Put(hash, handle.AddrOfPinnedObject(), data.Length);
        return (hash, handle);
    }

    // ═══════════════════════════════════════════════════════
    // 单 link 解析
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_SingleFormulaLink_ReturnsChain()
    {
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("10 + 5"));
        byte[] fBytes = formula.ToBytes();
        var (fHash, fHandle) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash,
                immCount: (byte)header.ImmediateCount,
                instCount: (ushort)header.Count,
                type: FluxType.Formula,
                varSlotCount: (ushort)header.VarSlotCount);

            byte[] vffBytes = BuildVffBytes(new[] { link });
            var (vffHash, vffHandle) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(result.Formula.IsChained, Is.True);
                Assert.That(result.Formula.ChainLength, Is.EqualTo(1));
                Assert.That(result.Overrides.Length, Is.EqualTo(0));
            }
            finally { vffHandle.Free(); }
        }
        finally { fHandle.Free(); }
    }

    [Test]
    public void Resolve_TwoFormulaLinks_ReturnsChainLength2()
    {
        var lexer = CreateMathLexer();
        var fA = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("3 + 4"));
        var fB = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("5 + 6"));

        byte[] bA = fA.ToBytes(), bB = fB.ToBytes();
        var (hA, gcA) = PutInCache(bA);
        var (hB, gcB) = PutInCache(bB);

        try
        {
            var hdrA = FormulaFormat.ReadHeader(bA);
            var hdrB = FormulaFormat.ReadHeader(bB);
            var links = new[]
            {
                new VffLinkEntry(hA, (byte)hdrA.ImmediateCount, (ushort)hdrA.Count, FluxType.Formula, (ushort)hdrA.VarSlotCount),
                new VffLinkEntry(hB, (byte)hdrB.ImmediateCount, (ushort)hdrB.Count, FluxType.Formula, (ushort)hdrB.VarSlotCount),
            };

            byte[] vffBytes = BuildVffBytes(links);
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(result.Formula.ChainLength, Is.EqualTo(2));
            }
            finally { gcVff.Free(); }
        }
        finally { gcA.Free(); gcB.Free(); }
    }

    [Test]
    public void Resolve_ModifierLink_ReturnsModifierChain()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("2 * 3"));
        var modifier = f.ToMultiplier();
        byte[] fBytes = modifier.ToBytes();
        var (fHash, gc) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Modifier, (ushort)header.VarSlotCount);
            byte[] vffBytes = BuildVffBytes(new[] { link });
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(result.Formula.ChainLength, Is.EqualTo(1));
            }
            finally { gcVff.Free(); }
        }
        finally { gc.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 嵌套 VFF (递归展开)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_NestedVff_FlattensLinks()
    {
        var lexer = CreateMathLexer();
        var fInner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("1 + 2"));
        byte[] innerBytes = fInner.ToBytes();
        var (innerHash, gcInner) = PutInCache(innerBytes);

        try
        {
            var hdr = FormulaFormat.ReadHeader(innerBytes);
            var innerLink = new VffLinkEntry(innerHash, (byte)hdr.ImmediateCount,
                (ushort)hdr.Count, FluxType.Formula, (ushort)hdr.VarSlotCount);
            byte[] innerVffBytes = BuildVffBytes(new[] { innerLink });
            var (innerVffHash, gcInnerVff) = PutInCache(innerVffBytes);

            try
            {
                var outerLink = new VffLinkEntry(innerVffHash, immCount: 0, instCount: 0,
                    type: FluxType.Formula, varSlotCount: 0);
                byte[] outerVffBytes = BuildVffBytes(new[] { outerLink });
                var (outerVffHash, gcOuterVff) = PutInCache(outerVffBytes);

                try
                {
                    var result = VffFormat.Resolve<float, FloatOp>(outerVffHash);
                    Assert.That(result.Formula.ChainLength, Is.EqualTo(1));

                    var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
                    float r = runner.Instantiate(result.Formula).Run();
                    Assert.That(r, Is.EqualTo(3f).Within(1e-6f));
                }
                finally { gcOuterVff.Free(); }
            }
            finally { gcInnerVff.Free(); }
        }
        finally { gcInner.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 变量槽的 VFF 解析
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_WithVariableSlots_MergesThem()
    {
        var lexer = TestHelper.CreateVarLexer("[", "]");
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("[a] + [b]"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gc) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Formula, (ushort)header.VarSlotCount);
            byte[] vffBytes = BuildVffBytes(new[] { link });
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
                var inst = runner.Instantiate(result.Formula).Set("a", 7f).Set("b", 3f);
                Assert.That(inst.Run(), Is.EqualTo(10f).Within(1e-6f));
            }
            finally { gcVff.Free(); }
        }
        finally { gc.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 错误路径
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_NotInCache_Throws()
    {
        var fakeHash = DualHash64.Compute(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.That(() => VffFormat.Resolve<float, FloatOp>(fakeHash),
            Throws.InvalidOperationException.With.Message.Contains("not found"));
    }

    [Test]
    public void Resolve_NotVff_Throws()
    {
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("42"));
        byte[] fBytes = formula.ToBytes();
        var (fHash, gc) = PutInCache(fBytes);
        try
        {
            Assert.That(() => VffFormat.Resolve<float, FloatOp>(fHash),
                Throws.InvalidOperationException.With.Message.Contains("not a VFF"));
        }
        finally { gc.Free(); }
    }

    [Test]
    public void Resolve_LinkNotFound_Throws()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("10"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gc) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var bogusHash = DualHash64.Compute(new byte[] { 0xFF, 0xEE, 0xDD });
            var link = new VffLinkEntry(bogusHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Formula, (ushort)header.VarSlotCount);
            byte[] vffBytes = BuildVffBytes(new[] { link });
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                Assert.That(() => VffFormat.Resolve<float, FloatOp>(vffHash),
                    Throws.InvalidOperationException.With.Message.Contains("not in cache"));
            }
            finally { gcVff.Free(); }
        }
        finally { gc.Free(); }
    }

    [Test]
    public void Resolve_UnsupportedVersion_Throws()
    {
        byte[] badVersion = new byte[VffFormat.HeaderSize];
        badVersion[0] = (byte)'V'; badVersion[1] = (byte)'F';
        badVersion[2] = (byte)'F'; badVersion[3] = 0;
        badVersion[4] = 99; badVersion[5] = 0; badVersion[6] = 0; badVersion[7] = 0;

        var (hash, gc) = PutInCache(badVersion);
        try
        {
            Assert.That(() => VffFormat.Resolve<float, FloatOp>(hash),
                Throws.InvalidOperationException.With.Message.Contains("Unsupported VFF version"));
        }
        finally { gc.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 带 Override 的 VFF 解析
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_WithInjectOverrides_IncludesOverridesInResult()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("10 + 5"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gcF) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Formula, (ushort)header.VarSlotCount);
            var overrides = new[] {
                (globalSlot: 0, kind: VffOverrideKind.Inject, value: 0f),
                (globalSlot: 1, kind: VffOverrideKind.Inject, value: 0f),
            };

            byte[] vffBytes = BuildVffBytes(new[] { link }, overrides);
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(result.Overrides.Length, Is.EqualTo(2));
                Assert.That(result.Overrides[0].Kind, Is.EqualTo(VffOverrideKind.Inject));
                Assert.That(result.Overrides[1].Kind, Is.EqualTo(VffOverrideKind.Inject));
                Assert.That(result.Overrides[0].GlobalSlot, Is.EqualTo(0));
                Assert.That(result.Overrides[1].GlobalSlot, Is.EqualTo(1));
            }
            finally { gcVff.Free(); }
        }
        finally { gcF.Free(); }
    }

    [Test]
    public void Resolve_WithConstantOverride_HasValue()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("42"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gcF) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Formula, (ushort)header.VarSlotCount);
            var overrides = new[] {
                (globalSlot: 3, kind: VffOverrideKind.Constant, value: 3.14f),
            };

            byte[] vffBytes = BuildVffBytes(new[] { link }, overrides);
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var result = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(result.Overrides.Length, Is.EqualTo(1));
                Assert.That(result.Overrides[0].Kind, Is.EqualTo(VffOverrideKind.Constant));
                Assert.That(result.Overrides[0].ConstantValue, Is.EqualTo(3.14f));
                Assert.That(result.Overrides[0].GlobalSlot, Is.EqualTo(3));
            }
            finally { gcVff.Free(); }
        }
        finally { gcF.Free(); }
    }

    [Test]
    public void Resolve_OverrideDataLenMismatch_Throws()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("42"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gcF) = PutInCache(fBytes);

        try
        {
            var header = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)header.ImmediateCount,
                (ushort)header.Count, FluxType.Formula, (ushort)header.VarSlotCount);

            int totalLen = VffFormat.HeaderSize + VffFormat.LinkEntrySize + 3 + 1 + 4;
            var buf = new byte[totalLen];
            buf[0] = (byte)'V'; buf[1] = (byte)'F'; buf[2] = (byte)'F'; buf[3] = 0;
            buf[4] = 1; buf[5] = 1; buf[6] = 1; buf[7] = 0;
            int loff = VffFormat.HeaderSize;
            BitConverter.TryWriteBytes(buf.AsSpan(loff), link.Hash.XxHash64);
            BitConverter.TryWriteBytes(buf.AsSpan(loff + 8), link.Hash.FnvHash64);
            buf[loff + 16] = link.ImmCount;
            buf[loff + 19] = (byte)link.Type;
            int ooff = loff + VffFormat.LinkEntrySize;
            buf[ooff + 2] = (byte)VffOverrideKind.Constant;
            buf[ooff + 3] = 99; // wrong dataLen

            var (vffHash, gcVff) = PutInCache(buf);
            try
            {
                Assert.That(() => VffFormat.Resolve<float, FloatOp>(vffHash),
                    Throws.InvalidOperationException.With.Message.Contains("data length mismatch"));
            }
            finally { gcVff.Free(); }
        }
        finally { gcF.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 嵌套 VFF — 变量槽偏移 (cumImm > 0)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_NestedVff_WithVariableSlots_OffsetsSlotIndex()
    {
        // 外层 VFF: [const formula (2 imm)] + [inner VFF with variables]
        // inner VFF 的变量槽 SlotIndex 应偏移 const formula 的 ImmediateCount
        var varLexer = CreateVarLexer("[", "]");
        var mathLexer = CreateMathLexer();

        var fVar = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(varLexer.Lex("[a] + [b]"));
        byte[] varBytes = fVar.ToBytes();
        var (varHash, gcVar) = PutInCache(varBytes);

        var fConst = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(mathLexer.Lex("10 + 5"));
        byte[] constBytes = fConst.ToBytes();
        var (constHash, gcConst) = PutInCache(constBytes);

        try
        {
            var varHdr = FormulaFormat.ReadHeader(varBytes);
            // Inner VFF: wraps the variable formula
            var innerLink = new VffLinkEntry(varHash, (byte)varHdr.ImmediateCount,
                (ushort)varHdr.Count, FluxType.Formula, (ushort)varHdr.VarSlotCount);
            byte[] innerVffBytes = BuildVffBytes(new[] { innerLink });
            var (innerVffHash, gcInnerVff) = PutInCache(innerVffBytes);

            try
            {
                var constHdr = FormulaFormat.ReadHeader(constBytes);
                // Outer VFF: const formula first (cumImm = constHdr.ImmediateCount),
                //            then the inner VFF
                var outerLinks = new[]
                {
                    new VffLinkEntry(constHash, (byte)constHdr.ImmediateCount,
                        (ushort)constHdr.Count, FluxType.Formula, (ushort)constHdr.VarSlotCount),
                    new VffLinkEntry(innerVffHash, immCount: 0, instCount: 0,
                        type: FluxType.Formula, varSlotCount: 2),
                };
                byte[] outerVffBytes = BuildVffBytes(outerLinks);
                var (outerVffHash, gcOuterVff) = PutInCache(outerVffBytes);

                try
                {
                    var result = VffFormat.Resolve<float, FloatOp>(outerVffHash);
                    var slots = result.Formula.VariableSlots;

                    Assert.That(slots.Length, Is.EqualTo(2));
                    Assert.That(slots[0].Name, Is.EqualTo("a"));
                    Assert.That(slots[0].SlotIndex, Is.EqualTo(constHdr.ImmediateCount),
                        "SlotIndex should be offset by preceding immediates");
                    Assert.That(slots[1].Name, Is.EqualTo("b"));
                    Assert.That(slots[1].SlotIndex, Is.EqualTo(constHdr.ImmediateCount + 1));
                }
                finally { gcOuterVff.Free(); }
            }
            finally { gcInnerVff.Free(); }
        }
        finally { gcVar.Free(); gcConst.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 嵌套 VFF — Override GlobalSlot 偏移
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_NestedVff_WithOverrides_OffsetsGlobalSlot()
    {
        // 外层 VFF: [const formula] + [inner VFF with override]
        // inner VFF 的 override GlobalSlot 应偏移外层累积的 immediates
        var mathLexer = CreateMathLexer();

        var fInner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(mathLexer.Lex("42"));
        byte[] innerBytes = fInner.ToBytes();
        var (innerHash, gcInner) = PutInCache(innerBytes);

        var fConst = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(mathLexer.Lex("99"));
        byte[] constBytes = fConst.ToBytes();
        var (constHash, gcConst) = PutInCache(constBytes);

        try
        {
            var innerHdr = FormulaFormat.ReadHeader(innerBytes);
            var innerLink = new VffLinkEntry(innerHash, (byte)innerHdr.ImmediateCount,
                (ushort)innerHdr.Count, FluxType.Formula, (ushort)innerHdr.VarSlotCount);
            // Inner VFF: formula + inject override at slot 0
            byte[] innerVffBytes = BuildVffBytes(new[] { innerLink },
                new[] { (globalSlot: 0, kind: VffOverrideKind.Inject, value: 0f) });
            var (innerVffHash, gcInnerVff) = PutInCache(innerVffBytes);

            try
            {
                var constHdr = FormulaFormat.ReadHeader(constBytes);
                var outerLinks = new[]
                {
                    new VffLinkEntry(constHash, (byte)constHdr.ImmediateCount,
                        (ushort)constHdr.Count, FluxType.Formula, (ushort)constHdr.VarSlotCount),
                    new VffLinkEntry(innerVffHash, immCount: 0, instCount: 0,
                        type: FluxType.Formula, varSlotCount: 0),
                };
                byte[] outerVffBytes = BuildVffBytes(outerLinks);
                var (outerVffHash, gcOuterVff) = PutInCache(outerVffBytes);

                try
                {
                    var result = VffFormat.Resolve<float, FloatOp>(outerVffHash);
                    Assert.That(result.Overrides.Length, Is.EqualTo(1));
                    Assert.That(result.Overrides[0].Kind, Is.EqualTo(VffOverrideKind.Inject));
                    Assert.That(result.Overrides[0].GlobalSlot,
                        Is.EqualTo(constHdr.ImmediateCount),
                        "GlobalSlot should be offset by outer VFF's preceding immediates");
                }
                finally { gcOuterVff.Free(); }
            }
            finally { gcInnerVff.Free(); }
        }
        finally { gcInner.Free(); gcConst.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // 错误路径 — 循环引用
    // ═══════════════════════════════════════════════════════
    //
    // 注：内容寻址哈希导致无法构造静态的互相引用循环（A→B→A 要求
    // hashA = Hash([link_to_hashB]) 且 hashB = Hash([link_to_hashA])，
    // 这是一个无解的不动点方程）。真机上的循环检测依赖运行时注入
    // （VFF 递归解析过程中 visited 集合）。此路径由 Unity 端
    // VffRecursiveTests 覆盖（blob 场景下 VFF 可互相引用）。

    [Test]
    public void Resolve_BrokenNestedLink_ThrowsLinkNotFound()
    {
        // 外层 VFF 引用的内层 VFF 在缓存中缺失 → 抛 "not in cache"
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("42"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gcF) = PutInCache(fBytes);

        try
        {
            var hdr = FormulaFormat.ReadHeader(fBytes);
            var link = new VffLinkEntry(fHash, (byte)hdr.ImmediateCount,
                (ushort)hdr.Count, FluxType.Formula, (ushort)hdr.VarSlotCount);
            byte[] innerVffBytes = BuildVffBytes(new[] { link });
            var (innerVffHash, gcInnerVff) = PutInCache(innerVffBytes);

            try
            {
                // 外层 VFF 引用 inner VFF + 一个不存在的链接
                var bogusHash = DualHash64.Compute(new byte[] { 0xDE, 0xAD });
                var outerLinks = new[]
                {
                    new VffLinkEntry(innerVffHash, immCount: 0, instCount: 0,
                        type: FluxType.Formula, varSlotCount: 0),
                    new VffLinkEntry(bogusHash, immCount: 1, instCount: 1,
                        type: FluxType.Formula, varSlotCount: 0),
                };
                byte[] outerVffBytes = BuildVffBytes(outerLinks);
                var (outerVffHash, gcOuterVff) = PutInCache(outerVffBytes);
                try
                {
                    var ex = Assert.Throws<InvalidOperationException>(() =>
                        VffFormat.Resolve<float, FloatOp>(outerVffHash));
                    Assert.That(ex.Message, Does.Contain("not in cache"));
                }
                finally { gcOuterVff.Free(); }
            }
            finally { gcInnerVff.Free(); }
        }
        finally { gcF.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // ToBytes — VFF 编码器
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToBytes_SingleLink_ProducesCorrectLayout()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("10 + 5"));
        byte[] fBytes = f.ToBytes();
        var fHash = DualHash64.Compute(fBytes);
        var header = FormulaFormat.ReadHeader(fBytes);

        var link = new ChainLink
        {
            Key              = fHash,
            Bytecode         = FormulaFormat.GetInstructionSpan(fBytes).ToArray(),
            InstructionCount = f.Count,
            Type             = FluxType.Formula,
            ImmediateCount   = header.ImmediateCount,
            VarSlots         = f.VariableSlots,
            MaxRegister      = f.MaxRegister,
        };

        var overrides = Array.Empty<VffOverride<float>>();
        byte[] vffBytes = VffFormat.ToBytes<float>(new[] { link }, overrides);

        // 验证头部
        Assert.That(vffBytes.Length, Is.GreaterThan(VffFormat.HeaderSize));
        Assert.That(vffBytes[0], Is.EqualTo((byte)'V'));
        Assert.That(vffBytes[1], Is.EqualTo((byte)'F'));
        Assert.That(vffBytes[2], Is.EqualTo((byte)'F'));
        Assert.That(vffBytes[3], Is.EqualTo(0));
        Assert.That(vffBytes[4], Is.EqualTo(1)); // Version
        Assert.That(vffBytes[5], Is.EqualTo(1)); // LinkCount
        Assert.That(vffBytes[6], Is.EqualTo(0)); // OverrideCount
        Assert.That(vffBytes[7], Is.EqualTo(0)); // Flags (no constants)

        // 验证链接表（22 bytes）
        int lo = VffFormat.HeaderSize;
        ulong xxh = BinaryFormat.ReadUInt64LE(vffBytes, lo);
        Assert.That(xxh, Is.EqualTo(fHash.XxHash64));
        ulong fnv = BinaryFormat.ReadUInt64LE(vffBytes, lo + 8);
        Assert.That(fnv, Is.EqualTo(fHash.FnvHash64));
        Assert.That(vffBytes[lo + 16], Is.EqualTo(header.ImmediateCount)); // ImmCount
        Assert.That(vffBytes[lo + 19], Is.EqualTo((byte)FluxType.Formula));
    }

    [Test]
    public void ToBytes_WithConstantOverride_SetsFlag()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("42"));
        byte[] fBytes = f.ToBytes();
        var fHash = DualHash64.Compute(fBytes);
        var header = FormulaFormat.ReadHeader(fBytes);

        var link = new ChainLink
        {
            Key              = fHash,
            Bytecode         = FormulaFormat.GetInstructionSpan(fBytes).ToArray(),
            InstructionCount = f.Count,
            Type             = FluxType.Formula,
            ImmediateCount   = header.ImmediateCount,
            VarSlots         = f.VariableSlots,
            MaxRegister      = f.MaxRegister,
        };

        var overrides = new[] { new VffOverride<float>(0, VffOverrideKind.Constant, 3.14f) };
        byte[] vffBytes = VffFormat.ToBytes<float>(new[] { link }, overrides);

        Assert.That(vffBytes[7], Is.EqualTo(VffFormat.FlagHasConstants)); // Flags
    }

    // ═══════════════════════════════════════════════════════
    // ToBytes → FromBytes 往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Roundtrip_ToBytes_FromBytes_ChainMatches()
    {
        var lexer = CreateMathLexer();
        var fA = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("3 + 4"));
        var fB = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("5 + 6"));
        byte[] bA = fA.ToBytes(), bB = fB.ToBytes();
        var (hA, gcA) = PutInCache(bA);
        var (hB, gcB) = PutInCache(bB);

        try
        {
            var hdrA = FormulaFormat.ReadHeader(bA);
            var hdrB = FormulaFormat.ReadHeader(bB);
            var links = new[]
            {
                new ChainLink
                {
                    Key = hA, Bytecode = FormulaFormat.GetInstructionSpan(bA).ToArray(),
                    InstructionCount = hdrA.Count, Type = FluxType.Formula,
                    ImmediateCount = hdrA.ImmediateCount, VarSlots = fA.VariableSlots,
                    MaxRegister = fA.MaxRegister,
                },
                new ChainLink
                {
                    Key = hB, Bytecode = FormulaFormat.GetInstructionSpan(bB).ToArray(),
                    InstructionCount = hdrB.Count, Type = FluxType.Formula,
                    ImmediateCount = hdrB.ImmediateCount, VarSlots = fB.VariableSlots,
                    MaxRegister = fB.MaxRegister,
                },
            };

            byte[] vffBytes = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                // FromBytes 从裸字节解析
                var fromBytesResult = VffFormat.FromBytes<float, FloatOp>(vffBytes);
                Assert.That(fromBytesResult.Formula.ChainLength, Is.EqualTo(2));

                // Resolve 从缓存解析——结果应一致
                var resolveResult = VffFormat.Resolve<float, FloatOp>(vffHash);
                Assert.That(resolveResult.Formula.ChainLength, Is.EqualTo(2));
            }
            finally { gcVff.Free(); }
        }
        finally { gcA.Free(); gcB.Free(); }
    }

    [Test]
    public void FromBytes_Standalone_SameAsResolve()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def).Compile(lexer.Lex("10 + 5"));
        byte[] fBytes = f.ToBytes();
        var (fHash, gcF) = PutInCache(fBytes);

        try
        {
            var hdr = FormulaFormat.ReadHeader(fBytes);
            var link = new ChainLink
            {
                Key = fHash, Bytecode = FormulaFormat.GetInstructionSpan(fBytes).ToArray(),
                InstructionCount = hdr.Count, Type = FluxType.Formula,
                ImmediateCount = hdr.ImmediateCount, VarSlots = f.VariableSlots,
                MaxRegister = f.MaxRegister,
            };

            byte[] vffBytes = VffFormat.ToBytes<float>(new[] { link }, Array.Empty<VffOverride<float>>());
            var (vffHash, gcVff) = PutInCache(vffBytes);

            try
            {
                var fromBytes = VffFormat.FromBytes<float, FloatOp>(vffBytes);
                var fromResolve = VffFormat.Resolve<float, FloatOp>(vffHash);

                Assert.That(fromBytes.Formula.ChainLength, Is.EqualTo(fromResolve.Formula.ChainLength));
                Assert.That(fromBytes.Overrides.Length, Is.EqualTo(fromResolve.Overrides.Length));

                var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
                float rFromBytes = runner.Instantiate(fromBytes.Formula).Run();
                float rResolve = runner.Instantiate(fromResolve.Formula).Run();
                Assert.That(rFromBytes, Is.EqualTo(rResolve).Within(1e-6f));
            }
            finally { gcVff.Free(); }
        }
        finally { gcF.Free(); }
    }

    // ═══════════════════════════════════════════════════════
    // FromBytes 错误路径
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FromBytes_NotVffMagic_Throws()
    {
        byte[] notVff = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        Assert.That(() => VffFormat.FromBytes<float, FloatOp>(notVff),
            Throws.InvalidOperationException.With.Message.Contains("not a VFF"));
    }
}
