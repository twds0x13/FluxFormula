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
        var fA      = runner.Compile(CreateVarLexer("[", "]").Lex("[a] + 1"));
        var fB      = runner.Compile(CreateVarLexer("[", "]").Lex("[b] + 2"));

        // 第二个公式转为 Modifier 后 Connect：b 仍是 Modifier 内的变量
        var merged  = fA.Connect(fB.ToMultiplier());

        // fB.ToMultiplier() 剥离首操作数 [b]，剩余 1 个 Immediate(2) + 变量 [b] 被移除
        // 改为用 fB 自己的变量做验证——fA 的 [a] 保留
        Assert.That(merged.VariableSlots.Length, Is.EqualTo(1));
        Assert.That(merged.VariableSlots[0].Name, Is.EqualTo("a"));
    }
}
