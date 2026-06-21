using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FluxEvaluator 边界路径测试。
/// </summary>
public class FluxEvaluatorTests
{
    [Test]
    public void MaxRegisterZero_UsesDefaultMaxRegisters()
    {
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("1 + 2 + 3 + 4 + 5"));
        float result = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Instantiate(f).Run();
        Assert.That(result, Is.EqualTo(15f).Within(1e-6f));
    }
}
