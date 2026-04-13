# 示例：R0 短路错误处理

演示如何利用 R0 错误寄存器实现运行时短路——公式计算过程中一旦检测到错误条件，立即终止并返回错误值。

## 原理

每条指令执行后，框架检查 R0 寄存器。若 R0 中的值不等于 `default(TData)`，执行立即终止并将 R0 作为整条公式的返回值。`Compute()` 或 `GetExpression()` 返回非 default 值即为向 R0 写入错误。

## 场景：带范围校验的除法

公式 `[a] / [b] + [c]`，要求：
- 除数为 0 → 返回 `float.NaN`，且 `[c]` 不会被加到结果中
- 结果 > 1e6 → 返回 `float.PositiveInfinity`，表示溢出

## 操作符枚举

```csharp
public enum SafeMathOp : byte
{
    Const, Add, Sub, Mul, Div,
    LParen, RParen, Return,
}
```

## 定义体（解释器路径）

```csharp
public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
{
    switch ((SafeMathOp)op)
    {
        case SafeMathOp.Div:
            float b = regs[inst.Arg1];
            if (Math.Abs(b) < float.Epsilon)
                return float.NaN;           // 写入 R0，触发短路
            float a = regs[inst.Arg0];
            float result = a / b;
            if (Math.Abs(result) > 1e6f)
                return float.PositiveInfinity; // 写入 R0，触发短路
            return result;

        case SafeMathOp.Add:
            float sum = regs[inst.Arg0] + regs[inst.Arg1];
            if (float.IsInfinity(sum))
                return float.PositiveInfinity;
            return sum;

        case SafeMathOp.Sub:
            return regs[inst.Arg0] - regs[inst.Arg1];

        case SafeMathOp.Mul:
            return regs[inst.Arg0] * regs[inst.Arg1];

        default:
            return 0f;
    }
}
```

## 定义体（JIT 路径）

与解释器语义一致：

```csharp
private static readonly Expression NanExpr   = Expression.Constant(float.NaN);
private static readonly Expression InfExpr   = Expression.Constant(float.PositiveInfinity);
private static readonly Expression EpsExpr   = Expression.Constant(float.Epsilon);
private static readonly Expression ZeroExpr  = Expression.Constant(0f);
private static readonly Expression LimitExpr = Expression.Constant(1e6f);

public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
{
    switch ((SafeMathOp)op)
    {
        case SafeMathOp.Div:
            var b = regs[inst.Arg1];
            var a = regs[inst.Arg0];
            var divResult = Expression.Divide(a, b);
            return Expression.Condition(
                Expression.LessThan(Expression.Call(typeof(Math).GetMethod("Abs", [typeof(float)])!, b), EpsExpr),
                NanExpr,    // 除零 → NaN 短路
                Expression.Condition(
                    Expression.GreaterThan(Expression.Call(typeof(Math).GetMethod("Abs", [typeof(float)])!, divResult), LimitExpr),
                    InfExpr, // 溢出 → ∞ 短路
                    divResult));

        case SafeMathOp.Add:
            var sum = Expression.Add(regs[inst.Arg0], regs[inst.Arg1]);
            return Expression.Condition(
                Expression.Call(typeof(float).GetMethod("IsInfinity", [typeof(float)])!, sum),
                InfExpr,
                sum);

        default:
            return ZeroExpr;
    }
}
```

## 使用

```csharp
var runner = new FluxAssembler<float, SafeMathOp, SafeMathDef>(new SafeMathDef());

var formula = runner.Compile(new[]
{
    // [a] / [b] + [c]
    C(0f), Op(SafeMathOp.Div), C(0f), Op(SafeMathOp.Add), C(0f),
});

// 正常运行：10 / 2 + 3 = 8
float ok = runner.Instantiate(formula)
    .SetIndex(0, 10f).SetIndex(1, 2f).SetIndex(2, 3f).Run();
// → 8

// 除零短路：10 / 0 + 3 → NaN（Add 不会执行）
float err = runner.Instantiate(formula)
    .SetIndex(0, 10f).SetIndex(1, 0f).SetIndex(2, 3f).Run();
// → NaN

// 溢出短路：1e7 / 1 + 3 → ∞
float overflow = runner.Instantiate(formula)
    .SetIndex(0, 1e7f).SetIndex(1, 1f).SetIndex(2, 3f).Run();
// → ∞
```

## 要点

- R0 在**每条指令后**检查，不是只在公式末尾
- `Compute` 和 `GetExpression` 返回值一致时会写入目标寄存器（Dest）；返回非 default 时写入 R0
- 不需要 try-catch，错误通过返回值传播
- 短路发生后，后续指令完全跳过——避免无效计算和潜在崩溃
