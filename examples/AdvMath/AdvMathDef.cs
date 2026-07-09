using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

enum AdvMathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    Select, Lerp, Max, Min, Step,
    Question, Colon,
    LParen, RParen, Comma, Return = 255,
}

readonly struct AdvMathDef : IFluxExprDefinition<float>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetReturnOp() => (byte)AdvMathOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Add => 2, AdvMathOp.Sub => 2, AdvMathOp.Mul => 2,
        AdvMathOp.Div => 2, AdvMathOp.Neg => 1,
        AdvMathOp.Select => 3, AdvMathOp.Lerp => 3,
        AdvMathOp.Max => 2, AdvMathOp.Min => 2, AdvMathOp.Step => 2,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpType GetKind(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Const  => OpType.Immediate,
        AdvMathOp.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Add => 1, AdvMathOp.Sub => 1,
        AdvMathOp.Mul => 2, AdvMathOp.Div => 2,
        AdvMathOp.Neg => 3,
        AdvMathOp.Select => 4, AdvMathOp.Lerp => 4,
        AdvMathOp.Max => 4, AdvMathOp.Min => 4, AdvMathOp.Step => 4,
        AdvMathOp.Question => -100,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.LParen => new OpPair { PairRole = Pair.Left },
        AdvMathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)AdvMathOp.LParen,
        },
        AdvMathOp.Comma => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft  = (byte)AdvMathOp.LParen,
            IsSeparator = true,
        },
        AdvMathOp.Question => new OpPair
        {
            PairRole    = Pair.None,
            EmitOnMatch = true,
            EmitOpCode  = (byte)AdvMathOp.Select,
        },
        AdvMathOp.Colon => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft  = (byte)AdvMathOp.Question,
            IsSeparator = true,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Neg => Associativity.Right,
        _             => Associativity.Left,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OperandPosition GetFirstPosition(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Add => OperandPosition.Left,
        AdvMathOp.Sub => OperandPosition.Left,
        AdvMathOp.Mul => OperandPosition.Left,
        AdvMathOp.Div => OperandPosition.Left,
        AdvMathOp.Question => OperandPosition.Left,
        _                  => OperandPosition.Right,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)AdvMathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)AdvMathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((AdvMathOp)op).ToString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return (AdvMathOp)op switch
        {
            AdvMathOp.Add  => regs[inst.Arg0] + regs[inst.Arg1],
            AdvMathOp.Sub  => regs[inst.Arg0] - regs[inst.Arg1],
            AdvMathOp.Mul  => regs[inst.Arg0] * regs[inst.Arg1],
            AdvMathOp.Div  => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN : regs[inst.Arg0] / regs[inst.Arg1],
            AdvMathOp.Neg  => -regs[inst.Arg0],
            AdvMathOp.Select => regs[inst.Arg0] != 0f ? regs[inst.Arg1] : regs[inst.Arg2],
            AdvMathOp.Lerp   => regs[inst.Arg0] + (regs[inst.Arg1] - regs[inst.Arg0]) * regs[inst.Arg2],
            AdvMathOp.Max    => regs[inst.Arg0] >= regs[inst.Arg1] ? regs[inst.Arg0] : regs[inst.Arg1],
            AdvMathOp.Min    => regs[inst.Arg0] <= regs[inst.Arg1] ? regs[inst.Arg0] : regs[inst.Arg1],
            AdvMathOp.Step   => regs[inst.Arg1] >= regs[inst.Arg0] ? 1f : 0f,
            _ => 0f,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero  = Expression.Constant(0f);
        var one   = Expression.Constant(1f);
        var nan   = Expression.Constant(float.NaN);
        return (AdvMathOp)op switch
        {
            AdvMathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero), nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            AdvMathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            AdvMathOp.Select => Expression.Condition(
                Expression.NotEqual(regs[inst.Arg0], zero),
                regs[inst.Arg1], regs[inst.Arg2]),
            AdvMathOp.Lerp => Expression.Add(
                regs[inst.Arg0],
                Expression.Multiply(
                    Expression.Subtract(regs[inst.Arg1], regs[inst.Arg0]),
                    regs[inst.Arg2])),
            AdvMathOp.Max => Expression.Condition(
                Expression.GreaterThanOrEqual(regs[inst.Arg0], regs[inst.Arg1]),
                regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Min => Expression.Condition(
                Expression.LessThanOrEqual(regs[inst.Arg0], regs[inst.Arg1]),
                regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Step => Expression.Condition(
                Expression.GreaterThanOrEqual(regs[inst.Arg1], regs[inst.Arg0]),
                one, zero),
            _ => Expression.Constant(0f),
        };
    }
}
