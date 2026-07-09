using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class AdvMathTests
{
    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    private static readonly AdvMathDef Def = default;

    private static FluxLexer<float> CreateLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralOper = (byte)AdvMathOp.Const,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s, CultureInfo.InvariantCulture)),
            Operators =
            {
                new("+", (byte)AdvMathOp.Add, slots: new sbyte[] { -1, +1 }),
                new("-", (byte)AdvMathOp.Sub, slots: new sbyte[] { -1, +1 }),
                new("*", (byte)AdvMathOp.Mul, slots: new sbyte[] { -1, +1 }),
                new("/", (byte)AdvMathOp.Div, slots: new sbyte[] { -1, +1 }),
                new("select", (byte)AdvMathOp.Select,
                    slots: new sbyte[] { +2, +4, +6 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ","), new(+7, ")") }),
                new("lerp", (byte)AdvMathOp.Lerp,
                    slots: new sbyte[] { +2, +4, +6 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ","), new(+7, ")") }),
                new("max", (byte)AdvMathOp.Max,
                    slots: new sbyte[] { +2, +4 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
                new("min", (byte)AdvMathOp.Min,
                    slots: new sbyte[] { +2, +4 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
                new("step", (byte)AdvMathOp.Step,
                    slots: new sbyte[] { +2, +4 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
                new("?", (byte)AdvMathOp.Question, slots: new sbyte[] { -1, +1, +3 },
                    aux: new AuxRule[] { new(+2, ":") }),
                new(":", (byte)AdvMathOp.Colon),
                new(",", (byte)AdvMathOp.Comma),
            },
            Brackets =
            {
                new("(", ")", (byte)AdvMathOp.LParen, (byte)AdvMathOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }

    private static float Eval(string expr, bool jit = false)
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<float, AdvMathDef>(Def);
        var r = lexer.Lex(expr);
        var f = runner.Compile(r);
        return runner.Instantiate(f, jit).Run();
    }

    // ═══════════════════════════════════════════════════════
    // 单算符：Select
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Select_CondTrue_ReturnsA()
        => Assert.That(Eval("select(1, 100, 200)"), Is.EqualTo(100f).Within(1e-6f));

    [Test]
    public void Select_CondFalse_ReturnsB()
        => Assert.That(Eval("select(0, 100, 200)"), Is.EqualTo(200f).Within(1e-6f));

    [Test]
    public void Select_CondNegative_StillTrue()
        => Assert.That(Eval("select(-1, 100, 200)"), Is.EqualTo(100f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 单算符：Lerp
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Lerp_T0_ReturnsA()
        => Assert.That(Eval("lerp(10, 20, 0)"), Is.EqualTo(10f).Within(1e-6f));

    [Test]
    public void Lerp_T1_ReturnsB()
        => Assert.That(Eval("lerp(10, 20, 1)"), Is.EqualTo(20f).Within(1e-6f));

    [Test]
    public void Lerp_T05_Midpoint()
        => Assert.That(Eval("lerp(0, 100, 0.5)"), Is.EqualTo(50f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 单算符：Max
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Max_FirstLarger()
        => Assert.That(Eval("max(7, 3)"), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void Max_SecondLarger()
        => Assert.That(Eval("max(3, 7)"), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void Max_Equal()
        => Assert.That(Eval("max(5, 5)"), Is.EqualTo(5f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 单算符：Min
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Min_FirstSmaller()
        => Assert.That(Eval("min(3, 7)"), Is.EqualTo(3f).Within(1e-6f));

    [Test]
    public void Min_SecondSmaller()
        => Assert.That(Eval("min(7, 3)"), Is.EqualTo(3f).Within(1e-6f));

    [Test]
    public void Min_Equal()
        => Assert.That(Eval("min(5, 5)"), Is.EqualTo(5f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 单算符：Step
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Step_AboveEdge_Returns1()
        => Assert.That(Eval("step(2, 3)"), Is.EqualTo(1f).Within(1e-6f));

    [Test]
    public void Step_BelowEdge_Returns0()
        => Assert.That(Eval("step(2, 1)"), Is.EqualTo(0f).Within(1e-6f));

    [Test]
    public void Step_AtEdge_Returns1()
        => Assert.That(Eval("step(2, 2)"), Is.EqualTo(1f).Within(1e-6f));

    [Test]
    public void Step_NegativeValues()
        => Assert.That(Eval("step(-1, -0.5)"), Is.EqualTo(1f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：Step → Select
    // ═══════════════════════════════════════════════════════

    [Test]
    public void StepSelect_AboveEdge()
        => Assert.That(Eval("select(step(2, 3), 100, 0)"), Is.EqualTo(100f).Within(1e-6f));

    [Test]
    public void StepSelect_BelowEdge()
        => Assert.That(Eval("select(step(2, 1), 100, 0)"), Is.EqualTo(0f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：Step → Lerp
    // ═══════════════════════════════════════════════════════

    [Test]
    public void StepLerp_AboveEdge()
        => Assert.That(Eval("lerp(0, 100, step(0.5, 0.8))"), Is.EqualTo(100f).Within(1e-6f));

    [Test]
    public void StepLerp_BelowEdge()
        => Assert.That(Eval("lerp(0, 100, step(0.5, 0.2))"), Is.EqualTo(0f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：Max/Min Clamp
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Clamp_Above()
        => Assert.That(Eval("max(min(150, 100), 0)"), Is.EqualTo(100f).Within(1e-6f));

    [Test]
    public void Clamp_Below()
        => Assert.That(Eval("max(min(-20, 100), 0)"), Is.EqualTo(0f).Within(1e-6f));

    [Test]
    public void Clamp_InRange()
        => Assert.That(Eval("max(min(50, 100), 0)"), Is.EqualTo(50f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 组合：混合算符
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FunctionNested()
        => Assert.That(Eval("max(select(1, 30, 10), 20)"), Is.EqualTo(30f).Within(1e-6f));

    [Test]
    public void InfixWithFunction()
        => Assert.That(Eval("1 + select(1, 2, 3) * 4"), Is.EqualTo(9f).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // JIT 一致性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterp_Select()
        => Assert.That(Eval("select(1, 100, 200)", jit: true),
            Is.EqualTo(Eval("select(1, 100, 200)", jit: false)).Within(1e-6f));

    [Test]
    public void Jit_MatchesInterp_Lerp()
        => Assert.That(Eval("lerp(0, 100, 0.5)", jit: true),
            Is.EqualTo(Eval("lerp(0, 100, 0.5)", jit: false)).Within(1e-6f));

    [Test]
    public void Jit_MatchesInterp_StepSelect()
        => Assert.That(Eval("select(step(2, 3), 100, 0)", jit: true),
            Is.EqualTo(Eval("select(step(2, 3), 100, 0)", jit: false)).Within(1e-6f));

    [Test]
    public void Jit_MatchesInterp_Clamp()
        => Assert.That(Eval("max(min(150, 100), 0)", jit: true),
            Is.EqualTo(Eval("max(min(150, 100), 0)", jit: false)).Within(1e-6f));

    // ═══════════════════════════════════════════════════════
    // 边界
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Select_MissingArg_UsesDefault()
    {
        // 编译器不校验函数参数数量：缺少的第三个参数槽位为 default(float)=0
        // select(1, 2, 0) = (1 != 0 ? 2 : 0) = 2
        Assert.That(Eval("select(1, 2)"), Is.EqualTo(2f).Within(1e-6f));
    }

    [Test]
    public void Comma_OutsideFunction_Throws()
    {
        Assert.That(() => Eval("1 , 2"), Throws.InstanceOf<Exception>());
    }
}
