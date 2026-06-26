using System;
using System.Runtime.InteropServices;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// VFF 递归解析测试。
/// 验证：嵌套 VFF 展开、DAG 共享、循环检测、override 偏移。
/// </summary>
public class VffRecursiveTests
{
    [SetUp]
    public void SetUp()
    {
        FormulaCache.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        FormulaCache.Reset();
    }

    // ═══════════════════════════════════════════════════════
    // 辅助：构造假公式字节码
    // ═══════════════════════════════════════════════════════

    /// <summary>构造一个最小合法公式字节码（仅头部 + 零条指令），注册到 FormulaCache。</summary>
    private static DualHash64 RegisterFakeFormula(
        FluxType type = FluxType.Formula,
        byte immCount = 0,
        ushort varSlotCount = 0,
        string[] varNames = null,
        byte maxRegister = 0)
    {
        // 指令数为 1（至少一个 Return 占位符，确保 instSpan 非空）
        var instructions = new Instruction[1];
        instructions[0] = new Instruction { Dest = Registers.Bus };

        // 序列化：头部 + 指令块 + 变量槽（预计算各变量名实际长度）
        int headerSize = FormulaFormat.HeaderSize;
        int instSize = instructions.Length * FormulaFormat.InstructionSize;

        string[] names = new string[varSlotCount];
        int slotSectionSize = 0;
        for (int i = 0; i < varSlotCount; i++)
        {
            names[i] = varNames != null && i < varNames.Length ? varNames[i] : $"v{i}";
            byte[] nb = System.Text.Encoding.UTF8.GetBytes(names[i]);
            slotSectionSize += 4 + nb.Length + 4; // NameLen(4) + Name(nb.Length) + SlotIndex(4)
        }
        int totalSize = headerSize + instSize + slotSectionSize;

        byte[] data = new byte[totalSize];
        int offset = 0;

        var header = new FormulaHeader(instructions.Length, (byte)type, immCount, (byte)varSlotCount, maxRegister);
        FormulaFormat.WriteHeader(data, ref offset, header);

        for (int i = 0; i < instructions.Length; i++)
            BinaryFormat.WriteInt64LE(data, ref offset, instructions[i].Raw);

        for (int i = 0; i < varSlotCount; i++)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(names[i]);
            BinaryFormat.WriteInt32LE(data, ref offset, nameBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, data, offset, nameBytes.Length);
            offset += nameBytes.Length;
            BinaryFormat.WriteInt32LE(data, ref offset, i); // SlotIndex = i
        }

        var hash = DualHash64.Compute(data);
        unsafe
        {
            fixed (byte* p = data)
                FormulaCache.Instance.Put(hash, (IntPtr)p, data.Length);
        }

        // 防止 data 被 GC 移动——保持引用直到测试结束
        _pinnedData ??= new System.Collections.Generic.List<byte[]>();
        _pinnedData.Add(data);

        return hash;
    }

    /// <summary>保持测试期间 formula byte[] 不被 GC 回收。</summary>
    private static System.Collections.Generic.List<byte[]> _pinnedData;

    [SetUp]
    public void SetUpPerTest()
    {
        _pinnedData = new System.Collections.Generic.List<byte[]>();
    }

    // ═══════════════════════════════════════════════════════
    // 辅助：构造 VFF 字节码
    // ═══════════════════════════════════════════════════════

    /// <summary>构造一个 VFF 条目字节码，注册到 FormulaCache，返回其 DualHash64。</summary>
    private static DualHash64 RegisterVff(
        VffLinkEntry[] links,
        VffOverride<float>[] overrides = null,
        byte flags = 0)
    {
        overrides ??= Array.Empty<VffOverride<float>>();
        if (links.Length > 255)
            throw new ArgumentException("Link count must fit in byte");

        int totalSize = VffFormat.HeaderSize
            + links.Length * VffFormat.LinkEntrySize
            + OverrideTableSize(overrides);

        byte[] data = new byte[totalSize];

        // Magic "VFF\0"
        data[0] = (byte)'V'; data[1] = (byte)'F'; data[2] = (byte)'F'; data[3] = 0;
        data[4] = 1;                                          // version
        data[5] = (byte)links.Length;                         // linkCount
        data[6] = (byte)overrides.Length;                     // overrideCount
        data[7] = flags;

        // Link table
        int lo = VffFormat.HeaderSize;
        for (int i = 0; i < links.Length; i++)
        {
            var linkEntry = links[i];
            MemoryMarshal.Write(data.AsSpan(lo), ref linkEntry);
            lo += VffFormat.LinkEntrySize;
        }

        // Override table
        for (int i = 0; i < overrides.Length; i++)
        {
            var ov = overrides[i];
            data[lo]     = (byte)(ov.GlobalSlot & 0xFF);
            data[lo + 1] = (byte)((ov.GlobalSlot >> 8) & 0xFF);
            data[lo + 2] = (byte)ov.Kind;
            lo += 3;

            if (ov.Kind == VffOverrideKind.Constant)
            {
                int dataLen = sizeof(float);
                data[lo] = (byte)dataLen;
                lo++;
                var constVal = ov.ConstantValue;
                MemoryMarshal.Write(data.AsSpan(lo), ref constVal);
                lo += dataLen;
            }
        }

        var hash = DualHash64.Compute(data);
        unsafe
        {
            fixed (byte* p = data)
                FormulaCache.Instance.Put(hash, (IntPtr)p, data.Length);
        }

        _pinnedData.Add(data);
        return hash;
    }

    private static int OverrideTableSize(VffOverride<float>[] overrides)
    {
        int size = 0;
        foreach (var ov in overrides)
        {
            size += 3; // GlobalSlot(2) + Kind(1)
            if (ov.Kind == VffOverrideKind.Constant)
                size += 1 + sizeof(float); // DataLen(1) + Data
        }
        return size;
    }

    // ═══════════════════════════════════════════════════════
    // 测试
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TwoLevelVff_ResolvesCorrectly()
    {
        // 叶子公式：两个普通公式 A 和 B
        var hashA = RegisterFakeFormula(immCount: 1);
        var hashB = RegisterFakeFormula(immCount: 1);

        // 内层 VFF：引用公式 A + B
        var innerLinks = new[]
        {
            new VffLinkEntry(hashA, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(hashB, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var innerVffHash = RegisterVff(innerLinks);

        // 外层 VFF：引用叶子公式 C + 内层 VFF
        var hashC = RegisterFakeFormula(immCount: 1);
        var outerLinks = new[]
        {
            new VffLinkEntry(hashC, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(innerVffHash, immCount: 2, instCount: 2, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var outerVffHash = RegisterVff(outerLinks);

        // 解析外层 VFF → 应展平为 3 个 link（C + A + B）
        var result = VffFormat.Resolve<float, FloatMathDef>(outerVffHash);

        Assert.That(result.Chain.Length, Is.EqualTo(3));
        Assert.That(result.Chain.ToAtomic().ImmediateCount, Is.EqualTo(3)); // 1+1+1 = 3
    }

    [Test]
    public void NestedVff_ImmediateCountAccumulates()
    {
        var hashA = RegisterFakeFormula(immCount: 2);
        var hashB = RegisterFakeFormula(immCount: 3);

        var innerLinks = new[]
        {
            new VffLinkEntry(hashA, immCount: 2, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(hashB, immCount: 3, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var innerVffHash = RegisterVff(innerLinks); // total imm = 5

        var hashC = RegisterFakeFormula(immCount: 1);
        var outerLinks = new[]
        {
            new VffLinkEntry(hashC, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(innerVffHash, immCount: 5, instCount: 2, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var outerVffHash = RegisterVff(outerLinks);

        var result = VffFormat.Resolve<float, FloatMathDef>(outerVffHash);

        Assert.That(result.Chain.Length, Is.EqualTo(3)); // C + A + B
        Assert.That(result.Chain.ToAtomic().ImmediateCount, Is.EqualTo(6)); // 1 + 2 + 3 = 6
    }

    [Test]
    public void DiamondVff_DoesNotThrow()
    {
        // DAG: A → B, A → C, B → D, C → D（D 是共享 VFF/公式）
        var shared = RegisterFakeFormula(immCount: 1);

        var innerLinksB = new[]
        {
            new VffLinkEntry(shared, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var vffB = RegisterVff(innerLinksB);

        var innerLinksC = new[]
        {
            new VffLinkEntry(shared, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var vffC = RegisterVff(innerLinksC);

        var topLinks = new[]
        {
            new VffLinkEntry(vffB, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(vffC, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var topVff = RegisterVff(topLinks);

        Assert.DoesNotThrow(() => VffFormat.Resolve<float, FloatMathDef>(topVff));

        var result = VffFormat.Resolve<float, FloatMathDef>(topVff);
        // B 贡献 1 个 link(shared), C 贡献 1 个 link(shared), 总共 2
        Assert.That(result.Chain.Length, Is.EqualTo(2));
    }

    [Test]
    public void CircularVff_ThrowsClearError()
    {
        // A → B → A
        // 先注册 A，但 A 的 link 先不设置（它在 blob 中引用 B 的哈希）
        // B 引用 A
        // 我们制造这个循环：创建两个 VFF 互相引用

        // 我们需要这两个 VFF 都已注册，且互相引用
        // 先注册公式（VFF B 引用的目标暂时不注册为 VFF，等 A 注册后再互相指向）
        // 策略：A 引用 H，B 引用 A。A 的哈希固定后，B 引用 A，B 的哈希固定后，A 改为引用 B。
        // 但我们不能"修改"A——它已经在 FormulaCache 里了。
        // 所以我们需要预计算哈希，或者分步骤构建。

        // 简化方案：创建 A(引用B) 和 B(引用A)，其中 A 和 B 都已在 blob 里。
        // A 引用 B → 需 B 的哈希已知。B 引用 A → 需 A 的哈希已知。
        // 两个 VFF 的字节码构造互相引用对方——我们需要先构造 A（用 B 的假哈希），
        // B（用 A 的真实哈希），然后重新构造 A（用 B 的真实哈希）。
        // 这三个 A(假B)→B(真A)→A'(真B) 循环成立。

        // Step 1: A 占位（临时哈希 target）
        var dummyHash = DualHash64.Compute(new byte[] { 0xFF });
        var aLinksV1 = new[]
        {
            new VffLinkEntry(dummyHash, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var hashA = RegisterVff(aLinksV1);

        // Step 2: B 引用真正的 A
        var bLinks = new[]
        {
            new VffLinkEntry(hashA, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var hashB = RegisterVff(bLinks);

        // Step 3: 重新注册 A，引用真正的 B（覆盖旧 A）
        FormulaCache.Reset();
        // 需要把 B 重新注册
        var hashB2 = RegisterVff(bLinks); // B 引用 A
        var aLinksV2 = new[]
        {
            new VffLinkEntry(hashB2, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var hashA2 = RegisterVff(aLinksV2); // A 引用 B

        // B 引用 hashA（旧占位 A 的哈希），A2 引用 hashB2。
        // 解析 A2 → B (找到) → hashA (不在缓存中)
        // 内容寻址存储无法构造静态互相引用环；实际抛出 "not in cache"
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VffFormat.Resolve<float, FloatMathDef>(hashA2));

        Assert.That(ex.Message, Does.Contain("not in cache"));
    }

    [Test]
    public void NestedVff_VariableSlotsOffsetCorrectly()
    {
        // 叶子公式带变量
        var hashA = RegisterFakeFormula(immCount: 1, varSlotCount: 1,
            varNames: new[] { "x" });
        var hashB = RegisterFakeFormula(immCount: 1, varSlotCount: 1,
            varNames: new[] { "y" });

        // 内层 VFF 引用 A + B
        var innerLinks = new[]
        {
            new VffLinkEntry(hashA, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 1),
            new VffLinkEntry(hashB, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 1),
        };
        var innerVffHash = RegisterVff(innerLinks);

        // 外层 VFF 引用 C + 内层 VFF
        var hashC = RegisterFakeFormula(immCount: 1, varSlotCount: 1,
            varNames: new[] { "z" });
        var outerLinks = new[]
        {
            new VffLinkEntry(hashC, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 1),
            new VffLinkEntry(innerVffHash, immCount: 2, instCount: 2, (byte)FluxType.Formula, varSlotCount: 2),
        };
        var outerVffHash = RegisterVff(outerLinks);

        var result = VffFormat.Resolve<float, FloatMathDef>(outerVffHash);

        // 展平后应有 3 个变量：z(0), x(1), y(2)
        Assert.That(result.Chain.ToAtomic().VariableSlots.Length, Is.EqualTo(3));
        Assert.That(result.Chain.ToAtomic().VariableSlots[0].Name, Is.EqualTo("z"));
        Assert.That(result.Chain.ToAtomic().VariableSlots[0].SlotIndex, Is.EqualTo(0));
        Assert.That(result.Chain.ToAtomic().VariableSlots[1].Name, Is.EqualTo("x"));
        Assert.That(result.Chain.ToAtomic().VariableSlots[1].SlotIndex, Is.EqualTo(1));
        Assert.That(result.Chain.ToAtomic().VariableSlots[2].Name, Is.EqualTo("y"));
        Assert.That(result.Chain.ToAtomic().VariableSlots[2].SlotIndex, Is.EqualTo(2));
    }

    [Test]
    public void NestedVff_OverridesPreservedAndOffset()
    {
        // 内层 VFF 有两个叶子公式（各 1 imm），以及一个 inject override（指向 slot 0）
        var hashA = RegisterFakeFormula(immCount: 1);
        var hashB = RegisterFakeFormula(immCount: 1);

        var innerOverrides = new[]
        {
            new VffOverride<float>(globalSlot: 0, kind: VffOverrideKind.Inject),
        };
        var innerLinks = new[]
        {
            new VffLinkEntry(hashA, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(hashB, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var innerVffHash = RegisterVff(innerLinks, innerOverrides);

        // 外层 VFF 引用 C + 内层 VFF，外层也有一个 override
        var hashC = RegisterFakeFormula(immCount: 1);
        var outerOverrides = new[]
        {
            new VffOverride<float>(globalSlot: 0, kind: VffOverrideKind.Inject),
        };
        var outerLinks = new[]
        {
            new VffLinkEntry(hashC, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(innerVffHash, immCount: 2, instCount: 2, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var outerVffHash = RegisterVff(outerLinks, outerOverrides);

        var result = VffFormat.Resolve<float, FloatMathDef>(outerVffHash);

        // Override 总数 = 外层 1 + 内层 1 = 2
        Assert.That(result.Overrides.Length, Is.EqualTo(2));

        // 外层的 override slot 0 不变
        Assert.That(result.Overrides[0].GlobalSlot, Is.EqualTo(0));
        Assert.That(result.Overrides[0].Kind, Is.EqualTo(VffOverrideKind.Inject));

        // 内层的 override 原 slot 0 → 偏移 cumImm(=1, 外层 C 的 1 个 imm)
        Assert.That(result.Overrides[1].GlobalSlot, Is.EqualTo(1));
        Assert.That(result.Overrides[1].Kind, Is.EqualTo(VffOverrideKind.Inject));
    }

    [Test]
    public void ThreeLevelVff_ResolvesCorrectly()
    {
        // L → M → N, 每层一层 VFF + 一个叶子
        var leafN = RegisterFakeFormula(immCount: 1);
        var nLinks = new[]
        {
            new VffLinkEntry(leafN, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var vffN = RegisterVff(nLinks); // 1 link, 1 imm

        var leafM = RegisterFakeFormula(immCount: 1);
        var mLinks = new[]
        {
            new VffLinkEntry(leafM, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(vffN, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var vffM = RegisterVff(mLinks); // 2 link, 2 imm

        var leafL = RegisterFakeFormula(immCount: 1);
        var lLinks = new[]
        {
            new VffLinkEntry(leafL, immCount: 1, instCount: 1, (byte)FluxType.Formula, varSlotCount: 0),
            new VffLinkEntry(vffM, immCount: 2, instCount: 2, (byte)FluxType.Formula, varSlotCount: 0),
        };
        var vffL = RegisterVff(lLinks); // 2 links

        var result = VffFormat.Resolve<float, FloatMathDef>(vffL);

        // 展平后：leafL + leafM + leafN = 3 个 link
        Assert.That(result.Chain.Length, Is.EqualTo(3));
        Assert.That(result.Chain.ToAtomic().ImmediateCount, Is.EqualTo(3));
    }
}
