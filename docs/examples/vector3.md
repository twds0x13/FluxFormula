# 示例：Vector3 运算

演示使用自定义 `TData` 结构体进行三维向量运算。

## 场景

3D 空间中的位置计算：`P = P0 + V0 * t + 0.5 * G * t^2`（初位置 + 速度 × 时间 + 0.5 × 重力加速度 × 时间的平方）。

## TData 结构体

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

`Vector3f` 满足 `unmanaged` 约束（仅含三个 blittable float 字段，无引用类型）。`sizeof(Vector3f) = 12` 字节，在字节码的 Immediate 指令中占用 2 个 Instruction 槽。

## 操作符枚举

```csharp
public enum Vector3Op : byte
{
    Const,       // 立即数
    Add, Sub,    // 向量加减
    Scale,       // 向量 × 标量（双目：左侧向量，右侧标量）
    LParen, RParen,
    Return,
}
```

本例中公式为 `P0 + V0 * t`，其中 `*` 是标量乘法。设计思路：`V0 * t` 编译为 `V0` 和标量 `t` 两个 Const 立即数，后跟 `Scale` 操作符。`Scale` 的 arity 为 2：它取两个操作数，左侧向量（`V0`）和右侧标量（`t`）。标量以 Const 立即数形式嵌入字节码，运行时可通过 `SetIndex` 覆盖对应 immediate 槽位来修改标量值。

## 定义体

```csharp
public readonly struct Vector3Def : IFluxExprDefinition<Vector3f>
{
    public byte GetReturnOp() => (byte)Vector3Op.Return;

    public int GetArity(byte op) => ((Vector3Op)op) switch
    {
        Vector3Op.Add   => 2,
        Vector3Op.Sub   => 2,
        Vector3Op.Scale => 2,  // 双操作数：向量 + 标量
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
        Vector3Op.Scale => 2,  // 标量乘法优先级高于加减
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

    public Vector3f Compute(byte op, Instruction inst, Span<Vector3f> regs)
    {
        return ((Vector3Op)op) switch
        {
            Vector3Op.Add   => regs[inst.Arg0] + regs[inst.Arg1],
            Vector3Op.Sub   => regs[inst.Arg0] - regs[inst.Arg1],
            Vector3Op.Scale => regs[inst.Arg0] * regs[inst.Arg1].X,  // 标量借 X 分量传递
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
```

## 使用

```csharp
var def    = new Vector3Def();
var runner = new FluxAssembler<Vector3f, Vector3Def>(def);

// 公式: P0 + V0 * t
// Token: Const(P0), Const(V0), Const(t), Scale, Add
var formula = runner.Compile(new FluxToken<Vector3f>[]
{
    new() { Oper = (byte)Vector3Op.Const, Data = new Vector3f(0f, 0f, 0f) },    // P0 （slot 0）
    new() { Oper = (byte)Vector3Op.Const, Data = new Vector3f(5f, 2f, 0f) },    // V0  （slot 1）
    new() { Oper = (byte)Vector3Op.Const, Data = new Vector3f(3f, 0f, 0f) },    // t   （slot 2，标量借 X 分量）
    new() { Oper = (byte)Vector3Op.Scale },  // V0 * t
    new() { Oper = (byte)Vector3Op.Add },     // P0 + (V0 * t)
});

// 运行时覆盖初位置和速度，标量 t 使用编译时嵌入的立即数（需要修改时可 SetIndex(2, ...) 覆盖）
Vector3f result = runner.Instantiate(formula)
    .SetIndex(0, new Vector3f(10f, 5f, 0f))       // P0
    .SetIndex(1, new Vector3f(5f, 2f, 0f))         // V0
    .Run();
// → (25.00, 11.00, 0.00) = P0 + V0 * t
```

## 要点

- `TData` 必须满足 `unmanaged` 约束。含引用类型的 struct 不可用
- `sizeof(TData)` 越大，每个 Immediate 消耗的 Instruction 槽位越多。`Vector3f`（12 字节）= 2 槽
- 标量以 Const 立即数形式嵌入字节码，通过 X 分量承载 float 值。`Compute` 中通过 `regs[inst.Arg1].X` 读取
- 更复杂的向量运算（点积、叉积）可通过增加更多操作符枚举值实现
