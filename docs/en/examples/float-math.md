# Example: FloatMathDef

A copy-paste-ready floating-point arithmetic definition for v3.0.0.

> **v3.0.0**: The operator enum is now a `private` implementation detail. The definition implements `IFluxExprDefinition<TData>` (1 generic param). All operator-related methods use `byte`.

## Operator Enum

```csharp
enum MathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}
```

## Definition Body

```csharp
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

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    // Interpreter
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

## Usage

```csharp
var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);

// 1 + 2 * 3 = 7
var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
Assert.That(runner.Instantiate(formula).Run(), Is.EqualTo(7f).Within(1e-6f));

// (1 + 2) * 3 = 9
// -5
// 1 / 0 → NaN
```

### Key v3.0.0 Changes

- `IFluxExprDefinition<float, FloatOp>` → `IFluxExprDefinition<float>` — single generic param
- Operator enum (`MathOp`) is `private` — no longer needs to be `public`
- All method signatures use `byte` instead of enum type
- `OpPair<FloatOp>` → `OpPair` (non-generic)
- `FluxToken<float, FloatOp>` → `FluxToken<float>`; `Oper` is `byte`
- `FluxAssembler<float, FloatOp, FloatMathDef>` → `FluxAssembler<float, MathDef>`
- No more `sizeof(TOper) != 1` runtime check — `byte` is always 1 byte

Full source at `tests/FluxFormula.Core.Tests/TestDefinition.cs`.
