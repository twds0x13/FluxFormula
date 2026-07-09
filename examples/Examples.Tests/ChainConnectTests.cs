using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class ChainConnectTests
{
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
        });
    }

    private static FluxAssembler<float, AdvMathDef> Runner() => new(Def);

    [Test]
    public void Connect_Basic()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var fA = runner.Compile(lexer.Lex("10 + 5"));
        var fB = runner.Compile(lexer.Lex("2 * 2")).ToModifier();
        float r = runner.Instantiate(fA.Connect(fB)).Run();
        Assert.That(r, Is.EqualTo(30f).Within(1e-6f));
    }

    [Test]
    public void Ternary_StepToTernary()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var stepHi = runner.Compile(lexer.Lex("step(0.5, 0.8)"));
        var stepLo = runner.Compile(lexer.Lex("step(0.5, 0.2)"));
        var tern   = runner.Compile(lexer.Lex("? 100 : 0")).ToModifier();

        float rHi = runner.Instantiate(stepHi.Connect(tern)).Run();
        float rLo = runner.Instantiate(stepLo.Connect(tern)).Run();
        Assert.That(rHi, Is.EqualTo(100f).Within(1e-6f));
        Assert.That(rLo, Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void Chain_MultipleModifiers()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var base_ = runner.Compile(lexer.Lex("1 + 2"));
        var chain = base_
            .Connect(runner.Compile(lexer.Lex("3 * 2")).ToModifier())
            .Connect(runner.Compile(lexer.Lex("2 + 1")).ToModifier());
        float r = runner.Instantiate(chain).Run();
        Assert.That(r, Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void Roundtrip_ToModifier_ToFormula()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var f = runner.Compile(lexer.Lex("7 + 3"));
        var mod = f.ToModifier();
        var restored = mod.ToFormula("x");
        float r = runner.Instantiate(restored).Set("x", 7f).Run();
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void ToAtomic_MatchesPerLink()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var fA = runner.Compile(lexer.Lex("10 + 5"));
        var fB = runner.Compile(lexer.Lex("2 * 2")).ToModifier();
        var chain = fA.Connect(fB);
        float perLink = runner.Instantiate(chain).Run();
        float atomic  = runner.Instantiate(chain.ToAtomic()).Run();
        Assert.That(perLink, Is.EqualTo(atomic).Within(1e-6f));
    }

    [Test]
    public void Jit_MatchesInterp_Connect()
    {
        var lexer  = CreateLexer();
        var runner = Runner();
        var fA = runner.Compile(lexer.Lex("10 + 5"));
        var fB = runner.Compile(lexer.Lex("2 * 2")).ToModifier();
        var chain = fA.Connect(fB);
        float jit = runner.Instantiate(chain, jit: true).Run();
        float interp = runner.Instantiate(chain, jit: false).Run();
        Assert.That(jit, Is.EqualTo(interp).Within(1e-6f));
    }
}
