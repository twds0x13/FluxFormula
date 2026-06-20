# Full Example: FloatMathDef

A copy-paste-ready floating-point arithmetic definition.

## Operator Enum

```csharp
public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return,
}
```

## Definition Body

```csharp
public readonly struct FloatMathDef : IFluxJITDefinition<float, FloatOp>
{
    public FloatOp GetReturnOp() => FloatOp.Return;

    public int GetArity(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => (FloatOp)op switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    public int GetPrecedence(FloatOp op) => op switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1,
        FloatOp.Mul => 2, FloatOp.Div => 2,
        FloatOp.Neg => 3,
        _           => 0,
    };

    public OpPair<FloatOp> GetPair(FloatOp op) => op switch
    {
        FloatOp.LParen => new OpPair<FloatOp> { PairRole = Pair.Left },
        FloatOp.RParen => new OpPair<FloatOp>
        {
            PairRole   = Pair.Right,
            TargetLeft = FloatOp.LParen,
        },
        _ => new OpPair<FloatOp> { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(FloatOp op) => op switch
    {
        FloatOp.Neg => Associativity.Right,
        _           => Associativity.Left,
    };

    // Interpreter
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return (FloatOp)op switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return (FloatOp)op switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero),
                nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
}
```

## Usage

```csharp
var def    = new FloatMathDef();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);

// 1 + 2 * 3 = 7
var tokens = new FluxToken<float, FloatOp>[]
{
    new() { Oper = FloatOp.Const, Data = 1f },
    new() { Oper = FloatOp.Add },
    new() { Oper = FloatOp.Const, Data = 2f },
    new() { Oper = FloatOp.Mul },
    new() { Oper = FloatOp.Const, Data = 3f },
};
Assert.That(runner.Build(tokens).Run(), Is.EqualTo(7f).Within(1e-6f));

// (1 + 2) * 3 = 9
// -5
// 1 / 0 → NaN
```

Full source at `tests/FluxFormula.Core.Tests/SmokeTest.cs` (standalone, no Unity required).
