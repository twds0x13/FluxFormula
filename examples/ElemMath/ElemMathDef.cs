using System;
using System.Linq.Expressions;
using FluxFormula.Core;
using SourceSerializer;

public enum Element : byte
{
    [Tag("Physical")] Physical = 0,
    [Tag("fire")]  Fire,
    [Tag("ice")]   Ice,
    [Tag("magic")] Magic,
}

[Template("<float Amount><optional>:<Element Element></optional>")]
public struct ElemValue : IEquatable<ElemValue>
{
    public float Amount;
    public Element Element;

    public ElemValue(float amount, Element element = Element.Physical)
        => (Amount, Element) = (amount, element);

    public static ElemValue Add(ElemValue a, ElemValue b)
        => new(a.Amount + b.Amount, a.Element);

    public static ElemValue Sub(ElemValue a, ElemValue b)
        => new(a.Amount - (a.Element == b.Element ? b.Amount : 0f), a.Element);

    public static ElemValue Mul(ElemValue a, ElemValue b)
        => new(a.Amount * b.Amount, b.Element);

    public static ElemValue Div(ElemValue a, ElemValue b)
        => new(Math.Abs(b.Amount) < float.Epsilon ? float.NaN : a.Amount / b.Amount, b.Element);

    public static ElemValue Neg(ElemValue a)
        => new(-a.Amount, a.Element);

    public readonly bool Equals(ElemValue other)
        => Amount == other.Amount && Element == other.Element;

    public override readonly string ToString()
        => Element == Element.Physical ? $"{Amount:F2}" : $"{Amount:F2}:{Element}";
}

public enum ElemOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}

public readonly struct ElemDef : IFluxExprDefinition<ElemValue>
{
    public byte GetReturnOp() => (byte)ElemOp.Return;

    public int GetArity(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Add => 2, ElemOp.Sub => 2, ElemOp.Mul => 2,
        ElemOp.Div => 2, ElemOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Const  => OpType.Immediate,
        ElemOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Add => 1, ElemOp.Sub => 1, ElemOp.Mul => 2,
        ElemOp.Div => 2, ElemOp.Neg => 3, _ => 0,
    };

    public OpPair GetPair(byte op) => ((ElemOp)op) switch
    {
        ElemOp.LParen => new OpPair { PairRole = Pair.Left },
        ElemOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)ElemOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;
    public OperandPosition GetFirstPosition(byte op) => OperandPosition.Left;

    public byte ResolveToken(byte op, TokenContext ctx)
        => op == (byte)ElemOp.Sub && ctx == TokenContext.OperandExpected
            ? (byte)ElemOp.Neg : op;

    public string GetOperatorName(byte op) => ((ElemOp)op).ToString();

    public ElemValue Compute(byte op, Instruction inst, Span<ElemValue> regs)
    {
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((ElemOp)op) switch
        {
            ElemOp.Add => ElemValue.Add(a, b),
            ElemOp.Sub => ElemValue.Sub(a, b),
            ElemOp.Mul => ElemValue.Mul(a, b),
            ElemOp.Div => ElemValue.Div(a, b),
            ElemOp.Neg => ElemValue.Neg(a),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var type = typeof(ElemValue);
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((ElemOp)op) switch
        {
            ElemOp.Add => Expression.Call(type.GetMethod(nameof(ElemValue.Add))!, a, b),
            ElemOp.Sub => Expression.Call(type.GetMethod(nameof(ElemValue.Sub))!, a, b),
            ElemOp.Mul => Expression.Call(type.GetMethod(nameof(ElemValue.Mul))!, a, b),
            ElemOp.Div => Expression.Call(type.GetMethod(nameof(ElemValue.Div))!, a, b),
            ElemOp.Neg => Expression.Call(type.GetMethod(nameof(ElemValue.Neg))!, a),
            _ => Expression.Constant(default(ElemValue)),
        };
    }
}
