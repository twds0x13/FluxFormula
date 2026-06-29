using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

/// <summary>
/// 示例：单精度浮点四则运算操作符定义。
/// 实现 IFluxExprDefinition&lt;float&gt; 以同时获取解释器和 JIT 执行路径。
/// </summary>
public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, LParen, RParen, Return,
}

public readonly struct FloatMathDef : IFluxExprDefinition<float>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetReturnOp() => (byte)FloatOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpType GetKind(byte op) => (FloatOp)op switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op)
    {
        var o = (FloatOp)op;
        return o switch
        {
            FloatOp.Add => 1, FloatOp.Sub => 1,
            FloatOp.Mul => 2, FloatOp.Div => 2,
            FloatOp.Neg => 3,
            _ => 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op)
    {
        var o = (FloatOp)op;
        return o switch
        {
            FloatOp.LParen => new OpPair { PairRole = Pair.Left },
            FloatOp.RParen => new OpPair
            {
                PairRole   = Pair.Right,
                TargetLeft = (byte)FloatOp.LParen,
            },
            _ => new OpPair { PairRole = Pair.None },
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op)
    {
        return (FloatOp)op == FloatOp.Neg ? Associativity.Right : Associativity.Left;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return (FloatOp)op switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN : regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    float IFluxDefinition<float>.Compute(byte op, Instruction inst, IntPtr rp, int _)
    {
        unsafe
        {
            float* r = (float*)rp;
            return (FloatOp)op switch
            {
                FloatOp.Add => r[inst.Arg0] + r[inst.Arg1],
                FloatOp.Sub => r[inst.Arg0] - r[inst.Arg1],
                FloatOp.Mul => r[inst.Arg0] * r[inst.Arg1],
                FloatOp.Div => Math.Abs(r[inst.Arg1]) < float.Epsilon
                    ? float.NaN : r[inst.Arg0] / r[inst.Arg1],
                FloatOp.Neg => -r[inst.Arg0],
                _ => 0f,
            };
        }
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return (FloatOp)op switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero), nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ResolveToken(byte oper, TokenContext context)
    {
        if ((FloatOp)oper == FloatOp.Sub && context == TokenContext.OperandExpected)
            return (byte)FloatOp.Neg;
        return 0;
    }

    public string GetOperatorName(byte op) => ((FloatOp)op).ToString();
}
