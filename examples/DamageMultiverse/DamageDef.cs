using System;
using System.Linq.Expressions;
using FluxFormula.Core;

enum DamageOp : byte
{
    Const, Add, Sub, Mul, Div,
    Select,           // ternary: condition ? trueVal : falseVal
    Question, Colon,  // ? : sugar → emits Select
    Comma, LParen, RParen,
    Return = 255,
}

readonly struct DamageDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)DamageOp.Return;

    public int GetArity(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Add    => 2,
        DamageOp.Sub    => 2,
        DamageOp.Mul    => 2,
        DamageOp.Div    => 2,
        DamageOp.Select => 3,
        _               => 0,
    };

    public OpType GetKind(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Const  => OpType.Immediate,
        DamageOp.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Add => 1, DamageOp.Sub => 1,
        DamageOp.Mul => 2, DamageOp.Div => 2,
        DamageOp.Select   => 4,
        DamageOp.Question => -100,
        _                 => 0,
    };

    public OpPair GetPair(byte op) => ((DamageOp)op) switch
    {
        DamageOp.LParen => new OpPair { PairRole = Pair.Left },
        DamageOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)DamageOp.LParen,
        },
        DamageOp.Question => new OpPair
        {
            PairRole    = Pair.None,
            EmitOnMatch  = true,
            EmitOpCode   = (byte)DamageOp.Select,
        },
        DamageOp.Colon => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)DamageOp.Question,
            IsSeparator = true,
        },
        DamageOp.Comma => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)DamageOp.LParen,
            IsSeparator = true,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public OperandPosition GetFirstPosition(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Add => OperandPosition.Left,
        DamageOp.Sub => OperandPosition.Left,
        DamageOp.Mul => OperandPosition.Left,
        DamageOp.Div => OperandPosition.Left,
        _             => OperandPosition.Right,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)DamageOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)DamageOp.Mul; // negation handled by Multiply by -1 for simplicity
        return oper;
    }

    public string GetOperatorName(byte op) => ((DamageOp)op).ToString();

    // ── Compute (interpreter path) ──
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((DamageOp)op) switch
        {
            DamageOp.Add    => regs[inst.Arg0] + regs[inst.Arg1],
            DamageOp.Sub    => regs[inst.Arg0] - regs[inst.Arg1],
            DamageOp.Mul    => regs[inst.Arg0] * regs[inst.Arg1],
            DamageOp.Div    => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN : regs[inst.Arg0] / regs[inst.Arg1],
            DamageOp.Select => regs[inst.Arg0] != 0f ? regs[inst.Arg1] : regs[inst.Arg2],
            _ => 0f,
        };
    }

    // ── JIT expression tree path ──
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        return ((DamageOp)op) switch
        {
            DamageOp.Add    => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Sub    => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Mul    => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Div    => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Select => Expression.Condition(
                Expression.NotEqual(regs[inst.Arg0], zero),
                regs[inst.Arg1], regs[inst.Arg2]),
            _ => Expression.Constant(0f),
        };
    }
}
