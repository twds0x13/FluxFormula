# IFluxDefinition / IFluxExprDefinition

Core interfaces for operator semantics. v3.0.0 removed the `TOper` generic parameter — all operator-related methods now accept and return `byte`; the operator enum is an internal implementation detail of the definition.

## IFluxDefinition

```csharp
public interface IFluxDefinition<TData>
    where TData : unmanaged
```

### Methods

| Method | Returns | Description |
|------|------|------|
| `GetReturnOp()` | `byte` | Returns which opcode represents the termination instruction |
| `GetArity(byte op)` | `int` | Operand count. Immediate/Return return 0 |
| `GetKind(byte op)` | `OpType` | Immediate / Instruction / Return |
| `GetPrecedence(byte op)` | `int` | Precedence. Higher values bind more tightly |
| `GetPair(byte op)` | `OpPair` | Bracket pairing information |
| `GetAssociativity(byte op)` | `Associativity` | Left / Right |
| `ResolveToken(byte oper, TokenContext ctx)` | `byte` | Token disambiguation: maps the same symbol to different semantics based on context. Returns 0 to skip |
| `Compute(byte op, Instruction inst, Span<TData> registers)` | `TData` | Interpreter path: performs the computation |
| `GetOperatorName(byte op)` | `string` | Display name for the opcode (DIM, returns null by default). Editor/toolchain query point |

### ResolveToken

The lexer cannot determine context (operand expected vs. operator expected) when scanning symbols. `ResolveToken` performs secondary disambiguation after token generation based on `TokenContext`.

```csharp
// Definition-internal enum
enum MathOp : byte { Const = 0, Add, Sub, Mul, Div, Neg }

// '-' is unary negation when operand expected, binary subtraction otherwise
public byte ResolveToken(byte oper, TokenContext ctx)
{
    if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
        return (byte)MathOp.Neg;
    return oper;
}
```

| TokenContext | Meaning |
|---|---|
| `OperandExpected` | Current position expects an operand (expression start, after left paren, after operator) |
| `OperatorExpected` | Current position expects an operator (after operand, after right paren) |

### Error Handling in Compute

Returning a value other than `default` causes the executor to write it to R0 and trigger an early exit.

## IFluxExprDefinition

```csharp
public interface IFluxExprDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
```

### Additional Method

| Method | Returns | Description |
|------|------|------|
| `GetExpression(byte op, Instruction inst, ParameterExpression[] regs)` | `Expression` | JIT path: returns a LINQ expression tree |

`regs` is an array of 256 `ParameterExpression` instances, indexed by register number. The return value is a pure computation expression; assignment and error checking are automatically wrapped by the JIT compiler.

## Implementation Requirements

- The implementing type should be a `readonly struct`; if used as the `TDef` generic parameter, it must satisfy `unmanaged`
- Mark all methods with `[MethodImpl(AggressiveInlining)]`
- `GetExpression` and `Compute` must be semantically consistent: same input produces same output

## Complete Implementation Example

```csharp
enum MathOp : byte { Const = 0, Add, Sub, Mul, Div, Neg, Return = 255 }

readonly struct MathDef : IFluxExprDefinition<float>
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

    public float Compute(byte op, Instruction inst, Span<float> regs)
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

