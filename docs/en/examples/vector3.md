# Example: Vector3 Operations

Using a custom `TData` struct for 3D vector arithmetic.

## Scenario

Position computation in 3D space: `P = P0 + V0 * t` (initial position + velocity × time).

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

    public readonly bool Equals(Vector3f other)
        => X == other.X && Y == other.Y && Z == other.Z;

    public override readonly string ToString()
        => $"({X:F2}, {Y:F2}, {Z:F2})";
}
```

`Vector3f` satisfies the `unmanaged` constraint (three blittable float fields, no reference types). `sizeof(Vector3f) = 12` bytes, consuming 2 Instruction slots per Immediate in the bytecode.

## Operator Enum

```csharp
public enum Vector3Op : byte
{
    Const,       // Immediate
    Add, Sub,    // Vector arithmetic
    Scale,       // Vector × scalar (unary: reads scalar from R1)
    LParen, RParen,
    Return,
}
```

The formula `P0 + V0 * t` treats `*` as scalar multiplication. `V0 * t` compiles to `V0` followed by `Scale`. `Scale` has arity 1 — it takes a vector operand and reads the scalar from R1 (injected via `SetIndex`).

## Definition

```csharp
public readonly struct Vector3Def : IFluxJITDefinition<Vector3f, Vector3Op>
{
    public Vector3Op GetReturnOp() => Vector3Op.Return;

    public int GetArity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 2,
        Vector3Op.Sub   => 2,
        Vector3Op.Scale => 1,  // Single operand: the vector
        _ => 0,
    };

    public OpType GetKind(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Const  => OpType.Immediate,
        Vector3Op.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    public int GetPrecedence(Vector3Op op) => op switch
    {
        Vector3Op.Add   => 1,
        Vector3Op.Sub   => 1,
        Vector3Op.Scale => 2,  // Scalar multiplication binds tighter than +/- 
        _               => 0,
    };

    public OpPair<Vector3Op> GetPair(Vector3Op op) => op switch
    {
        Vector3Op.LParen => new OpPair<Vector3Op> { PairRole = Pair.Left },
        Vector3Op.RParen => new OpPair<Vector3Op>
        {
            PairRole   = Pair.Right,
            TargetLeft = Vector3Op.LParen,
        },
        _ => new OpPair<Vector3Op> { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(Vector3Op op) => Associativity.Left;

    public Vector3Op ResolveToken(Vector3Op op, TokenContext ctx) => op;

    public Vector3f Compute(byte op, Instruction inst, ReadOnlySpan<Vector3f> regs)
    {
        // Scalar comes from R1's X component
        float scalar = regs[1].X;
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => regs[inst.Arg0] + regs[inst.Arg1],
            Vector3Op.Sub   => regs[inst.Arg0] - regs[inst.Arg1],
            Vector3Op.Scale => regs[inst.Arg0] * scalar,
            _               => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var scalar = Expression.PropertyOrField(regs[1], "X");
        var arg0   = regs[inst.Arg0];
        var arg1   = regs[inst.Arg1];
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => Expression.Add(arg0, arg1),
            Vector3Op.Sub   => Expression.Subtract(arg0, arg1),
            Vector3Op.Scale => Expression.Multiply(arg0, scalar),
            _               => Expression.Constant(default(Vector3f)),
        };
    }
}
```

## Usage

```csharp
var def    = new Vector3Def();
var runner = new FluxAssembler<Vector3f, Vector3Op, Vector3Def>(def);

// Formula: P0 + V0 * t
// Tokens: Const(P0), Const(V0), Scale, Add
// P0 at slot 0, V0 at slot 1, scalar t injected at slot 2
var formula = runner.Compile(new FluxToken<Vector3f, Vector3Op>[]
{
    new() { Oper = Vector3Op.Const, Data = new Vector3f(0f, 0f, 0f) },    // P0
    new() { Oper = Vector3Op.Const, Data = new Vector3f(5f, 2f, 0f) },    // V0
    new() { Oper = Vector3Op.Scale },  // V0 * t
    new() { Oper = Vector3Op.Add },     // P0 + (V0 * t)
});

// t = 3.0
Vector3f result = runner.Instantiate(formula)
    .SetIndex(0, new Vector3f(10f, 5f, 0f))       // P0
    .SetIndex(1, new Vector3f(5f, 2f, 0f))         // V0
    .SetIndex(2, new Vector3f(3f, 0f, 0f))         // t (passed via X component)
    .Run();
// → (25.00, 11.00, 0.00) = P0 + V0 * t
```

## Key Points

- `TData` must satisfy the `unmanaged` constraint. Structs with reference-type fields are invalid
- Larger `sizeof(TData)` means more Instruction slots per Immediate. `Vector3f` (12 bytes) = 2 slots
- Scalars can be tunneled through the X component when working with vector TData
- More complex vector operations (dot product, cross product) can be added via additional operator enum values
