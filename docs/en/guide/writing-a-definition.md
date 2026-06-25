# Writing a Definition

Define operator semantics. One implementation yields both interpreter and JIT execution paths. Since v3.0.0, the interface uses a single generic parameter — the operator enum is a `private` internal detail of the definition.

## Interface Overview

```csharp
public interface IFluxDefinition<TData>
    where TData : unmanaged
{
    byte GetReturnOp();                                               // Termination instruction
    int GetArity(byte op);                                            // Operand count
    OpType GetKind(byte op);                                          // Instruction classification
    int GetPrecedence(byte op);                                       // Operator precedence
    OpPair GetPair(byte op);                                          // Bracket pairing
    Associativity GetAssociativity(byte op);                          // Binding direction
    byte ResolveToken(byte oper, TokenContext ctx);                   // Token disambiguation
    TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers); // Interpreter computation
    string GetOperatorName(byte op);                                  // Display name (DIM, optional)
}

public interface IFluxJITDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
{
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers); // JIT expression
}
```

## Method Reference

### GetReturnOp

Returns which byte value represents the termination instruction. The compiler automatically inserts it at the end of the bytecode.

```csharp
public byte GetReturnOp() => (byte)MathOp.Return;
```

### GetArity

Number of operands. Immediate and Return types return 0. Maximum arity is 6, limited by Instruction's Arg0-Arg5 field count.

```csharp
public int GetArity(byte op) => ((MathOp)op) switch
{
    MathOp.Add => 2,   // left + right
    MathOp.Neg => 1,   // negation needs only one operand
    _ => 0,
};
```

### GetKind

Classifies opcodes into three `OpType` values:

```csharp
public OpType GetKind(byte op) => ((MathOp)op) switch
{
    MathOp.Const  => OpType.Immediate,   // Carries a data value
    MathOp.Return => OpType.Return,      // Terminates execution
    _             => OpType.Instruction, // Normal operation
};
```

### GetPrecedence

Operator precedence. Higher values bind more tightly. Typical assignment: add/sub = 1, mul/div = 2, unary prefix = 3, power = 4.

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

Defines bracket behavior. `OpPair` is a non-generic struct; `TargetLeft` and `EmitOpCode` are both `byte`:

```csharp
public struct OpPair
{
    public Pair PairRole;      // None / Left / Right
    public byte TargetLeft;    // The left opcode a right bracket targets
    public bool EmitOnMatch;   // Whether to emit an instruction on match
    public byte EmitOpCode;    // Which instruction to emit
    public bool IsSeparator;   // Argument separator (e.g., comma)
}
```

**Standard brackets:**

```csharp
public OpPair GetPair(byte op) => ((MathOp)op) switch
{
    MathOp.LParen => new OpPair { PairRole = Pair.Left },
    MathOp.RParen => new OpPair
    {
        PairRole   = Pair.Right,
        TargetLeft = (byte)MathOp.LParen,
        // EmitOnMatch defaults to false; LParen is just popped from the stack
    },
    _ => new OpPair { PairRole = Pair.None },
};
```

**Function call simulation (sin):**

In `sin(x)`, `sin` is a Left-pair, and `(` triggers `EmitOnMatch` to emit a `SinOp` instruction:

```csharp
(byte)MyOp.Sin => new OpPair
{
    PairRole    = Pair.Left,
    EmitOnMatch = false,     // Sin itself does not emit
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
    EmitOnMatch = true,       // Emit Sin instruction on match
    EmitOpCode  = (byte)MyOp.Sin,
},
```

### GetAssociativity

`Left` or `Right`. Binary operators typically use `Left` (`2 - 1 - 1 = 0`); unary prefix uses `Right`.

```csharp
public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
{
    MathOp.Neg => Associativity.Right,
    _          => Associativity.Left,
};
```

### ResolveToken

The lexer cannot determine whether the current position expects an operand or an operator. `ResolveToken` performs secondary disambiguation after token generation, mapping the same symbol to different opcodes based on `TokenContext`. Return `oper` directly if no disambiguation is needed; return `0` to skip.

```csharp
public byte ResolveToken(byte oper, TokenContext ctx)
{
    // '-' is unary negation when operand expected, binary subtraction otherwise
    if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
        return (byte)MathOp.Neg;
    return oper;
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
    return ((MathOp)op) switch
    {
        MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
        MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],

        // Division by zero → write NaN to dest register, triggering R0 early exit
        MathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
            ? float.NaN
            : regs[inst.Arg0] / regs[inst.Arg1],

        MathOp.Neg => -regs[inst.Arg0],
        _ => 0f,
    };
}
```

### GetExpression (JIT Path)

Must match `Compute` semantics exactly, expressed via LINQ Expressions:

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

### GetOperatorName (Optional)

Display name for the opcode, used by editors and toolchains. The DIM returns `null` by default — override to provide meaningful names.

```csharp
public string GetOperatorName(byte op) => ((MathOp)op).ToString();
```

## Error Handling: R0 Early Exit

If `Compute()` or `GetExpression()` returns a non-default value, that value is written to R0 (the error register). The executor checks R0 after each instruction; if non-default, execution terminates immediately and returns the error value.

## Performance Recommendations

- Mark all methods with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- Use `(MathOp)op` cast in switch expressions — zero boxing overhead
- For non-bracket ops on the hot path, return a default `Pair.None` instance directly from `GetPair`
