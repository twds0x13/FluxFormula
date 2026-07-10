using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class ILInlineTests
{
    // ═══════════════════════════════════════════════════════
    // IL definition (EmitOp for Add/Mul, fallback Compute for others)
    // ═══════════════════════════════════════════════════════

    private static readonly FloatMathILDef ILDef = default;

    private static FluxLexer<float> CreateLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralOper   = (byte)FloatOp.Const,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s, CultureInfo.InvariantCulture)),
            Operators =
            {
                new("+", (byte)FloatOp.Add),
                new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul),
                new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }

    private static float EvalIL(string expr, bool jit = false)
    {
        var lexer     = CreateLexer();
        var runner    = new FluxAssembler<float, FloatMathILDef>(ILDef);
        var lexResult = lexer.Lex(expr);
        var formula   = runner.Compile(lexResult);
        return runner.Instantiate(formula, jit).Run();
    }

    // ═══════════════════════════════════════════════════════
    // Single-operator correctness (IL inline for Add/Mul, Compute for Sub/Div/Neg)
    // ═══════════════════════════════════════════════════════

    [Test] public void Add_TwoNumbers()          => Assert.That(EvalIL("1 + 2"), Is.EqualTo(3f));
    [Test] public void Sub_TwoNumbers()          => Assert.That(EvalIL("5 - 3"), Is.EqualTo(2f));
    [Test] public void Mul_TwoNumbers()          => Assert.That(EvalIL("3 * 4"), Is.EqualTo(12f));
    [Test] public void Div_TwoNumbers()          => Assert.That(EvalIL("10 / 4"), Is.EqualTo(2.5f));
    [Test] public void Neg_SingleNumber()        => Assert.That(EvalIL("(-5)"), Is.EqualTo(-5f));

    // ═══════════════════════════════════════════════════════
    // Precedence and parentheses
    // ═══════════════════════════════════════════════════════

    [Test] public void Precedence_MulBeforeAdd() => Assert.That(EvalIL("1 + 2 * 3"), Is.EqualTo(7f));
    [Test] public void Precedence_DivBeforeSub() => Assert.That(EvalIL("10 - 4 / 2"), Is.EqualTo(8f));
    [Test] public void Precedence_NegBeforeMul() => Assert.That(EvalIL("(-3) * 4"), Is.EqualTo(-12f));
    [Test] public void Parens_Override()         => Assert.That(EvalIL("(1 + 2) * 3"), Is.EqualTo(9f));
    [Test] public void Parens_Nested()           => Assert.That(EvalIL("((3 + 4) * 2) - 5"), Is.EqualTo(9f));

    // ═══════════════════════════════════════════════════════
    // Variable injection
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Var_Injection_AddMul()
    {
        var lexer     = CreateLexer();
        var runner    = new FluxAssembler<float, FloatMathILDef>(ILDef);
        var formula   = runner.Compile(lexer.Lex("[x] * [y] + 10"));
        float r = runner.Instantiate(formula, jit: true).Set("x", 3f).Set("y", 4f).Run();
        Assert.That(r, Is.EqualTo(22f));
    }

    [Test]
    public void Var_Injection_SubDiv()
    {
        var lexer     = CreateLexer();
        var runner    = new FluxAssembler<float, FloatMathILDef>(ILDef);
        var formula   = runner.Compile(lexer.Lex("[a] / [b] - 1"));
        float r = runner.Instantiate(formula, jit: true).Set("a", 20f).Set("b", 2f).Run();
        Assert.That(r, Is.EqualTo(9f));
    }

    // ═══════════════════════════════════════════════════════
    // IL vs Expr cross-path: same formula, different definitions
    // ═══════════════════════════════════════════════════════

    private static float EvalExpr(string expr, bool jit = false)
    {
        var lexer = new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralOper   = (byte)MathOp.Const,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s, CultureInfo.InvariantCulture)),
            Operators =
            {
                new("+", (byte)MathOp.Add),
                new("-", (byte)MathOp.Sub),
                new("*", (byte)MathOp.Mul),
                new("/", (byte)MathOp.Div),
            },
            Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
            VariablePatterns = { new("[", "]") },
        });
        var runner = new FluxAssembler<float, MathDef>(default(MathDef));
        var formula = runner.Compile(lexer.Lex(expr));
        return runner.Instantiate(formula, jit).Run();
    }

    [Test] public void ILvsExpr_Add()         => Assert.That(EvalIL("10 + 20", jit: true), Is.EqualTo(EvalExpr("10 + 20", jit: true)));
    [Test] public void ILvsExpr_Mul()         => Assert.That(EvalIL("7 * 8", jit: true),   Is.EqualTo(EvalExpr("7 * 8", jit: true)));
    [Test] public void ILvsExpr_Sub()         => Assert.That(EvalIL("15 - 6", jit: true),   Is.EqualTo(EvalExpr("15 - 6", jit: true)));
    [Test] public void ILvsExpr_Div()         => Assert.That(EvalIL("30 / 4", jit: true),   Is.EqualTo(EvalExpr("30 / 4", jit: true)));
    [Test] public void ILvsExpr_Neg()         => Assert.That(EvalIL("(-9)", jit: true),     Is.EqualTo(EvalExpr("(-9)", jit: true)));
    [Test] public void ILvsExpr_Mixed()       => Assert.That(EvalIL("1 + 2 * 3 - 4 / 2", jit: true), Is.EqualTo(EvalExpr("1 + 2 * 3 - 4 / 2", jit: true)));
    [Test] public void ILvsExpr_Parens()      => Assert.That(EvalIL("(3 + 5) * (2 + 1)", jit: true), Is.EqualTo(EvalExpr("(3 + 5) * (2 + 1)", jit: true)));

    // ═══════════════════════════════════════════════════════
    // JIT vs Interpreter consistency
    // ═══════════════════════════════════════════════════════

    [Test] public void Jit_MatchesInterp_Add()    => Assert.That(EvalIL("1 + 2", jit: true), Is.EqualTo(EvalIL("1 + 2", jit: false)));
    [Test] public void Jit_MatchesInterp_Mixed()  => Assert.That(EvalIL("1 + 2 * 3 - 4 / 2", jit: true), Is.EqualTo(EvalIL("1 + 2 * 3 - 4 / 2", jit: false)));
    [Test] public void Jit_MatchesInterp_Neg()    => Assert.That(EvalIL("(-5) + 3", jit: true), Is.EqualTo(EvalIL("(-5) + 3", jit: false)));

    // ═══════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Div_ByZero_ReturnsNaN()
    {
        float r = EvalIL("1 / 0", jit: true);
        Assert.That(float.IsNaN(r));
    }
}
