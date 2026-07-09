using System;
using System.Linq.Expressions;
using FluxFormula.Core;

public struct Vector3f : IEquatable<Vector3f>
{
    public float X, Y, Z;

    public Vector3f(float x, float y, float z) => (X, Y, Z) = (x, y, z);

    public static Vector3f operator +(Vector3f a, Vector3f b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3f operator -(Vector3f a, Vector3f b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3f operator *(Vector3f a, float s)
        => new(a.X * s, a.Y * s, a.Z * s);

    public readonly bool Equals(Vector3f other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override readonly string ToString()
        => $"({X:F2}, {Y:F2}, {Z:F2})";
}

public enum Vector3Op : byte
{
    Const, Add, Sub, Scale,
    LParen, RParen, Return = 255,
}

public readonly struct Vector3Def : IFluxExprDefinition<Vector3f>
{
    public byte GetReturnOp() => (byte)Vector3Op.Return;

    public int GetArity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 2,
        Vector3Op.Sub   => 2,
        Vector3Op.Scale => 2,
        _ => 0,
    };

    public OpType GetKind(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Const  => OpType.Immediate,
        Vector3Op.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 1,
        Vector3Op.Sub   => 1,
        Vector3Op.Scale => 2,
        _               => 0,
    };

    public OpPair GetPair(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.LParen => new OpPair { PairRole = Pair.Left },
        Vector3Op.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)Vector3Op.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public OperandPosition GetFirstPosition(byte op) => OperandPosition.Left;

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public string GetOperatorName(byte op) => ((Vector3Op)op).ToString();

    public Vector3f Compute(byte op, Instruction inst, Span<Vector3f> regs)
    {
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => regs[inst.Arg0] + regs[inst.Arg1],
            Vector3Op.Sub   => regs[inst.Arg0] - regs[inst.Arg1],
            Vector3Op.Scale => regs[inst.Arg0] * regs[inst.Arg1].X,
            _               => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var arg0 = regs[inst.Arg0];
        var arg1 = regs[inst.Arg1];
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => Expression.Add(arg0, arg1),
            Vector3Op.Sub   => Expression.Subtract(arg0, arg1),
            Vector3Op.Scale => Expression.Multiply(arg0,
                Expression.PropertyOrField(arg1, "X")),
            _               => Expression.Constant(default(Vector3f)),
        };
    }
}
