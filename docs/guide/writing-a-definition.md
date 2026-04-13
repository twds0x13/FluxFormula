# 自定义运算符

实现 `IFluxJITDefinition<TData, TOper>` 定义运算规则。一次实现同时获得解释器和 JIT 两条执行路径。

## 接口总览

```csharp
public interface IFluxDefinition<TData, TOper>
{
    TOper GetReturnOp();                                      // 终止指令
    int GetArity(byte op);                                    // 操作数数量
    OpType GetKind(byte op);                                  // 指令分类
    int GetPrecedence(TOper op);                              // 优先级
    OpPair<TOper> GetPair(TOper op);                          // 括号配对
    Associativity GetAssociativity(TOper op);                 // 结合方向
    TOper ResolveToken(TOper op, TokenContext ctx);           // Token 消歧
    TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers); // 解释器计算
}

public interface IFluxJITDefinition<TData, TOper> : IFluxDefinition<TData, TOper>
{
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers); // JIT 表达式
}
```

## 方法详解

### GetReturnOp

返回哪个枚举值代表终止指令。编译器在字节码末尾自动插入。

```csharp
public FloatOp GetReturnOp() => FloatOp.Return;
```

### GetArity

操作数个数。Immediate 和 Return 类型的 op 返回 0。arity 上限为 6，受 `Instruction` 的 Arg0-Arg5 字段数量限制。

```csharp
public int GetArity(byte op) => ((FloatOp)op) switch
{
    FloatOp.Add => 2,   // 左 + 右
    FloatOp.Neg => 1,   // 取负只需一个操作数
    _ => 0,
};
```

### GetKind

将 opcode 分类为三种 `OpType`：

```csharp
public OpType GetKind(byte op) => ((FloatOp)op) switch
{
    FloatOp.Const  => OpType.Immediate,   // 携带数据值
    FloatOp.Return => OpType.Return,      // 终止执行
    _              => OpType.Instruction, // 普通运算
};
```

### GetPrecedence

运算符优先级。数值越大越优先结合。典型分配：加减 = 1，乘除 = 2，一元前缀 = 3，幂运算 = 4。

```csharp
public int GetPrecedence(FloatOp op) => op switch
{
    FloatOp.Add => 1,
    FloatOp.Mul => 2,
    FloatOp.Neg => 3,
    _           => 0,
};
```

### GetPair

定义括号行为。`OpPair` 将语法括号映射为语义指令。

```csharp
public struct OpPair<TOper>
{
    public Pair PairRole;      // None / Left / Right
    public TOper TargetLeft;   // Right 括号匹配的目标 Left opcode
    public bool EmitOnMatch;   // 匹配时是否发射指令
    public TOper EmitOpCode;   // 发射什么指令
}
```

**普通括号：**

```csharp
FloatOp.LParen => new OpPair<FloatOp> { PairRole = Pair.Left },
FloatOp.RParen => new OpPair<FloatOp>
{
    PairRole   = Pair.Right,
    TargetLeft = FloatOp.LParen,
    // EmitOnMatch 默认为 false，仅从栈中弹走 LParen，不发射运算指令
},
```

**函数调用模拟（sin）：**

`sin(x)` 中 `sin` 是 Left-pair，`(` 匹配时触发 `EmitOnMatch` 发射 `SinOp` 指令：

```csharp
SomeOp.Sin => new OpPair<SomeOp>
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,     // Sin 自身不发射
},
SomeOp.FuncLParen => new OpPair<SomeOp>
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,
},
SomeOp.FuncRParen => new OpPair<SomeOp>
{
    PairRole    = Pair.Right,
    TargetLeft  = SomeOp.FuncLParen,
    EmitOnMatch = true,       // 匹配时发射 Sin 指令
    EmitOpCode  = SomeOp.Sin,
},
```

### GetAssociativity

`Left` 或 `Right`。二元运算符选 `Left`（`2 - 1 - 1 = 0`），一元前缀选 `Right`。

```csharp
public Associativity GetAssociativity(FloatOp op) => op switch
{
    FloatOp.Neg => Associativity.Right,
    _           => Associativity.Left,
};
```

### ResolveToken

Lexer 扫描符号时无法判断当前处于期望操作数还是期望运算符的上下文。`ResolveToken` 在 Token 生成后根据 `TokenContext` 做二次消歧，将同一符号映射为不同操作符。若不需要消歧，直接返回 `op`。

```csharp
public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
{
    // '-' 在期望操作数时是一元取负，否则是二元减法
    if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
        return FloatOp.Neg;
    return op;
}
```

| TokenContext | 触发条件 |
|---|---|
| `OperandExpected` | 表达式起点、左括号后、二元运算符后 |
| `OperatorExpected` | 操作数后、右括号后 |

### Compute（解释器路径）

```csharp
public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
{
    return ((FloatOp)op) switch
    {
        FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
        FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],

        // 除零 → 写入 NaN 到目标寄存器，触发 R0 短路
        FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
            ? float.NaN
            : regs[inst.Arg0] / regs[inst.Arg1],

        FloatOp.Neg => -regs[inst.Arg0],
        _ => 0f,
    };
}
```

### GetExpression（JIT 路径）

与 `Compute` 语义一致，用 LINQ Expression 表达：

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
{
    return ((FloatOp)op) switch
    {
        FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
        FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
        FloatOp.Div => Expression.Condition(
            Expression.Equal(regs[inst.Arg1], Expression.Constant(0f)),
            Expression.Constant(float.NaN),
            Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])
        ),
        _ => Expression.Constant(0f),
    };
}
```

## 错误处理：R0 短路

`Compute()` 或 `GetExpression()` 若返回非 default 值，该值被写入 R0（错误寄存器）。执行器在每条指令后检查 R0，一旦非 default 立即终止并返回错误值。

```csharp
// 除零错误通过 NaN 传播
C(1f), Op(Div), C(0f), Op(Add), C(5f)
// → 1/0 = NaN → 短路 → 整条公式返回 NaN，Add 不会执行
```

## 性能建议

- 所有方法标注 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- `Compute` 中的 switch 使用枚举值转换而非 `Enum.Equals()`，避免装箱
- `GetPair` 热路径上对非括号 op 直接返回 `Pair.None` 的 default 实例
