# 示例：MathDef

可直接拷贝使用的浮点四则运算定义（v3.0.0）。

> **v3.0.0**：操作符枚举现在是定义体的 `private` 实现细节。定义实现 `IFluxJITDefinition<TData>`（1 个泛型参数）。所有操作符相关方法使用 `byte`。

## 操作符枚举

```csharp
enum MathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}
```

## 定义体

```csharp
readonly struct MathDef : IFluxJITDefinition<float>
{
    public byte GetReturnOp() => (byte)MathOp.Return;

    public int GetArity(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 2, MathOp.Sub => 2, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((MathOp)op) switch
    {
        MathOp.Const  => OpType.Immediate,
        MathOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 1, MathOp.Sub => 1,
        MathOp.Mul => 2, MathOp.Div => 2,
        MathOp.Neg => 3,
        _          => 0,
    };

    public OpPair GetPair(byte op) => ((MathOp)op) switch
    {
        MathOp.LParen => new OpPair { PairRole = Pair.Left },
        MathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)MathOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
    {
        MathOp.Neg => Associativity.Right,
        _          => Associativity.Left,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    // 解释器
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return ((MathOp)op) switch
        {
            MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            MathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            MathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            MathOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return ((MathOp)op) switch
        {
            MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero),
                nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
}
```

## 使用

```csharp
var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);

// 1 + 2 * 3 = 7
var tokens = new FluxToken<float>[]
{
    new() { Oper = (byte)MathOp.Const, Data = 1f },
    new() { Oper = (byte)MathOp.Add },
    new() { Oper = (byte)MathOp.Const, Data = 2f },
    new() { Oper = (byte)MathOp.Mul },
    new() { Oper = (byte)MathOp.Const, Data = 3f },
};
Assert.That(runner.Build(tokens).Run(), Is.EqualTo(7f).Within(1e-6f));

// (1 + 2) * 3 = 9
// -5
// 1 / 0 → NaN
```

### v3.0.0 关键变更

- `IFluxJITDefinition<float, FloatOp>` → `IFluxJITDefinition<float>`：单泛型参数
- 操作符枚举（`MathOp`）是 `private`，不再需要 `public`
- 所有方法签名用 `byte` 替代枚举类型
- `OpPair<FloatOp>` → `OpPair`（非泛型）
- `FluxToken<float, FloatOp>` → `FluxToken<float>`；`Oper` 为 `byte`
- `FluxAssembler<float, FloatOp, FloatMathDef>` → `FluxAssembler<float, MathDef>`
- 不再需要 `sizeof(TOper) != 1` 运行时检查：`byte` 永远是 1 字节

完整源码见 `tests/FluxFormula.Core.Tests/TestDefinition.cs`。
