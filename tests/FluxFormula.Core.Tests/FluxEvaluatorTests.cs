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
        public float Compute(byte op, Instruction inst, Span<float> registers) => 0;
        public byte ResolveToken(byte oper, TokenContext context) => oper;
        // Does NOT override GetOperatorName — exercises the DIM
    }

    [Test]
    public void GetOperatorName_DIM_ReturnsNullForUnknownOp()
    {
        IFluxDefinition<float> def = new BareDef();
        Assert.That(def.GetOperatorName(0), Is.Null);
    }

    // ═══════════════════════════════════════════════════════
    // R0 中断：写入 Error 寄存器立即停止求值
    // ═══════════════════════════════════════════════════════

    struct R0TestData { public int CallCount; }

    enum R0TestOp : byte { Const, Break, Inc, Return }

    readonly struct R0TestDef : IFluxExprDefinition<R0TestData>
    {
        public byte GetReturnOp() => (byte)R0TestOp.Return;
        public int GetArity(byte op) => ((R0TestOp)op) switch { R0TestOp.Break => 1, R0TestOp.Inc => 1, _ => 0 };
        public OpType GetKind(byte op) => ((R0TestOp)op) switch
        {
            R0TestOp.Const => OpType.Immediate, R0TestOp.Return => OpType.Return, _ => OpType.Instruction,
        };
        public int GetPrecedence(byte op) => 0;
        public OpPair GetPair(byte op) => default;
        public Associativity GetAssociativity(byte op) => Associativity.Left;
        public byte ResolveToken(byte oper, TokenContext context) => oper;

        public R0TestData Compute(byte op, Instruction inst, Span<R0TestData> regs)
        {
            var a = regs[inst.Arg0];
            switch ((R0TestOp)op)
            {
                case R0TestOp.Break:
                    return new R0TestData { CallCount = a.CallCount + 1 };
                case R0TestOp.Inc:
                    a.CallCount++;
                    regs[Registers.Error] = a;  // 写入 R0 → 求值器立即终止
                    return a;
                default:
                    return default;
            }
        }

        public System.Linq.Expressions.Expression GetExpression(byte op, Instruction inst,
            System.Linq.Expressions.ParameterExpression[] regs)
        {
            throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// Inc 指令写入 R0 后，后续 Break 指令应被跳过。
    /// 若 R0 短路未生效，Break 会再次递增 CallCount。
    /// </summary>
    [Test]
    public void R0Write_ShortCircuits_SubsequentInstructionsSkipped()
    {
        var runner = new FluxAssembler<R0TestData, R0TestDef>(default);
        // 中缀: [0] Inc Break — Inc 写入 R0，Break 被短路
        var tokens = new FluxToken<R0TestData>[]
        {
            new() { Oper = (byte)R0TestOp.Const, Data = default },
            new() { Oper = (byte)R0TestOp.Inc },
            new() { Oper = (byte)R0TestOp.Break },
        };
        var f = runner.Compile(tokens);
        var result = runner.Instantiate(f, jit: false).Run();
        Assert.That(result.CallCount, Is.EqualTo(1),
            "Inc increments once then writes R0; Break should be skipped, not increment to 2");

        if (!FluxPlatform.IsJitDisabled)
        {
            // JIT 路径同样应短路
            var jitResult = runner.Instantiate(f, jit: true).Run();
            Assert.That(jitResult.CallCount, Is.EqualTo(1),
                "JIT path: Break should also be skipped via R0 short-circuit");
        }
    }
}
