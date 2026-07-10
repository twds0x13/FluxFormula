using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class VffPersistenceTests
{
    private static readonly MathDef Def = default;
    private static readonly FluxLexer<float> Lexer = new(new LexerConfig<float>
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
    private static readonly FluxAssembler<float, MathDef> Runner = new(Def);

    private static void Cache(FluxFormula<float, MathDef> f)
    {
        var b = f.ToBytes();
        FormulaCache.Instance.PutBytes(DualHash64.Compute(b), b);
    }

    // Helper: wrap a single formula in a chain by connecting to [prev] + 0 (identity passthrough)
    private static FluxChain<float, MathDef> SingleLink(FluxFormula<float, MathDef> f)
    {
        var identityF = Runner.Compile(Lexer.Lex("[prev] + 0"));
        var identity  = identityF.ToModifier();
        Cache(identityF);
        return f.Connect(identity);
    }

    // ═══════════════════════════════════════════════════════
    // Basic round-trip: single formula wrapped in a chain
    // ═══════════════════════════════════════════════════════

    [Test]
    public void RoundTrip_SimpleChain_ProducesCorrectResult()
    {
        var f = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        Cache(f);

        var chain    = SingleLink(f);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());

        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);
        float r = Runner.Instantiate(resolved.Chain, jit: false)
            .Set("atk", 100f).Set("mult", 2f).Run();
        Assert.That(r, Is.EqualTo(200f));
    }

    // ═══════════════════════════════════════════════════════
    // Connected chain round-trip: damage * (1 - defReduction)
    // damage = [atk] * [mult], reducer = [prev] - [def] * 0.5
    // Chain semantics: (atk * mult) - def * 0.5
    // ═══════════════════════════════════════════════════════

    [Test]
    public void RoundTrip_ConnectedChain_ProducesCorrectResult()
    {
        var damage   = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        // [prev] is the bus input; [def] is a user variable preserved after ToModifier
        var reducerF = Runner.Compile(Lexer.Lex("[prev] - [def] * 0.5"));
        var reducer  = reducerF.ToModifier();
        Cache(damage); Cache(reducerF);

        var chain    = damage.Connect(reducer);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());

        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);
        float r = Runner.Instantiate(resolved.Chain, jit: false)
            .Set("atk", 100f).Set("mult", 2f).Set("def", 50f).Run();
        Assert.That(r, Is.EqualTo(175f)); // (100*2) - 50*0.5
    }

    // ═══════════════════════════════════════════════════════
    // Empty overrides
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EmptyOverrides_RoundTrip_Works()
    {
        var f = Runner.Compile(Lexer.Lex("[x] + [y]"));
        Cache(f);

        var chain    = SingleLink(f);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float r = Runner.Instantiate(resolved.Chain, jit: false)
            .Set("x", 3f).Set("y", 7f).Run();
        Assert.That(r, Is.EqualTo(10f));
    }

    // ═══════════════════════════════════════════════════════
    // JIT vs Interpreter after round-trip
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterpreter_AfterRoundTrip()
    {
        var damage   = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        var reducerF = Runner.Compile(Lexer.Lex("[prev] - [def] * 0.5"));
        var reducer  = reducerF.ToModifier();
        Cache(damage); Cache(reducerF);

        var chain    = damage.Connect(reducer);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());

        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float interp = Runner.Instantiate(resolved.Chain, jit: false)
            .Set("atk", 50f).Set("mult", 3f).Set("def", 20f).Run();
        float jit = Runner.Instantiate(resolved.Chain, jit: true)
            .Set("atk", 50f).Set("mult", 3f).Set("def", 20f).Run();

        Assert.That(jit, Is.EqualTo(interp));
        Assert.That(interp, Is.EqualTo(140f)); // (50*3) - 20*0.5
    }

    // ═══════════════════════════════════════════════════════
    // Resolve via cache hash
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Resolve_ViaCacheHash_ProducesCorrectResult()
    {
        var f = Runner.Compile(Lexer.Lex("[x] * 2 + [y]"));
        Cache(f);

        var chain    = SingleLink(f);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());

        var vffHash = DualHash64.Compute(vffBytes);
        FormulaCache.Instance.PutBytes(vffHash, vffBytes);

        var resolved = VffFormat.Resolve<float, MathDef>(vffHash);
        float r = Runner.Instantiate(resolved.Chain, jit: false)
            .Set("x", 5f).Set("y", 3f).Run();
        Assert.That(r, Is.EqualTo(13f)); // 5*2 + 3
    }

    // ═══════════════════════════════════════════════════════
    // Instantiate(VffResolveResult): overrides are auto-applied
    // ═══════════════════════════════════════════════════════

    [Test]
    public void InstantiateResolved_Constant_FixesVariable()
    {
        var f = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        Cache(f);

        var chain = SingleLink(f);
        int multSlot = -1;
        foreach (var link in chain.GetLinks())
            foreach (var vs in link.VarSlots)
                if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }
        Assert.That(multSlot, Is.GreaterThanOrEqualTo(0), "mult slot not found");

        var overrides = new[] { new VffOverride<float>(multSlot, VffOverrideKind.Constant, 2f) };
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), overrides);
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float r = Runner.Instantiate(resolved, jit: false)
            .Set("atk", 100f)
            .Run();
        Assert.That(r, Is.EqualTo(200f)); // atk=100, mult=2 (constant)
    }

    [Test]
    public void InstantiateResolved_Constant_JitPath()
    {
        var f = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        Cache(f);

        var chain = SingleLink(f);
        int multSlot = -1;
        foreach (var link in chain.GetLinks())
            foreach (var vs in link.VarSlots)
                if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }

        var overrides = new[] { new VffOverride<float>(multSlot, VffOverrideKind.Constant, 3f) };
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), overrides);
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float r = Runner.Instantiate(resolved, jit: true)
            .Set("atk", 50f)
            .Run();
        Assert.That(r, Is.EqualTo(150f)); // atk=50, mult=3 (constant)
    }

    [Test]
    public void InstantiateResolved_ConnectedChain_MultipleConstants()
    {
        var damage   = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        var reducerF = Runner.Compile(Lexer.Lex("[prev] - [def] * 0.5"));
        var reducer  = reducerF.ToModifier();
        Cache(damage); Cache(reducerF);

        var chain = damage.Connect(reducer);
        // Semantics: (atk * mult) - def * 0.5

        int multSlot = -1, defSlot = -1;
        foreach (var link in chain.GetLinks())
            foreach (var vs in link.VarSlots)
            {
                if (vs.Name == "mult") multSlot = vs.SlotIndex;
                if (vs.Name == "def")  defSlot  = vs.SlotIndex;
            }

        var overrides = new[]
        {
            new VffOverride<float>(multSlot, VffOverrideKind.Constant, 2f),
            new VffOverride<float>(defSlot,  VffOverrideKind.Constant, 30f),
        };
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), overrides);
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float r = Runner.Instantiate(resolved, jit: false)
            .Set("atk", 100f)
            .Run();
        Assert.That(r, Is.EqualTo(185f)); // (100*2) - 30*0.5
    }

    [Test]
    public void InstantiateResolved_JitMatchesInterpreter()
    {
        var damage   = Runner.Compile(Lexer.Lex("[atk] * [mult]"));
        var reducerF = Runner.Compile(Lexer.Lex("[prev] - [def] * 0.5"));
        var reducer  = reducerF.ToModifier();
        Cache(damage); Cache(reducerF);

        var chain = damage.Connect(reducer);
        int multSlot = -1;
        foreach (var link in chain.GetLinks())
            foreach (var vs in link.VarSlots)
                if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }

        var overrides = new[] { new VffOverride<float>(multSlot, VffOverrideKind.Constant, 4f) };
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), overrides);
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        float interp = Runner.Instantiate(resolved, jit: false)
            .Set("atk", 50f).Set("def", 10f)
            .Run();

        float jit = Runner.Instantiate(resolved, jit: true)
            .Set("atk", 50f).Set("def", 10f)
            .Run();

        Assert.That(jit, Is.EqualTo(interp));
        Assert.That(interp, Is.EqualTo(195f)); // (50*4) - 10*0.5
    }

    [Test]
    public void InstantiateResolved_EmptyOverrides_Works()
    {
        var f = Runner.Compile(Lexer.Lex("[x] + [y]"));
        Cache(f);

        var chain    = SingleLink(f);
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), Array.Empty<VffOverride<float>>());
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        // Empty overrides should not affect evaluation
        float r = Runner.Instantiate(resolved, jit: false)
            .Set("x", 10f).Set("y", 20f)
            .Run();
        Assert.That(r, Is.EqualTo(30f));
    }

    [Test]
    public void InstantiateResolved_InjectKind_NoOp()
    {
        var f = Runner.Compile(Lexer.Lex("[x] + [y]"));
        Cache(f);

        var chain = SingleLink(f);
        int xSlot = -1;
        foreach (var link in chain.GetLinks())
            foreach (var vs in link.VarSlots)
                if (vs.Name == "x") { xSlot = vs.SlotIndex; break; }

        var overrides = new[] { new VffOverride<float>(xSlot, VffOverrideKind.Inject, 0f) };
        var vffBytes = VffFormat.ToBytes<float>(chain.GetLinks().ToArray(), overrides);
        var resolved = VffFormat.FromBytes<float, MathDef>(vffBytes);

        // Inject kind should be ignored; caller sets both variables normally
        float r = Runner.Instantiate(resolved, jit: false)
            .Set("x", 7f).Set("y", 8f)
            .Run();
        Assert.That(r, Is.EqualTo(15f));
    }
}
