# IFluxDefinition / IFluxJITDefinition

运算符语义的核心接口。

## IFluxDefinition

```csharp
public interface IFluxDefinition<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

### 方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `GetReturnOp()` | `TOper` | 返回哪个枚举值代表终止指令 |
| `GetArity(byte op)` | `int` | 操作数个数。Immediate/Return 返回 0 |
| `GetKind(byte op)` | `OpType` | Immediate / Instruction / Return |
| `GetPrecedence(TOper op)` | `int` | 优先级。数值越大越优先结合 |
| `GetPair(TOper op)` | `OpPair<TOper>` | 括号配对信息 |
| `GetAssociativity(TOper op)` | `Associativity` | Left / Right |
| `ResolveToken(TOper op, TokenContext ctx)` | `TOper` | Token 消歧：根据上下文将同一符号映射为不同语义 |
| `Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers)` | `TData` | 解释器路径：执行运算 |

### ResolveToken

Lexer 扫描符号时无法判断上下文（期望操作数还是期望运算符）。`ResolveToken` 在 Token 生成后根据 `TokenContext` 做二次消歧。

```csharp
// '-' 在期望操作数时是一元取负，否则是二元减法
public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
{
    if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
        return FloatOp.Neg;
    return op;
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
public interface IFluxJITDefinition<TData, TOper>
    : IFluxDefinition<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
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
