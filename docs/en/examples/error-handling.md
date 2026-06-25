# Example: R0 Short-Circuit Error Handling

Demonstrates runtime early-exit via the R0 error register — when an error condition is detected during formula evaluation, execution terminates immediately and returns the error value.

## How It Works

After each instruction executes, the framework checks the R0 register. If R0 holds a value other than `default(TData)`, execution stops immediately and R0 becomes the formula's return value. Returning a non-default value from `Compute()` or `GetExpression()` writes to R0.

## Scenario: Division with Range Validation

Formula `[a] / [b] + [c]` with constraints:
- Divisor is zero → return `float.NaN`；`[c]` is never added
- Result exceeds 1e6 → return `float.PositiveInfinity` (overflow)

## Operator Enum

```csharp
public enum SafeMathOp : byte
{
    Const, Add, Sub, Mul, Div,
    LParen, RParen, Return,
}
```

## Definition (Interpreter Path)

```csharp
public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
{
    switch ((SafeMathOp)op)
    {
        case SafeMathOp.Div:
            float b = regs[inst.Arg1];
            if (Math.Abs(b) < float.Epsilon)
                return float.NaN;           // Write to R0, trigger early exit
            float a = regs[inst.Arg0];
            float result = a / b;
            if (Math.Abs(result) > 1e6f)
                return float.PositiveInfinity; // Write to R0, trigger early exit
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

## Definition (JIT Path)

Semantically identical to the interpreter path:

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
                NanExpr,    // Division by zero → NaN early exit
                Expression.Condition(
                    Expression.GreaterThan(Expression.Call(typeof(Math).GetMethod("Abs", [typeof(float)])!, divResult), LimitExpr),
                    InfExpr, // Overflow → ∞ early exit
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

## Usage

```csharp
var runner = new FluxAssembler<float, SafeMathDef>(new SafeMathDef());

var formula = runner.Compile(new[]
{
    // [a] / [b] + [c]
    C(0f), Op((byte)SafeMathOp.Div), C(0f), Op((byte)SafeMathOp.Add), C(0f),
});

// Normal: 10 / 2 + 3 = 8
float ok = runner.Instantiate(formula)
    .SetIndex(0, 10f).SetIndex(1, 2f).SetIndex(2, 3f).Run();
// → 8

// Division by zero early exit: 10 / 0 + 3 → NaN (Add never executes)
float err = runner.Instantiate(formula)
    .SetIndex(0, 10f).SetIndex(1, 0f).SetIndex(2, 3f).Run();
// → NaN

// Overflow early exit: 1e7 / 1 + 3 → ∞
float overflow = runner.Instantiate(formula)
    .SetIndex(0, 1e7f).SetIndex(1, 1f).SetIndex(2, 3f).Run();
// → ∞
```

## Key Points

- R0 is checked **after each instruction**, not just at the end of the formula
- When `Compute` / `GetExpression` returns a normal value, it's written to the Dest register. A non-default return goes to R0
- No try-catch needed — errors propagate through the return value
- After an early exit, subsequent instructions are skipped entirely, avoiding wasted computation and potential crashes
