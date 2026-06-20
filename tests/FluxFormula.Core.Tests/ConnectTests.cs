using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ConnectTests
{
    [Test]
    public void Connect_EmptyToNonEmpty_ReturnsNonEmpty()
    {
        var f42 = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(new[] { C(42f) });
        var connected = FluxFormula<float, FloatOp>.Empty.Connect(f42);
        Assert.That(EvalFormula(connected), Is.EqualTo(42f).Within(1e-6f));
    }

    [Test]
    public void Connect_NonEmptyToEmpty_ReturnsNonEmpty()
    {
        var f42 = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(new[] { C(42f) });
        var connected = f42.Connect(FluxFormula<float, FloatOp>.Empty);
        Assert.That(EvalFormula(connected), Is.EqualTo(42f).Within(1e-6f));
    }

    [Test]
    public void Connect_BothEmpty_ReturnsEmpty()
    {
        var connected = FluxFormula<float, FloatOp>.Empty.Connect(
            FluxFormula<float, FloatOp>.Empty);
        Assert.That(connected.Count, Is.EqualTo(0));
        Assert.That(connected.Raw().Length, Is.EqualTo(0));
    }

    [Test]
    public void Connect_WithVariables_MergesSlots()
    {
        // 连接两个带变量的公式，验证 VariableSlots 正确合并（SlotIndex 偏移）
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lexA    = CreateVarLexer("[", "]").Lex("[a]");
        var lexB    = CreateVarLexer("[", "]").Lex("[b]");
        var fA      = runner.Compile(lexA);
        var fB      = runner.Compile(lexB);
        var merged  = fA.Connect(fB);

        // 合并后共有 2 个变量
        Assert.That(merged.VariableSlots.Length, Is.EqualTo(2));
        Assert.That(merged.ImmediateCount,
            Is.EqualTo(fA.ImmediateCount + fB.ImmediateCount));

        // b 的 SlotIndex 应偏移了 a 的 ImmediateCount
        Assert.That(merged.VariableSlots[1].Name, Is.EqualTo("b"));
        Assert.That(merged.VariableSlots[1].SlotIndex,
            Is.EqualTo(fB.VariableSlots[0].SlotIndex + fA.ImmediateCount));
    }
}
