using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class JitConsistencyTests
{
    [Test]
    public void JitMatchesInterpreter_Simple()
    {
        var tokens = new[] { C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.Mul), C(3f) };
        Assert.That(Eval(tokens, jit: true), Is.EqualTo(Eval(tokens, jit: false)).Within(1e-6f));
    }

    [Test]
    public void JitMatchesInterpreter_Complex()
    {
        var tokens = new[]
        {
            Op(FloatOp.LParen), C(1.5f), Op(FloatOp.Add), C(2.5f), Op(FloatOp.RParen),
            Op(FloatOp.Mul), C(3f), Op(FloatOp.Sub), C(1f),
        };
        Assert.That(Eval(tokens, jit: true), Is.EqualTo(Eval(tokens, jit: false)).Within(1e-6f));
    }

    [Test]
    public void JitMatchesInterpreter_Negate()
    {
        var tokens = new[] { Op(FloatOp.Neg), C(7f), Op(FloatOp.Add), C(3f) };
        Assert.That(Eval(tokens, jit: true), Is.EqualTo(Eval(tokens, jit: false)).Within(1e-6f));
    }

    [Test]
    public void JitMatchesInterpreter_WithVariables()
    {
        var result = CreateVarLexer("[", "]").Lex("[a] * [b] + [c]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);

        var instJit = runner.Instantiate(runner.Compile(result), jit: true);
        var instInt = runner.Instantiate(runner.Compile(result), jit: false);

        float jitVal = instJit.Set("a", 2f).Set("b", 3f).Set("c", 1f).Run();
        float intVal = instInt.Set("a", 2f).Set("b", 3f).Set("c", 1f).Run();

        Assert.That(jitVal, Is.EqualTo(intVal).Within(1e-6f));
        Assert.That(intVal, Is.EqualTo(7f).Within(1e-6f)); // 2*3+1=7
    }
}
