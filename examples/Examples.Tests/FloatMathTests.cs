using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class FloatMathTests
{
    // ═══════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════

    private static readonly MathDef Def = default;

    private static FluxLexer<float> CreateLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralOper = (byte)MathOp.Const,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s, CultureInfo.InvariantCulture)),
            Operators =
            {
                new("+", (byte)MathOp.Add),
                new("-", (byte)MathOp.Sub),
                new("*", (byte)MathOp.Mul),
                new("/", (byte)MathOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }

    private static float EvalStr(string expr, bool jit = false)
    {
        var lexer   = CreateLexer();
        var runner  = new FluxAssembler<float, MathDef>(Def);
        var lexResult = lexer.Lex(expr);
        var formula = runner.Compile(lexResult);
        return runner.Instantiate(formula, jit).Run();
    }

    // ═══════════════════════════════════════════════════════
    // 单算符正确性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Const_ReturnsValue()
        => Assert.That(EvalStr("42"), Is.EqualTo(42f).Within(1e-6f));

    [Test]
    public void Add_TwoNumbers()
        => Assert.That(EvalStr("1 + 2"), Is.EqualTo(3f).Within(1e-6f));

    [Test]
    public void Sub_TwoNumbers()
        => Assert.That(EvalStr("10 - 3"), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void Mul_TwoNumbers()
        => Assert.That(EvalStr("4 * 5"), Is.EqualTo(20f).Within(1e-6f));

    [Test]
    public void Div_TwoNumbers()
        => Assert.That(EvalStr("10 / 4"), Is.EqualTo(2.5f).Within(1e-6f));

    [Test]
    public void Neg_UnaryMinus()
        => Assert.That(EvalStr("-5"), Is.EqualTo(-5f).Within(1e-6f));

    [Test]
    public void Neg_DoubleNegate()
        => Assert.That(EvalStr("--5"), Is.EqualTo(5f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：优先级
    // ═══════════════════════════════════════════════════════

    [Test]
    public void MulHigherThanAdd()
        => Assert.That(EvalStr("1 + 2 * 3"), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void DivHigherThanSub()
        => Assert.That(EvalStr("10 - 6 / 2"), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void SamePrecedence_LeftAssociative()
        => Assert.That(EvalStr("10 - 3 - 2"), Is.EqualTo(5f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：括号
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Parentheses_OverridePrecedence()
        => Assert.That(EvalStr("(1 + 2) * 3"), Is.EqualTo(9f).Within(1e-6f));

    [Test]
    public void Parentheses_Nested()
        => Assert.That(EvalStr("((1 + 2) * (3 + 4))"), Is.EqualTo(21f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：一元负号
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Neg_WithMultiplication()
        => Assert.That(EvalStr("-3 * 4"), Is.EqualTo(-12f).Within(1e-6f));

    [Test]
    public void Neg_InParentheses()
        => Assert.That(EvalStr("-(1 + 2)"), Is.EqualTo(-3f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：复杂表达式
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Complex_MixedPrecedence()
        => Assert.That(EvalStr("(1 + 2) * (3 + 4) - 5 * 2"), Is.EqualTo(11f).Within(1e-6f));

    [Test]
    public void Complex_NestedNegAndParens()
        => Assert.That(EvalStr("-((2 + 3) * 4)"), Is.EqualTo(-20f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 边界与错误
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DivByZero_ReturnsNaN()
    {
        float result = EvalStr("1 / 0");
        Assert.That(float.IsNaN(result), Is.True);
    }

    [Test]
    public void DivByZero_NaN_ShortCircuits()
    {
        float result = EvalStr("1 / 0 + 5");
        Assert.That(float.IsNaN(result), Is.True);
    }

    [Test]
    public void UnmatchedLeftParen_Throws()
    {
        Assert.That(() => EvalStr("(1 + 2"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void UnmatchedRightParen_Throws()
    {
        Assert.That(() => EvalStr("1 + 2)"), Throws.TypeOf<FormatException>());
    }

    // ═══════════════════════════════════════════════════════
    // JIT 一致性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterpreter_Add()
        => Assert.That(EvalStr("1 + 2", jit: true), Is.EqualTo(EvalStr("1 + 2", jit: false)).Within(1e-6f));

    [Test]
    public void Jit_MatchesInterpreter_Mixed()
        => Assert.That(EvalStr("(1 + 2) * 3 - 4 / 2", jit: true),
            Is.EqualTo(EvalStr("(1 + 2) * 3 - 4 / 2", jit: false)).Within(1e-6f));

    [Test]
    public void Jit_DivByZero_AlsoReturnsNaN()
    {
        float jitResult = EvalStr("1 / 0", jit: true);
        Assert.That(float.IsNaN(jitResult), Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // 变量注入
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Variables_SetByName()
    {
        var lexer     = CreateLexer();
        var runner    = new FluxAssembler<float, MathDef>(Def);
        var lexResult = lexer.Lex("[atk] * 2 + [bonus]");
        var formula   = runner.Compile(lexResult);
        float result  = runner.Instantiate(formula)
            .Set("atk", 150f)
            .Set("bonus", 25f)
            .Run();
        Assert.That(result, Is.EqualTo(325f).Within(1e-6f));
    }

    [Test]
    public void Variables_SetByIndex()
    {
        var lexer     = CreateLexer();
        var runner    = new FluxAssembler<float, MathDef>(Def);
        var lexResult = lexer.Lex("[x] + [y]");
        var formula   = runner.Compile(lexResult);

        // 变量按首次出现顺序分配 slot：x=slot 0, y=slot 1
        float result = runner.Instantiate(formula)
            .SetIndex(0, 10f)
            .SetIndex(1, 20f)
            .Run();
        Assert.That(result, Is.EqualTo(30f).Within(1e-6f));
    }
}
