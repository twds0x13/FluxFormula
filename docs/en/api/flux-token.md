# FluxToken

Lexical unit, the atomic building block of infix expressions.

## Signature

```csharp
public struct FluxToken<TData>
    where TData : unmanaged
```

v3.0.0 removed the `TOper` generic parameter — the operator enum is an internal detail of the definition; Token only stores a `byte` opcode.

## Fields

| Field | Type | Description |
|------|------|------|
| `Oper` | `byte` | Opcode (produced by definition's `ResolveToken()`) |
| `Data` | `TData` | Data value (meaningful only for Immediate-type tokens) |

## Usage

```csharp
// Construct an immediate token — Oper defined by the definition
new FluxToken<float> { Oper = (byte)MathOp.Const, Data = 3.14f };

// Construct an operator token
new FluxToken<float> { Oper = (byte)MathOp.Add };
// Data is default(float) = 0f, carries no meaning
```

## Encoding Conventions

- **Immediate Token**: `Oper`'s `GetKind()` returns `OpType.Immediate`; `Data` carries a concrete value
- **Operator Token**: `Oper`'s `GetKind()` returns `OpType.Instruction`; `Data` is ignored
- **Bracket Token**: `Oper`'s `GetPair()` returns `PairRole = Left/Right`

## See Also

- [IDefinition](./idefinition) — Token.Oper bytecodes are produced by the definition
- [FluxAssembler](./flux-assembler) — compilation entry point for token arrays
