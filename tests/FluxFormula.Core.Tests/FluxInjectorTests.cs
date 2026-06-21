using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FluxInjector 构造器、值注入、值检索测试。
/// </summary>
public unsafe class FluxInjectorTests
{
    [Test]
    public void Constructor_FromInstructionArray()
    {
        var payload = new Instruction[2];
        payload[0] = new Instruction { OpCode = 1, Dest = 2 };
        payload[1] = new Instruction { OpCode = 3, Dest = 4 };

        var injector = new FluxInjector<float>(payload, null, System.Array.Empty<VariableSlot>());
        Assert.That(injector.ToString(), Does.Contain("FluxInjector"));
    }

    [Test]
    public void SetIndex_ValidIndex()
    {
        var lexer = CreateVarLexer("[", "]");
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("[x] + [y]"));
        var inst = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Instantiate(f).Set("x", 99f).Set("y", 1f);
        Assert.That(inst.Run(), Is.EqualTo(100f).Within(1e-6f));
    }

    [Test]
    public void GetValue_ReturnsInjectedValue()
    {
        var lexer = CreateVarLexer("[", "]");
        var f = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("[a] + [b] + [c]"));
        var inst = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Instantiate(f).Set("a", 1f).Set("b", 2f).Set("c", 3f);
        Assert.That(inst.Run(), Is.EqualTo(6f).Within(1e-6f));
    }
}
