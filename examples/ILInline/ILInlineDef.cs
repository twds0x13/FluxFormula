using System;
using System.Linq.Expressions;
using System.Reflection.Emit;
using FluxFormula.Compiler;
using FluxFormula.Core;

enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}

readonly struct FloatMathILDef : IFluxILDefinition<float>, IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)FloatOp.Return;

    public int GetArity(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _               => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1,
        FloatOp.Mul => 2, FloatOp.Div => 2,
        FloatOp.Neg => 3,
        _            => 0,
    };

    public OpPair GetPair(byte op) => ((FloatOp)op) switch
    {
        FloatOp.LParen => new OpPair { PairRole = Pair.Left },
        FloatOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)FloatOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Neg => Associativity.Right,
        _            => Associativity.Left,
    };

    public OperandPosition GetFirstPosition(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => OperandPosition.Left,
        FloatOp.Sub => OperandPosition.Left,
        FloatOp.Mul => OperandPosition.Left,
        FloatOp.Div => OperandPosition.Left,
        _            => OperandPosition.Right,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)FloatOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)FloatOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((FloatOp)op).ToString();

    // ── Compute (interpreter path) ──
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((FloatOp)op) switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // ── JIT expression tree path ──
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return ((FloatOp)op) switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero),
                nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }

    // ── IL inline emission (Tier B) ──
    // Add and Mul are handled with hand-written IL; other opcodes fall back
    // to the Compute pointer-call path (return false).
    public bool EmitOp(byte op, Instruction inst, ILGenerator il, LocalBuilder regArr)
    {
        // Add: regs[Dest] = regs[Arg0] + regs[Arg1]
        if ((FloatOp)op == FloatOp.Add)
        {
            il.Emit(OpCodes.Ldloc, regArr);          // [arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Dest); // [arr, destIdx]
            il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0); // [arr, destIdx, arr, idx0]
            il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0]]
            il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr[arg0], arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1); // [arr, destIdx, arr[arg0], arr, idx1]
            il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0], arr[arg1]]
            il.Emit(OpCodes.Add);                    // [arr, destIdx, sum]
            il.Emit(OpCodes.Stelem, typeof(float));  // arr[destIdx] = sum; []
            return true;
        }

        // Mul: regs[Dest] = regs[Arg0] * regs[Arg1]
        if ((FloatOp)op == FloatOp.Mul)
        {
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Dest);
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0);
            il.Emit(OpCodes.Ldelem, typeof(float));
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1);
            il.Emit(OpCodes.Ldelem, typeof(float));
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stelem, typeof(float));
            return true;
        }

        // Sub, Div, Neg, etc. → fall back to Compute pointer call
        return false;
    }
}
