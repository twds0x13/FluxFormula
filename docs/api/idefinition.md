# IFluxDefinition / IFluxJITDefinition

运算符语义的核心接口。v3.0.0 移除了 `TOper` 泛型参数：所有操作符相关方法现在接收和返回 `byte`，操作符枚举变为定义体的内部实现细节。

## IFluxDefinition

```csharp
public interface IFluxDefinition<TData>
    where TData : unmanaged
```

### 方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `GetReturnOp()` | `byte` | 返回哪个操作码代表终止指令 |
| `GetArity(byte op)` | `int` | 操作数个数。Immediate/Return 返回 0 |
| `GetKind(byte op)` | `OpType` | Immediate / Instruction / Return |
| `GetPrecedence(byte op)` | `int` | 优先级。数值越大越优先结合 |
| `GetPair(byte op)` | `OpPair` | 括号配对信息 |
| `GetAssociativity(byte op)` | `Associativity` | Left / Right |
| `ResolveToken(byte oper, TokenContext ctx)` | `byte` | Token 消歧：根据上下文将同一符号映射为不同语义。返回 0 表示不消歧 |
| `Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers)` | `TData` | 解释器路径：执行运算 |
| `GetOperatorName(byte op)` | `string` | 操作码的显示名称（DIM，默认返回 null）。编辑器/工具链查询点 |

### ResolveToken

Lexer 扫描符号时无法判断上下文（期望操作数还是期望运算符）。`ResolveToken` 在 Token 生成后根据 `TokenContext` 做二次消歧。

```csharp
// 定义体内部的枚举
enum MathOp : byte { Const = 0, Add, Sub, Mul, Div, Neg }

// '-' 在期望操作数时是一元取负，否则是二元减法
public byte ResolveToken(byte oper, TokenContext ctx)
{
    if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
        return (byte)MathOp.Neg;
    return oper;
}
```

| TokenContext | 含义 |
|---|---|
| `OperandExpected` | 当前位置期望操作数（表达式起点、左括号后、运算符后） |
| `OperatorExpected` | 当前位置期望运算符（操作数后、右括号后） |

### Compute 的错误处理

返回 `default` 以外的值时，执行器将其写入 R0 并短路返回。

## IFluxJITDefinition

```csharp
public interface IFluxJITDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
```

### 额外方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `GetExpression(byte op, Instruction inst, ParameterExpression[] regs)` | `Expression` | JIT 路径：返回 LINQ 表达式树 |

`regs` 是 256 个 `ParameterExpression`，索引对应寄存器号。返回值是纯计算表达式，赋值和错误检查由 JIT 编译器自动包装。

## 实现要求

- 实现类应为 `readonly struct`，若作为 `TDef` 泛型参数则须满足 `unmanaged`
- 所有方法标注 `[MethodImpl(AggressiveInlining)]`
- `GetExpression` 与 `Compute` 必须语义一致：同一输入产生同一输出

## 完整实现示例

```csharp
enum MathOp : byte { Const = 0, Add, Sub, Mul, Div, Neg, Return = 255 }

readonly struct MathDef : IFluxJITDefinition<float>
{
    public byte GetReturnOp() => (byte)MathOp.Return;
    public int GetArity(byte op) => ((MathOp)op) switch
    {
        MathOp.Const => 0, MathOp.Return => 0,
        MathOp.Neg => 1, _ => 2
    };
    public OpType GetKind(byte op) => op == (byte)MathOp.Const ? OpType.Immediate
        : op == (byte)MathOp.Return ? OpType.Return : OpType.Instruction;
    public int GetPrecedence(byte op) => ((MathOp)op) switch
    {
        MathOp.Add or MathOp.Sub => 1, MathOp.Mul or MathOp.Div => 2, _ => 0
    };
    public Associativity GetAssociativity(byte op) => Associativity.Left;
    public OpPair GetPair(byte op) => default;
    public byte ResolveToken(byte oper, TokenContext ctx) => oper;
    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
        => ((MathOp)op) switch
        {
            MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            MathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            MathOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            MathOp.Neg => -regs[inst.Arg0],
            _ => default
        };

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
        => ((MathOp)op) switch
        {
            MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Default(typeof(float))
        };
}
```

## v3.0.0 变更

- `IFluxDefinition<TData, TOper>` → `IFluxDefinition<TData>`（两参数→一参数）
- `IFluxJITDefinition<TData, TOper>` → `IFluxJITDefinition<TData>`
- 所有 `TOper` 参数和返回值 → `byte`
- 操作符枚举现在是定义体的 `internal`/`private` 细节，不再需要 `public enum`
- 新增 `GetOperatorName(byte op)` DIM，供编辑器/工具链查询操作码名称
- `OpPair` 不再泛型化：`OpPair` 替代 `OpPair<TOper>`
