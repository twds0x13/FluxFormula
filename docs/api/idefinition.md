# IFluxDefinition / IFluxExprDefinition

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
| `GetFirstPosition(byte op)` | `OperandPosition` | 首个操作数在操作符左侧（中缀 a+b）还是右侧（前缀 -x、max(a,b)）。默认 Left。Right 时表达式以本操作符开头不视为缺左操作数 |
| `ResolveToken(byte oper, TokenContext ctx)` | `byte` | Token 消歧：根据上下文将同一符号映射为不同语义。返回 0 表示不消歧 |
| `Compute(byte op, Instruction inst, Span<TData> registers)` | `TData` | 解释器路径：执行运算 |
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

## IFluxExprDefinition

```csharp
public interface IFluxExprDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
```

### 额外方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `GetExpression(byte op, Instruction inst, ParameterExpression[] regs)` | `Expression` | JIT 路径：返回 LINQ 表达式树 |

`regs` 是 256 个 `ParameterExpression`，索引对应寄存器号。返回值是纯计算表达式，赋值和错误检查由 JIT 编译器自动包装。

## 实现要求

- 实现类应为 `readonly struct`，若作为 `TDef` 泛型参数则须满足 `unmanaged`
- 热路径方法可使用 `[MethodImpl(AggressiveInlining)]` 减少调用开销
- `GetExpression` 与 `Compute` 必须语义一致：同一输入产生同一输出

## 完整实现示例

```csharp
using System;
using System.Linq.Expressions;
using FluxFormula.Core;

enum MathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}

readonly struct MathDef : IFluxExprDefinition<float>
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

    public OperandPosition GetFirstPosition(byte op) => (MathOp)op switch
    {
        MathOp.Add => OperandPosition.Left,
        MathOp.Sub => OperandPosition.Left,
        MathOp.Mul => OperandPosition.Left,
        MathOp.Div => OperandPosition.Left,
        _          => OperandPosition.Right,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    public float Compute(byte op, Instruction inst, Span<float> regs)
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
