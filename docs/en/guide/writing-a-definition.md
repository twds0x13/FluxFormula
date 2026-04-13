# Writing a Definition

Implement `IFluxJITDefinition<TData, TOper>` to define operator semantics. One implementation yields both interpreter and JIT execution paths.

## Interface Overview

```csharp
public interface IFluxDefinition<TData, TOper>
{
    TOper GetReturnOp();                                      // Termination instruction
    int GetArity(byte op);                                    // Operand count
    OpType GetKind(byte op);                                  // Instruction classification
    int GetPrecedence(TOper op);                              // Operator precedence
    OpPair<TOper> GetPair(TOper op);                          // Bracket pairing
    Associativity GetAssociativity(TOper op);                 // Binding direction
    TOper ResolveToken(TOper op, TokenContext ctx);           // Token disambiguation
    TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers); // Interpreter computation
}

public interface IFluxJITDefinition<TData, TOper> : IFluxDefinition<TData, TOper>
{
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers); // JIT expression
}
```

## Method Reference

### GetReturnOp

Returns which enum value represents the termination instruction. The compiler automatically inserts it at the end of the bytecode.

```csharp
public FloatOp GetReturnOp() => FloatOp.Return;
```

### GetArity

Number of operands. Immediate and Return types return 0. Maximum arity is 6, limited by Instruction's Arg0-Arg5 field count.

```csharp
public int GetArity(byte op) => ((FloatOp)op) switch
{
    FloatOp.Add => 2,   // left + right
    FloatOp.Neg => 1,   // negation needs only one operand
    _ => 0,
};
```

### GetKind

Classifies opcodes into three `OpType` values:

```csharp
public OpType GetKind(byte op) => ((FloatOp)op) switch
{
    FloatOp.Const  => OpType.Immediate,   // Carries a data value
    FloatOp.Return => OpType.Return,      // Terminates execution
    _              => OpType.Instruction, // Normal operation
};
```

### GetPrecedence

Operator precedence. Higher values bind more tightly. Typical assignment: add/sub = 1, mul/div = 2, unary prefix = 3, power = 4.

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

Defines bracket behavior. `OpPair` maps syntactic brackets to semantic instructions.

```csharp
public struct OpPair<TOper>
{
    public Pair PairRole;      // None / Left / Right
    public TOper TargetLeft;   // The left opcode a right bracket targets
    public bool EmitOnMatch;   // Whether to emit an instruction on match
    public TOper EmitOpCode;   // Which instruction to emit
}
```

**Standard brackets:**

```csharp
FloatOp.LParen => new OpPair<FloatOp> { PairRole = Pair.Left },
FloatOp.RParen => new OpPair<FloatOp>
{
    PairRole   = Pair.Right,
    TargetLeft = FloatOp.LParen,
    // EmitOnMatch defaults to false; LParen is just popped from the stack
},
```

**Function call simulation (sin):**

In `sin(x)`, `sin` is a Left-pair, and `(` triggers `EmitOnMatch` to emit a `SinOp` instruction:

```csharp
SomeOp.Sin => new OpPair<SomeOp>
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,     // Sin itself does not emit
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
    EmitOnMatch = true,       // Emit Sin instruction on match
    EmitOpCode  = SomeOp.Sin,
},
```

### GetAssociativity

`Left` or `Right`. Binary operators typically use `Left` (`2 - 1 - 1 = 0`); unary prefix uses `Right`.

```csharp
public Associativity GetAssociativity(FloatOp op) => op switch
{
    FloatOp.Neg => Associativity.Right,
    _           => Associativity.Left,
};
```

### ResolveToken

The lexer cannot determine whether the current position expects an operand or an operator. `ResolveToken` performs secondary disambiguation after token generation, mapping the same symbol to different operators based on `TokenContext`. If no disambiguation is needed, return `op` directly.

```csharp
public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
{
    // '-' is unary negation when operand expected, binary subtraction otherwise
    if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
        return FloatOp.Neg;
    return op;
}
```

| TokenContext | Trigger condition |
|---|---|
| `OperandExpected` | Expression start, after left parenthesis, after binary operator |
| `OperatorExpected` | After operand, after right parenthesis |

### Compute (Interpreter Path)

```csharp
public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
{
    return ((FloatOp)op) switch
    {
        FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
        FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],

        // Division by zero → write NaN to dest register, triggering R0 early exit
        FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
            ? float.NaN
            : regs[inst.Arg0] / regs[inst.Arg1],

        FloatOp.Neg => -regs[inst.Arg0],
        _ => 0f,
    };
}
```

### GetExpression (JIT Path)

Must match `Compute` semantics exactly, expressed via LINQ Expressions:

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

## Error Handling: R0 Early Exit

If `Compute()` or `GetExpression()` returns a non-default value, that value is written to R0 (the error register). The executor checks R0 after each instruction; if non-default, execution terminates immediately and returns the error value.

```csharp
// Division-by-zero error propagated via NaN
C(1f), Op(Div), C(0f), Op(Add), C(5f)
// → 1/0 = NaN → early exit → entire formula returns NaN, Add is never executed
```

## Performance Recommendations

- Mark all methods with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- Use enum value casting rather than `Enum.Equals()` in Compute switch statements to avoid boxing
- For non-bracket ops on the hot path, return a default `Pair.None` instance directly from `GetPair`
