# Example: Vector3 Operations

Using a custom `Vector3f` struct for 3D vector math, demonstrating function-style prefix syntax, infix syntax, and dual-syntax views.

## Scenario

3D vector operations: cross product as `a x b` (infix) or `cross(a, b)` (function), normalization as `norm(v)`, dot product as `dot(a, b)`. Kinematics: `P = P0 + V0 * t`.

## TData Struct

```csharp
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

    public static Vector3f Cross(Vector3f a, Vector3f b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static float Dot(Vector3f a, Vector3f b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3f Normalize(Vector3f v)
    {
        float mag = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return mag < 1e-12f ? default : new(v.X / mag, v.Y / mag, v.Z / mag);
    }

    public readonly bool Equals(Vector3f other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override readonly string ToString()
        => $"({X:F2}, {Y:F2}, {Z:F2})";
}
```

`sizeof(Vector3f) = 12` bytes, consuming 2 Instruction slots per Immediate.

## Operator Enum

```csharp
public enum Vector3Op : byte
{
    Const, Add, Sub, Scale,
    Cross, Norm, Dot,
    Comma, LParen, RParen, Return = 255,
}
```

## Definition

```csharp
public readonly struct Vector3Def : IFluxExprDefinition<Vector3f>
{
    public byte GetReturnOp() => (byte)Vector3Op.Return;

    public int GetArity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 2, Vector3Op.Sub   => 2,
        Vector3Op.Scale => 2, Vector3Op.Cross => 2,
        Vector3Op.Dot   => 2, Vector3Op.Norm  => 1,
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
        Vector3Op.Add   => 1, Vector3Op.Sub   => 1,
        Vector3Op.Scale => 2, Vector3Op.Cross => 2,
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
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => a + b,
            Vector3Op.Sub   => a - b,
            Vector3Op.Scale => a * b.X,
            Vector3Op.Cross => Vector3f.Cross(a, b),
            Vector3Op.Norm  => Vector3f.Normalize(a),
            Vector3Op.Dot   => new Vector3f(Vector3f.Dot(a, b), 0, 0),
            _               => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var t = typeof(Vector3f);
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => Expression.Add(a, b),
            Vector3Op.Sub   => Expression.Subtract(a, b),
            Vector3Op.Scale => Expression.Multiply(a, Expression.PropertyOrField(b, "X")),
            Vector3Op.Cross => Expression.Call(a, t.GetMethod(nameof(Vector3f.Cross))!, a, b),
            Vector3Op.Norm  => Expression.Call(a, t.GetMethod(nameof(Vector3f.Normalize))!, a),
            Vector3Op.Dot   => Expression.New(t.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) })!,
                Expression.Call(a, t.GetMethod(nameof(Vector3f.Dot))!, a, b),
                Expression.Constant(0f), Expression.Constant(0f)),
            _               => Expression.Constant(default(Vector3f)),
        };
    }
}
```

## Lexer Configuration

```csharp
var config = new LexerConfig<Vector3f>
{
    LiteralOper = (byte)Vector3Op.Const,
    LiteralScanner = LexerConfig<Vector3f>.CreateDefaultNumberScanner(
        s => new Vector3f(float.Parse(s, CultureInfo.InvariantCulture), 0, 0)),
    Operators =
    {
        new("+", (byte)Vector3Op.Add, slots: new sbyte[] { -1, +1 }),
        new("-", (byte)Vector3Op.Sub, slots: new sbyte[] { -1, +1 }),
        new("*", (byte)Vector3Op.Scale, slots: new sbyte[] { -1, +1 }),
        new("x", (byte)Vector3Op.Cross, slots: new sbyte[] { -1, +1 }),
        new("cross", (byte)Vector3Op.Cross,
            slots: new sbyte[] { +2, +4 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
        new("dot", (byte)Vector3Op.Dot,
            slots: new sbyte[] { +2, +4 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
        new("norm", (byte)Vector3Op.Norm,
            slots: new sbyte[] { +2 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ")") }),
        new(",", (byte)Vector3Op.Comma),
    },
    Brackets = { new("(", ")", (byte)Vector3Op.LParen, (byte)Vector3Op.RParen) },
    VariablePatterns = { new("[", "]") },
};
```

## Usage

```csharp
var def    = new Vector3Def();
var runner = new FluxAssembler<Vector3f, Vector3Def>(def);
var lexer  = new FluxLexer<Vector3f>(config);

// Scalar multiply: P = P0 + V0 * t
var f = runner.Compile(lexer.Lex("[P0] + [V0] * [t]"));
var r = runner.Instantiate(f)
    .Set("P0", new Vector3f(10f, 5f, 0f))
    .Set("V0", new Vector3f(5f, 2f, 0f))
    .Set("t",  new Vector3f(3f, 0f, 0f))
    .Run();
// → (25.00, 11.00, 0.00)

// Cross: infix syntax
var cross = runner.Instantiate(runner.Compile(lexer.Lex("[a] x [b]")))
    .Set("a", new Vector3f(1, 0, 0))
    .Set("b", new Vector3f(0, 1, 0))
    .Run();
// → (0.00, 0.00, 1.00)

// Normalize
var norm = runner.Instantiate(runner.Compile(lexer.Lex("norm([v])")))
    .Set("v", new Vector3f(3, 4, 0))
    .Run();
// → (0.60, 0.80, 0.00)

// Dot product
var dot = runner.Instantiate(runner.Compile(lexer.Lex("dot([a], [b])")))
    .Set("a", new Vector3f(1, 2, 3))
    .Set("b", new Vector3f(4, 5, 6))
    .Run();
// dot.X → 32
```

## Key Points

- Scalars are wrapped as `Vector3f(s, 0, 0)`; `Compute` extracts via `.X`
- Function-style operators use `Slots + Aux` to declare the full token pattern
- Infix operators use `Slots [-1, +1]`
- `Cross` supports dual syntax: infix `a x b` and function `cross(a, b)`
- `Dot` result is wrapped in the X component; callers read `result.X`
- `sizeof(Vector3f) = 12`, each Immediate consumes 2 Instruction slots
