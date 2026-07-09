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
                new("+", (byte)Vector3Op.Add, slots: new sbyte[] { -1, +1 }),
                new("-", (byte)Vector3Op.Sub, slots: new sbyte[] { -1, +1 }),
                new("*", (byte)Vector3Op.Scale, slots: new sbyte[] { -1, +1 }),
                new("x", (byte)Vector3Op.Cross, slots: new sbyte[] { -1, +1 }),
                new("cross", (byte)Vector3Op.Cross,
                    slots: new sbyte[] { +2, +4 },
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
                new("dot", (byte)Vector3Op.Dot,
                    slots: new sbyte[] { +2, +4 },
                    aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
                new("norm", (byte)Vector3Op.Norm,
                    slots: new sbyte[] { +2 },
                    aux: new AuxRule[] { new(+1, "("), new(+3, ")") }),
                new(",", (byte)Vector3Op.Comma),
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

    private static void AssertVec(Vector3f v, float x, float y, float z)
    {
        Assert.That(v.X, Is.EqualTo(x).Within(1e-6f));
        Assert.That(v.Y, Is.EqualTo(y).Within(1e-6f));
        Assert.That(v.Z, Is.EqualTo(z).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // Single Operator Correctness
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Const_ReturnsVector()
        => AssertVec(Eval("5"), 5, 0, 0);

    [Test]
    public void Add_TwoVectors()
        => AssertVec(Eval("1 + 2"), 3, 0, 0);

    [Test]
    public void Sub_TwoVectors()
        => AssertVec(Eval("5 - 3"), 2, 0, 0);

    [Test]
    public void Scale_VectorByScalar()
        => AssertVec(Eval("4 * 3"), 12, 0, 0);

    [Test]
    public void Cross_Infix()
        => AssertVec(Eval("[a] x [b]",
            jit: false,
            a: new(1, 0, 0),
            b: new(0, 1, 0)),
            0, 0, 1);

    [Test]
    public void Cross_FunctionSyntax()
        => AssertVec(Eval("cross([a], [b])",
            jit: false,
            a: new(1, 0, 0),
            b: new(0, 1, 0)),
            0, 0, 1);

    [Test]
    public void Norm_UnitVector()
    {
        var v = Eval("norm([v])", jit: false, v: new Vector3f(3, 4, 0));
        AssertVec(v, 0.6f, 0.8f, 0f);
    }

    [Test]
    public void Norm_ZeroVector_ReturnsDefault()
    {
        var v = Eval("norm([v])", jit: false, v: new Vector3f(0, 0, 0));
        AssertVec(v, 0, 0, 0);
    }

    [Test]
    public void Dot_ReturnsResultInX()
    {
        var v = Eval("dot([a], [b])",
            jit: false,
            a: new(1, 2, 3),
            b: new(4, 5, 6));
        Assert.That(v.X, Is.EqualTo(32f));  // 1*4 + 2*5 + 3*6
        Assert.That(v.Y, Is.EqualTo(0f));
        Assert.That(v.Z, Is.EqualTo(0f));
    }

    // ═══════════════════════════════════════════════════════
    // Combinations
    // ═══════════════════════════════════════════════════════

    [Test]
    public void CrossNorm_Compose()
    {
        var v = Eval("norm([a]) x [b]",
            jit: false,
            a: new(3, 4, 0),
            b: new(0, 0, 1));
        AssertVec(v, 0.8f, -0.6f, 0f);
    }

    [Test]
    public void CrossAndScale_MixedPrecedence()
        => AssertVec(Eval("[a] x [b] * 2",
            jit: false,
            a: new(1, 0, 0),
            b: new(0, 1, 0)),
            0, 0, 2);  // Cross and Scale same precedence, left-assoc

    // ═══════════════════════════════════════════════════════
    // Parentheses
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Parentheses_OverridePrecedence()
        => AssertVec(Eval("(1 + 2) * 3"), 9, 0, 0);

    [Test]
    public void Parentheses_CrossWithAdd()
    {
        var a = new Vector3f(1, 0, 0);
        var b = new Vector3f(0, 1, 0);
        var c = new Vector3f(0, 0, 1);
        // [a] + [b] x [c] = (1,0,0) + (1,0,0) = (2,0,0)
        AssertVec(Eval("[a] + [b] x [c]", jit: false, a: a, b: b, c: c), 2, 0, 0);
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
    public void Jit_MatchesInterp_Cross()
    {
        var args = (a: new Vector3f(1, 0, 0), b: new Vector3f(0, 1, 0));
        var interp = Eval("[a] x [b]", jit: false, a: args.a, b: args.b);
        var jit    = Eval("[a] x [b]", jit: true, a: args.a, b: args.b);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Norm()
    {
        var v = new Vector3f(3, 4, 0);
        var interp = Eval("norm([v])", jit: false, v: v);
        var jit    = Eval("norm([v])", jit: true, v: v);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Dot()
    {
        var args = (a: new Vector3f(1, 2, 3), b: new Vector3f(4, 5, 6));
        var interp = Eval("dot([a], [b])", jit: false, a: args.a, b: args.b);
        var jit    = Eval("dot([a], [b])", jit: true, a: args.a, b: args.b);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Composed()
    {
        var args = (a: new Vector3f(3, 4, 0), b: new Vector3f(0, 0, 1));
        var interp = Eval("norm([a]) x [b]", jit: false, a: args.a, b: args.b);
        var jit    = Eval("norm([a]) x [b]", jit: true, a: args.a, b: args.b);
        Assert.That(jit, Is.EqualTo(interp));
    }

    // ═══════════════════════════════════════════════════════
    // Helpers (overload with named variable injection)
    // ═══════════════════════════════════════════════════════

    private static Vector3f Eval(string expr, bool jit, Vector3f a = default, Vector3f b = default, Vector3f c = default, Vector3f v = default)
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<Vector3f, Vector3Def>(Def);
        var r = lexer.Lex(expr);
        var f = runner.Compile(r);
        var inst = runner.Instantiate(f, jit);
        if (a != default) inst = inst.Set("a", a);
        if (b != default) inst = inst.Set("b", b);
        if (c != default) inst = inst.Set("c", c);
        if (v != default) inst = inst.Set("v", v);
        return inst.Run();
    }
}
