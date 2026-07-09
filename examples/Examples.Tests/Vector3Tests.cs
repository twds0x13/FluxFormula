using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class Vector3Tests
{
    private static readonly Vector3Def Def = default;

    private static FluxLexer<Vector3f> CreateLexer()
    {
        return new FluxLexer<Vector3f>(new LexerConfig<Vector3f>
        {
            LiteralOper = (byte)Vector3Op.Const,
            LiteralScanner = LexerConfig<Vector3f>.CreateDefaultNumberScanner(
                s => new Vector3f(float.Parse(s, CultureInfo.InvariantCulture), 0, 0)),
            Operators =
            {
                new("+", (byte)Vector3Op.Add),
                new("-", (byte)Vector3Op.Sub),
                new("*", (byte)Vector3Op.Scale),
            },
            Brackets =
            {
                new("(", ")", (byte)Vector3Op.LParen, (byte)Vector3Op.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }

    private static Vector3f Eval(string expr, bool jit = false)
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<Vector3f, Vector3Def>(Def);
        var r = lexer.Lex(expr);
        var f = runner.Compile(r);
        return runner.Instantiate(f, jit).Run();
    }

    // ═══════════════════════════════════════════════════════
    // Single Operator Correctness
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Const_ReturnsVector()
    {
        var v = Eval("5");
        Assert.That(v.X, Is.EqualTo(5f));
        Assert.That(v.Y, Is.EqualTo(0f));
        Assert.That(v.Z, Is.EqualTo(0f));
    }

    [Test]
    public void Add_TwoVectors()
    {
        var v = Eval("1 + 2");
        Assert.That(v.X, Is.EqualTo(3f));
    }

    [Test]
    public void Sub_TwoVectors()
    {
        var v = Eval("5 - 3");
        Assert.That(v.X, Is.EqualTo(2f));
    }

    [Test]
    public void Scale_VectorByScalar()
    {
        var v = Eval("4 * 3");
        Assert.That(v.X, Is.EqualTo(12f));
    }

    // ═══════════════════════════════════════════════════════
    // Precedence
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ScaleBindsTighterThanAdd()
    {
        var v = Eval("1 + 2 * 3");
        Assert.That(v.X, Is.EqualTo(7f));   // 1 + (2*3), not (1+2)*3
    }

    [Test]
    public void AddAndSubSamePrecedence_LeftAssoc()
    {
        var v = Eval("10 - 3 + 2");
        Assert.That(v.X, Is.EqualTo(9f));   // (10-3)+2
    }

    // ═══════════════════════════════════════════════════════
    // Parentheses
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Parentheses_OverridePrecedence()
    {
        var v = Eval("(1 + 2) * 3");
        Assert.That(v.X, Is.EqualTo(9f));
    }

    [Test]
    public void Parentheses_Nested()
    {
        var v = Eval("(10 - (3 + 2)) * 2");
        Assert.That(v.X, Is.EqualTo(10f));  // (10-5)*2
    }

    // ═══════════════════════════════════════════════════════
    // Variables
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Variables_SetByName()
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<Vector3f, Vector3Def>(Def);
        var r = lexer.Lex("[P0] + [V0] * [t]");
        var f = runner.Compile(r);

        var result = runner.Instantiate(f)
            .Set("P0", new Vector3f(10f, 5f, 0f))
            .Set("V0", new Vector3f(5f, 2f, 0f))
            .Set("t",  new Vector3f(3f, 0f, 0f))
            .Run();

        Assert.That(result.X, Is.EqualTo(25f));
        Assert.That(result.Y, Is.EqualTo(11f));
        Assert.That(result.Z, Is.EqualTo(0f));
    }

    // ═══════════════════════════════════════════════════════
    // JIT Consistency
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterp_Add()
    {
        var interp = Eval("1 + 2", jit: false);
        var jit    = Eval("1 + 2", jit: true);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Mixed()
    {
        var interp = Eval("(1 + 2) * 3 - 5", jit: false);
        var jit    = Eval("(1 + 2) * 3 - 5", jit: true);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_WithVariables()
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<Vector3f, Vector3Def>(Def);
        var r = lexer.Lex("[a] + [b] * [c]");
        var f = runner.Compile(r);

        var vA = new Vector3f(1f, 2f, 3f);
        var vB = new Vector3f(4f, 5f, 6f);
        var vC = new Vector3f(2f, 0f, 0f);

        var interp = runner.Instantiate(f, jit: false)
            .Set("a", vA).Set("b", vB).Set("c", vC).Run();
        var jit = runner.Instantiate(f, jit: true)
            .Set("a", vA).Set("b", vB).Set("c", vC).Run();

        Assert.That(jit, Is.EqualTo(interp));
    }
}
