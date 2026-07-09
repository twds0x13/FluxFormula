using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3f Cross(Vector3f a, Vector3f b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3f a, Vector3f b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3f Normalize(Vector3f v)
    {
        float mag = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (mag < 1e-12f) return default;
        return new(v.X / mag, v.Y / mag, v.Z / mag);
    }

    public readonly bool Equals(Vector3f other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public static bool operator ==(Vector3f a, Vector3f b) => a.Equals(b);
    public static bool operator !=(Vector3f a, Vector3f b) => !a.Equals(b);

    public override readonly bool Equals(object obj)
        => obj is Vector3f other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public override readonly string ToString()
        => $"({X:F2}, {Y:F2}, {Z:F2})";
}

public enum Vector3Op : byte
{
    Const, Add, Sub, Scale,
    Cross, Norm, Dot,
    Comma, LParen, RParen, Return = 255,
}

public readonly struct Vector3Def : IFluxExprDefinition<Vector3f>
{
    public byte GetReturnOp() => (byte)Vector3Op.Return;

    public int GetArity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 2,
        Vector3Op.Sub   => 2,
        Vector3Op.Scale => 2,
        Vector3Op.Cross => 2,
        Vector3Op.Dot   => 2,
        Vector3Op.Norm  => 1,
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
        Vector3Op.Cross => 2,
        Vector3Op.Dot   => 2,
        Vector3Op.Norm  => 3,
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
        Vector3Op.Comma => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft  = (byte)Vector3Op.LParen,
            IsSeparator = true,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Norm => Associativity.Right,
        _              => Associativity.Left,
    };

    public OperandPosition GetFirstPosition(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => OperandPosition.Left,
        Vector3Op.Sub   => OperandPosition.Left,
        Vector3Op.Scale => OperandPosition.Left,
        Vector3Op.Cross => OperandPosition.Left,
        _               => OperandPosition.Right,
    };

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public string GetOperatorName(byte op) => ((Vector3Op)op).ToString();

    public Vector3f Compute(byte op, Instruction inst, Span<Vector3f> regs)
    {
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => regs[inst.Arg0] + regs[inst.Arg1],
            Vector3Op.Sub   => regs[inst.Arg0] - regs[inst.Arg1],
            Vector3Op.Scale => regs[inst.Arg0] * regs[inst.Arg1].X,
            Vector3Op.Cross => Vector3f.Cross(regs[inst.Arg0], regs[inst.Arg1]),
            Vector3Op.Norm  => Vector3f.Normalize(regs[inst.Arg0]),
            Vector3Op.Dot   => new Vector3f(
                Vector3f.Dot(regs[inst.Arg0], regs[inst.Arg1]), 0, 0),
            _               => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var arg0 = regs[inst.Arg0];
        var arg1 = regs[inst.Arg1];
        var type = typeof(Vector3f);
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => Expression.Add(arg0, arg1),
            Vector3Op.Sub   => Expression.Subtract(arg0, arg1),
            Vector3Op.Scale => Expression.Multiply(arg0,
                Expression.PropertyOrField(arg1, "X")),
            Vector3Op.Cross => Expression.Call(
                Expression.Constant(default(Vector3f)), type.GetMethod(nameof(Vector3f.Cross))!, arg0, arg1),
            Vector3Op.Norm  => Expression.Call(
                Expression.Constant(default(Vector3f)), type.GetMethod(nameof(Vector3f.Normalize))!, arg0),
            Vector3Op.Dot   => Expression.New(typeof(Vector3f).GetConstructor(new[] { typeof(float), typeof(float), typeof(float) })!,
                Expression.Call(
                    Expression.Constant(default(Vector3f)), type.GetMethod(nameof(Vector3f.Dot))!, arg0, arg1),
                Expression.Constant(0f), Expression.Constant(0f)),
            _               => Expression.Constant(default(Vector3f)),
        };
    }
}
