# IFluxDefinition / IFluxJITDefinition

Core interfaces for operator semantics.

## IFluxDefinition

```csharp
public interface IFluxDefinition<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

### Methods

| Method | Returns | Description |
|------|------|------|
| `GetReturnOp()` | `TOper` | Returns which enum value represents the termination instruction |
| `GetArity(byte op)` | `int` | Operand count. Immediate/Return return 0 |
| `GetKind(byte op)` | `OpType` | Immediate / Instruction / Return |
| `GetPrecedence(TOper op)` | `int` | Precedence. Higher values bind more tightly |
| `GetPair(TOper op)` | `OpPair<TOper>` | Bracket pairing information |
| `GetAssociativity(TOper op)` | `Associativity` | Left / Right |
| `ResolveToken(TOper op, TokenContext ctx)` | `TOper` | Token disambiguation: maps the same symbol to different semantics based on context |
| `Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers)` | `TData` | Interpreter path: performs the computation |

### ResolveToken

The lexer cannot determine context (operand expected vs. operator expected) when scanning symbols. `ResolveToken` performs secondary disambiguation after token generation based on `TokenContext`.

```csharp
// '-' is unary negation when operand expected, binary subtraction otherwise
public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
{
    if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
        return FloatOp.Neg;
    return op;
}
```

| TokenContext | Meaning |
|---|---|
| `OperandExpected` | Current position expects an operand (expression start, after left paren, after operator) |
| `OperatorExpected` | Current position expects an operator (after operand, after right paren) |

### Error Handling in Compute

Returning a value other than `default` causes the executor to write it to R0 and trigger an early exit.

## IFluxJITDefinition

```csharp
public interface IFluxJITDefinition<TData, TOper>
    : IFluxDefinition<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
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
