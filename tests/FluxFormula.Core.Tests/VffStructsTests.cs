using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// VFF 相关结构体的构造与字段验证。
/// VffHeader / VffLinkEntry / VffOverride / VffResolveResult
/// </summary>
public class VffStructsTests
{
    // ═══════════════════════════════════════════════════════
    // VffHeader
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VffHeader_Constructed_FieldsMatch()
    {
        var h = new VffHeader(version: 1, linkCount: 3, overrideCount: 2, flags: 0);
        Assert.That(h.Version, Is.EqualTo(1));
        Assert.That(h.LinkCount, Is.EqualTo(3));
        Assert.That(h.OverrideCount, Is.EqualTo(2));
        Assert.That(h.Flags, Is.EqualTo(0));
        Assert.That(h.HasConstants, Is.False);
    }

    [Test]
    public void VffHeader_HasConstants_WhenFlagSet()
    {
        var h = new VffHeader(1, 0, 0, VffFormat.FlagHasConstants);
        Assert.That(h.HasConstants, Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // VffLinkEntry
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VffLinkEntry_Constructed_FieldsMatch()
    {
        var hash = DualHash64.Compute(new byte[] { 0xAB, 0xCD });
        var entry = new VffLinkEntry(hash, immCount: 2, instCount: 10, type: (byte)FluxType.Modifier, varSlotCount: 1);

        Assert.That(entry.Hash, Is.EqualTo(hash));
        Assert.That(entry.ImmCount, Is.EqualTo(2));
        Assert.That(entry.InstCount, Is.EqualTo(10));
        Assert.That(entry.Type, Is.EqualTo((byte)FluxType.Modifier));
        Assert.That(entry.VarSlotCount, Is.EqualTo(1));
    }

    [Test]
    public void VffLinkEntry_FormulaType()
    {
        var entry = new VffLinkEntry(DualHash64.Compute(new byte[] { 1 }), 0, 5, (byte)FluxType.Formula, 0);
        Assert.That(entry.Type, Is.EqualTo((byte)FluxType.Formula));
    }

    // ═══════════════════════════════════════════════════════
    // VffOverride
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VffOverride_InjectKind()
    {
        var ov = new VffOverride<float>(globalSlot: 3, kind: VffOverrideKind.Inject);
        Assert.That(ov.GlobalSlot, Is.EqualTo(3));
        Assert.That(ov.Kind, Is.EqualTo(VffOverrideKind.Inject));
        Assert.That(ov.ConstantValue, Is.EqualTo(0f));
    }

    [Test]
    public void VffOverride_ConstantKind()
    {
        var ov = new VffOverride<float>(globalSlot: 5, kind: VffOverrideKind.Constant, constantValue: 3.14f);
        Assert.That(ov.GlobalSlot, Is.EqualTo(5));
        Assert.That(ov.Kind, Is.EqualTo(VffOverrideKind.Constant));
        Assert.That(ov.ConstantValue, Is.EqualTo(3.14f));
    }

    // ═══════════════════════════════════════════════════════
    // VffResolveResult
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VffResolveResult_WithOverrides()
    {
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(TestHelper.CreateMathLexer().Lex("42"));
        var overrides = new[] { new VffOverride<float>(0, VffOverrideKind.Inject) };

        var result = new VffResolveResult<float, FloatMathDef>(formula, overrides);
        Assert.That(result.Formula.IsChained, Is.False);
        Assert.That(result.Overrides.Length, Is.EqualTo(1));
    }

    [Test]
    public void VffResolveResult_NullOverrides_BecomesEmpty()
    {
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(TestHelper.CreateMathLexer().Lex("1 + 2"));

        var result = new VffResolveResult<float, FloatMathDef>(formula, null);
        Assert.That(result.Overrides, Is.Not.Null);
        Assert.That(result.Overrides.Length, Is.EqualTo(0));
    }

    [Test]
    public void VffResolveResult_ChainFormula()
    {
        var lexer = TestHelper.CreateMathLexer();
        var fA = new FluxAssembler<float, FloatMathDef>(TestHelper.Def).Compile(lexer.Lex("1 + 2"));
        var fB = new FluxAssembler<float, FloatMathDef>(TestHelper.Def).Compile(lexer.Lex("3 + 4")).ToModifier();
        var chain = fA.Connect(fB);

        var result = new VffResolveResult<float, FloatMathDef>(chain, Array.Empty<VffOverride<float>>());
        Assert.That(result.Formula.IsChained, Is.True);
    }
}
