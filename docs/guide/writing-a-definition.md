# 自定义运算符

如果你想定义的不只是加减乘除，而是伤害公式里的元素克制、法术系统里的抽牌消耗——这一页告诉你怎么实现自己的 `IFluxExprDefinition<TData>`。一次实现同时获得解释器和 JIT 两条执行路径。接口为单泛型参数：操作符枚举是定义体的 `private` 内部细节。

## 接口总览

```csharp
public interface IFluxDefinition<TData>
    where TData : unmanaged
{
    byte GetReturnOp();                                               // 终止指令
    int GetArity(byte op);                                            // 操作数数量
    OpType GetKind(byte op);                                          // 指令分类
    int GetPrecedence(byte op);                                       // 优先级
    OpPair GetPair(byte op);                                          // 括号配对
    Associativity GetAssociativity(byte op);                          // 结合方向
    OperandPosition GetFirstPosition(byte op);                        // 首个操作数位置（DIM: Left）
    byte ResolveToken(byte oper, TokenContext ctx);                   // Token 消歧
    TData Compute(byte op, Instruction inst, Span<TData> registers); // 解释器计算
    string GetOperatorName(byte op);                                  // 显示名称（DIM，可选）
}

public interface IFluxExprDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
{
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers); // JIT 表达式
}
```

## 方法详解

### GetReturnOp

返回哪个 byte 值代表终止指令。编译器在字节码末尾自动插入。

```csharp
public byte GetReturnOp() => (byte)MathOp.Return;
```

### GetArity

操作数个数。Immediate 和 Return 类型的 op 返回 0。arity 上限为 6，受 `Instruction` 的 Arg0-Arg5 字段数量限制。

```csharp
public int GetArity(byte op) => ((MathOp)op) switch
{
    MathOp.Add => 2,   // 左 + 右
    MathOp.Neg => 1,   // 取负只需一个操作数
    _ => 0,
};
```

### GetKind

将 opcode 分类为三种 `OpType`：

```csharp
public OpType GetKind(byte op) => ((MathOp)op) switch
{
    MathOp.Const  => OpType.Immediate,   // 携带数据值
    MathOp.Return => OpType.Return,      // 终止执行
    _             => OpType.Instruction, // 普通运算
};
```

### GetPrecedence

运算符优先级。数值越大越优先结合。典型分配：加减 = 1，乘除 = 2，一元前缀 = 3，幂运算 = 4。

```csharp
public int GetPrecedence(byte op) => ((MathOp)op) switch
{
    MathOp.Add => 1,
    MathOp.Mul => 2,
    MathOp.Neg => 3,
    _          => 0,
};
```

### GetPair

定义括号行为。`OpPair` 是非泛型结构体，`TargetLeft` 和 `EmitOpCode` 均为 `byte`：

```csharp
public struct OpPair
{
    public Pair PairRole;      // None / Left / Right
    public byte TargetLeft;    // Right 括号匹配的目标 Left opcode
    public bool EmitOnMatch;   // 匹配时是否发射指令
    public byte EmitOpCode;    // 发射什么指令
    public bool IsSeparator;   // 参数分隔符（如逗号）
}
```

**普通括号：**

```csharp
public OpPair GetPair(byte op) => ((MathOp)op) switch
{
    MathOp.LParen => new OpPair { PairRole = Pair.Left },
    MathOp.RParen => new OpPair
    {
        PairRole   = Pair.Right,
        TargetLeft = (byte)MathOp.LParen,
        // EmitOnMatch 默认为 false，仅从栈中弹走 LParen，不发射运算指令
    },
    _ => new OpPair { PairRole = Pair.None },
};
```

**函数调用模拟（sin）：**

`sin(x)` 中 `sin` 是 Left-pair，`(` 匹配时触发 `EmitOnMatch` 发射 `SinOp` 指令：

```csharp
(byte)MyOp.Sin => new OpPair
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,     // Sin 自身不发射
},
(byte)MyOp.FuncLParen => new OpPair
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,
},
(byte)MyOp.FuncRParen => new OpPair
{
    PairRole    = Pair.Right,
    TargetLeft  = (byte)MyOp.FuncLParen,
    EmitOnMatch = true,       // 匹配时发射 Sin 指令
    EmitOpCode  = (byte)MyOp.Sin,
},
```

### GetAssociativity

`Left` 或 `Right`。二元运算符选 `Left`（`2 - 1 - 1 = 0`），一元前缀选 `Right`。

```csharp
public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
{
    MathOp.Neg => Associativity.Right,
    _          => Associativity.Left,
};
```

### GetFirstPosition

首个操作数相对于操作符的位置。`Left`（中缀 `a + b`）：表达式以本操作符开头时视为缺左操作数（`FluxModifier`）。`Right`（前缀 `-x`、`max(a,b)`）：表达式以本操作符开头为合法 `FluxFormula`。

默认实现返回 `Left`。定义体通过 switch 显式声明哪些操作符是中缀。

```csharp
public OperandPosition GetFirstPosition(byte op) => ((MathOp)op) switch
{
    MathOp.Add => OperandPosition.Left,
    MathOp.Sub => OperandPosition.Left,
    MathOp.Mul => OperandPosition.Left,
    MathOp.Div => OperandPosition.Left,
    _          => OperandPosition.Right,
};
```

### ResolveToken

Lexer 扫描符号时无法判断当前处于期望操作数还是期望运算符的上下文。`ResolveToken` 在 Token 生成后根据 `TokenContext` 做二次消歧，将同一符号映射为不同操作码。若不需要消歧，直接返回 `oper`。返回 `0` 表示不消歧。

```csharp
public byte ResolveToken(byte oper, TokenContext ctx)
{
    // '-' 在期望操作数时是一元取负，否则是二元减法
    if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
        return (byte)MathOp.Neg;
    return oper;
}
```

| TokenContext | 触发条件 |
|---|---|
| `OperandExpected` | 表达式起点、左括号后、二元运算符后 |
| `OperatorExpected` | 操作数后、右括号后 |

### Compute（解释器路径）

```csharp
public float Compute(byte op, Instruction inst, Span<float> regs)
{
    return ((MathOp)op) switch
    {
        MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
        MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],

        // 除零 → 写入 NaN 到目标寄存器，触发 R0 短路
        MathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
            ? float.NaN
            : regs[inst.Arg0] / regs[inst.Arg1],

        MathOp.Neg => -regs[inst.Arg0],
        _ => 0f,
    };
}
```

### GetExpression（JIT 路径）

与 `Compute` 语义一致，用 LINQ Expression 表达：

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
{
    return ((MathOp)op) switch
    {
        MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
        MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
        MathOp.Div => Expression.Condition(
            Expression.Equal(regs[inst.Arg1], Expression.Constant(0f)),
            Expression.Constant(float.NaN),
            Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])
        ),
        MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
        _ => Expression.Constant(0f),
    };
}
```

### GetOperatorName（可选）

操作码的显示名称，供编辑器和工具链使用。DIM 默认返回 `null`；覆写以提供有意义的名称。

```csharp
public string GetOperatorName(byte op) => ((MathOp)op).ToString();
```

## 错误处理：R0 短路

`Compute` 接收 `Span<TData>`，可直接写入 `registers[Registers.Error]` 触发短路。执行器在每条指令后检查 R0：一旦非 default，立即终止求值并返回 R0 的值。后续指令不会执行。

```csharp
public float Compute(byte op, Instruction inst, Span<float> regs)
{
    if (/* 错误条件 */)
    {
        regs[Registers.Error] = float.NaN;  // 写入非 default → 立即短路
        return default;
    }
    // ... 正常计算
}
```

JIT 路径同样支持：`GetExpression` 编写的 R0 写入逻辑在编译后的委托中同样生效。

## 性能建议

- 所有方法标注 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- `Compute` 中的 switch 使用 `(MathOp)op` 强转，零装箱开销
- `GetPair` 热路径上对非括号 op 直接返回 `Pair.None` 的 default 实例
