using System;
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
        var f = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("1 + 2 + 3 + 4 + 5"));
        float result = new FluxAssembler<float, FloatMathDef>(Def)
            .Instantiate(f).Run();
        Assert.That(result, Is.EqualTo(15f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // IFluxDefinition DIM
    // ═══════════════════════════════════════════════════════

    private readonly struct BareDef : IFluxDefinition<float>
    {
        public byte GetReturnOp() => 0;
        public int GetArity(byte op) => 0;
        public OpType GetKind(byte op) => OpType.Instruction;
        public int GetPrecedence(byte op) => 0;
        public OpPair GetPair(byte op) => default;
        public Associativity GetAssociativity(byte op) => Associativity.Left;
        public float Compute(byte op, Instruction inst, ReadOnlySpan<float> registers) => 0;
        public byte ResolveToken(byte oper, TokenContext context) => oper;
        // Does NOT override GetOperatorName — exercises the DIM
    }

    [Test]
    public void GetOperatorName_DIM_ReturnsNullForUnknownOp()
    {
        IFluxDefinition<float> def = new BareDef();
        Assert.That(def.GetOperatorName(0), Is.Null);
    }
}
