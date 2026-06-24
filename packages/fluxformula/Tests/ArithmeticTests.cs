using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ArithmeticTests
{
    // ── 基础运算 ─────────────────────────────────

    [Test]
    public void ConstantValue()
        => Assert.That(Eval(new[] { C(42f) }), Is.EqualTo(42f).Within(1e-6f));

    [Test]
    public void SimpleAddition()
        => Assert.That(Eval(new[] { C(1f), Op(FloatOp.Add), C(2f) }), Is.EqualTo(3f).Within(1e-6f));

    [Test]
    public void SimpleSubtraction()
        => Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(3f) }), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void SimpleMultiplication()
        => Assert.That(Eval(new[] { C(4f), Op(FloatOp.Mul), C(5f) }), Is.EqualTo(20f).Within(1e-6f));

    [Test]
    public void SimpleDivision()
        => Assert.That(Eval(new[] { C(10f), Op(FloatOp.Div), C(4f) }), Is.EqualTo(2.5f).Within(1e-6f));

    // ── 优先级 ───────────────────────────────────

    [Test]
    public void MultiplicationHasHigherPrecedence()
    {
        Assert.That(Eval(new[] { C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.Mul), C(3f) }),
            Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void DivisionHasHigherPrecedence()
    {
        Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(6f), Op(FloatOp.Div), C(2f) }),
            Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void SamePrecedenceLeftAssociative()
    {
        Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(3f), Op(FloatOp.Sub), C(2f) }),
            Is.EqualTo(5f).Within(1e-6f));
    }

    // ── 括号 ─────────────────────────────────────

    [Test]
    public void ParenthesesOverridePrecedence()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
            Op(FloatOp.Mul), C(3f),
        }), Is.EqualTo(9f).Within(1e-6f));
    }

    [Test]
    public void NestedParentheses()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen),
                Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
                Op(FloatOp.Mul),
                Op(FloatOp.LParen), C(3f), Op(FloatOp.Add), C(4f), Op(FloatOp.RParen),
            Op(FloatOp.RParen),
        }), Is.EqualTo(21f).Within(1e-6f));
    }

    // ── 一元取负 ─────────────────────────────────

    [Test]
    public void UnaryNegate()
        => Assert.That(Eval(new[] { Op(FloatOp.Neg), C(5f) }), Is.EqualTo(-5f).Within(1e-6f));

    [Test]
    public void DoubleNegate()
        => Assert.That(Eval(new[] { Op(FloatOp.Neg), Op(FloatOp.Neg), C(5f) }), Is.EqualTo(5f).Within(1e-6f));

    [Test]
    public void NegateWithMultiplication()
    {
        Assert.That(Eval(new[] { Op(FloatOp.Neg), C(3f), Op(FloatOp.Mul), C(4f) }),
            Is.EqualTo(-12f).Within(1e-6f));
    }

    // ── 复杂表达式 ───────────────────────────────

    [Test]
    public void ComplexExpression()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
            Op(FloatOp.Mul),
            Op(FloatOp.LParen), C(3f), Op(FloatOp.Add), C(4f), Op(FloatOp.RParen),
            Op(FloatOp.Sub),
            C(5f), Op(FloatOp.Mul), C(2f),
        }), Is.EqualTo(11f).Within(1e-6f));
    }

    // ── 错误条件 ─────────────────────────────────

    [Test]
    public void DivisionByZero_ReturnsNaN()
    {
        Assert.That(float.IsNaN(Eval(new[] { C(1f), Op(FloatOp.Div), C(0f) })), Is.True);
    }

    [Test]
    public void DivisionByZero_NaNShortCircuits()
    {
        float r = Eval(new[] { C(1f), Op(FloatOp.Div), C(0f), Op(FloatOp.Add), C(5f) });
        Assert.That(float.IsNaN(r), Is.True, "NaN should short-circuit");
    }

    [Test]
    public void UnmatchedRightParenthesis_Throws()
    {
        Assert.That(
            () => Eval(new[] { Op(FloatOp.RParen), C(1f), Op(FloatOp.Add), C(2f) }),
            Throws.TypeOf<FormatException>()
        );
    }

    [Test]
    public void UnmatchedLeftParenthesis_Throws()
    {
        Assert.That(
            () => Eval(new[] { Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f) }),
            Throws.TypeOf<FormatException>()
        );
    }
}
