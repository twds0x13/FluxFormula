using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class DamageMultiverseTests
{
    // ═══════════════════════════════════════════════════════
    // PCG64
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Pcg64_SameSeed_SameSequence()
    {
        var a = new Pcg64(42);
        var b = new Pcg64(42);
        for (int i = 0; i < 100; i++)
            Assert.That(a.NextFloat(), Is.EqualTo(b.NextFloat()));
    }

    [Test]
    public void Pcg64_DifferentSeed_DifferentSequence()
    {
        var a = new Pcg64(42);
        var b = new Pcg64(99);
        int same = 0;
        for (int i = 0; i < 20; i++)
            if (a.NextFloat() == b.NextFloat()) same++;
        Assert.That(same, Is.LessThan(20)); // probabilistic, but extremely unlikely all match
    }

    [Test]
    public void Pcg64_NextFloat_InRange()
    {
        var rng = new Pcg64(123);
        for (int i = 0; i < 1000; i++)
        {
            float v = rng.NextFloat();
            Assert.That(v, Is.GreaterThanOrEqualTo(0f));
            Assert.That(v, Is.LessThan(1f));
        }
    }

    // ═══════════════════════════════════════════════════════
    // DamageDef: basic arithmetic + select
    // ═══════════════════════════════════════════════════════

    private static readonly DamageDef Def = default;
    private static readonly FluxLexer<float> Lexer = CreateLexer();

    private static FluxLexer<float> CreateLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralOper   = (byte)DamageOp.Const,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s, CultureInfo.InvariantCulture)),
            Operators =
            {
                new("+", (byte)DamageOp.Add, slots: new sbyte[] { -1, +1 }),
                new("-", (byte)DamageOp.Sub, slots: new sbyte[] { -1, +1 }),
                new("*", (byte)DamageOp.Mul, slots: new sbyte[] { -1, +1 }),
                new("/", (byte)DamageOp.Div, slots: new sbyte[] { -1, +1 }),
                new("?", (byte)DamageOp.Question),
                new(":", (byte)DamageOp.Colon),
                new(",", (byte)DamageOp.Comma),
                new("select", (byte)DamageOp.Select,
                    slots: new sbyte[] { +2, +4, +6 }, bracketOpen: "(", bracketClose: ")",
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ","), new(+7, ")") }),
            },
            Brackets =
            {
                new("(", ")", (byte)DamageOp.LParen, (byte)DamageOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }

    private static readonly FluxAssembler<float, DamageDef> Runner = new(Def);

    [Test]
    public void Arithmetic_Simple()
    {
        var f = Runner.Compile(Lexer.Lex("1 + 2 * 3"));
        Assert.That(Runner.Instantiate(f).Run(), Is.EqualTo(7f));
    }

    [Test]
    public void Select_FunctionSyntax_True()
    {
        var f = Runner.Compile(Lexer.Lex("select(1, 10, 20)"));
        float r = Runner.Instantiate(f).Run();
        Assert.That(r, Is.EqualTo(10f)); // condition=1 (true) → trueVal=10
    }

    [Test]
    public void Select_FunctionSyntax_False()
    {
        var f = Runner.Compile(Lexer.Lex("select(0, 10, 20)"));
        float r = Runner.Instantiate(f).Run();
        Assert.That(r, Is.EqualTo(20f)); // condition=0 (false) → falseVal=20
    }

    [Test]
    public void Select_TernarySyntax()
    {
        var f = Runner.Compile(Lexer.Lex("[x] ? 100 : 200"));
        float r = Runner.Instantiate(f).Set("x", 1f).Run();
        Assert.That(r, Is.EqualTo(100f));
        float r2 = Runner.Instantiate(f).Set("x", 0f).Run();
        Assert.That(r2, Is.EqualTo(200f));
    }

    [Test]
    public void DamageFormula_NoCrit()
    {
        var f = Runner.Compile(Lexer.Lex(
            "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));
        float r = Runner.Instantiate(f)
            .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", 0f)
            .Run();
        Assert.That(r, Is.EqualTo(100f)); // atk * 1
    }

    [Test]
    public void DamageFormula_Crit()
    {
        var f = Runner.Compile(Lexer.Lex(
            "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));
        float r = Runner.Instantiate(f)
            .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", 1f)
            .Run();
        Assert.That(r, Is.EqualTo(150f)); // 100 * (1 + 0.5)
    }

    [Test]
    public void JitConsistency_Select()
    {
        var f = Runner.Compile(Lexer.Lex(
            "select([a], [b] + [c], [d] * 2)"));
        float interp = Runner.Instantiate(f, jit: false)
            .Set("a", 1f).Set("b", 3f).Set("c", 4f).Set("d", 0f)
            .Run();
        float jit = Runner.Instantiate(f, jit: true)
            .Set("a", 1f).Set("b", 3f).Set("c", 4f).Set("d", 0f)
            .Run();
        Assert.That(jit, Is.EqualTo(interp)); // select(1, 7, 0) = 7
    }

    // ═══════════════════════════════════════════════════════
    // Multiverse
    // ═══════════════════════════════════════════════════════

    private FluxCurryEvaluator<float, DamageDef> PrepareCurry()
    {
        var f = Runner.Compile(Lexer.Lex(
            "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));
        return FluxCurryEvaluator<float, DamageDef>.Create(Def, f)
            .Bind("atk", 100f)
            .Bind("critDmg", 0.5f);
    }

    [Test]
    public void Multiverse_Threshold_Always()
    {
        var curry = PrepareCurry();
        var rng = new Pcg64(42);
        float avg = curry.Multiverse("isCrit", count: 1000, critRate: 1f, rng);
        Assert.That(avg, Is.EqualTo(150f).Within(0.01f)); // 100% crit → all 150
    }

    [Test]
    public void Multiverse_Threshold_Never()
    {
        var curry = PrepareCurry();
        var rng = new Pcg64(42);
        float avg = curry.Multiverse("isCrit", count: 1000, critRate: 0f, rng);
        Assert.That(avg, Is.EqualTo(100f).Within(0.01f)); // 0% crit → all 100
    }

    [Test]
    public void Multiverse_Delegate()
    {
        var curry = PrepareCurry();
        var rng = new Pcg64(42);
        int callCount = 0;
        float avg = curry.Multiverse("isCrit", count: 1000, rng =>
        {
            callCount++;
            return callCount % 2 == 0; // every other hit crits
        }, rng);
        // Half crit (150) + half non-crit (100) = 125
        Assert.That(avg, Is.EqualTo(125f).Within(1f));
    }

    [Test]
    public void Multiverse_Deterministic()
    {
        var curry = PrepareCurry();
        var rngA = new Pcg64(42);
        float avgA = curry.Multiverse("isCrit", count: 500, critRate: 0.3f, rngA);

        var rngB = new Pcg64(42);
        float avgB = curry.Multiverse("isCrit", count: 500, critRate: 0.3f, rngB);

        Assert.That(avgA, Is.EqualTo(avgB)); // same seed → same result
    }

    [Test]
    public void Multiverse_JitMatchesInterpreter()
    {
        // Multiverse uses interpreter path (ForceComplete).
        // Verify the underlying formula works on both paths.
        var f = Runner.Compile(Lexer.Lex(
            "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));
        float interp = Runner.Instantiate(f, jit: false)
            .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", 1f).Run();
        float jit = Runner.Instantiate(f, jit: true)
            .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", 1f).Run();
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Multiverse_DoesNotMutateOriginal()
    {
        var curry = PrepareCurry();
        Assert.That(curry.BoundCount, Is.EqualTo(2));
        Assert.That(curry.IsCompleted, Is.False);

        var rng = new Pcg64(42);
        curry.Multiverse("isCrit", count: 100, critRate: 0.5f, rng);

        // Original should be unchanged
        Assert.That(curry.BoundCount, Is.EqualTo(2));
        Assert.That(curry.IsCompleted, Is.False);
    }
}
