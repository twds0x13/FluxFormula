using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ConnectTests
{
    [Test]
    public void Connect_EmptyToModifier_ReturnsFormulaFromModifier()
    {
        var modifier = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(new[] { Op(FloatOp.Add), C(42f) }).ToModifier();
        var chain = FluxFormula<float, FloatMathDef>.Empty.Connect(modifier);
        var atomic = chain.ToAtomic();
        Assert.That(atomic.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Connect_NonEmptyToEmptyModifier_ReturnsSelf()
    {
        var f42 = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(new[] { C(42f) });
        var chain = f42.Connect(FluxModifier<float, FloatMathDef>.Empty);
        Assert.That(EvalFormula(chain), Is.EqualTo(42f).Within(1e-6f));
    }

    [Test]
    public void Connect_BothEmpty_ReturnsEmpty()
    {
        var chain = FluxFormula<float, FloatMathDef>.Empty.Connect(
            FluxModifier<float, FloatMathDef>.Empty);
        Assert.That(chain.Length, Is.EqualTo(0));
    }

    [Test]
    public void Connect_WithVariables_MergesSlots()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA    = CreateVarLexer("[", "]").Lex("[a] + 1");
        var lexB    = CreateVarLexer("[", "]").Lex("[b] + 2");
        var fA      = runner.Compile(lexA);
        var fB      = runner.Compile(lexB);
        var chain   = fA.Connect(fB.ToModifier());
        var merged  = chain.ToAtomic();

        Assert.That(merged.VariableSlots.Length, Is.EqualTo(1));
        Assert.That(merged.VariableSlots[0].Name, Is.EqualTo("a"));
    }
}
