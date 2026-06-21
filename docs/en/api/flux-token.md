# FluxToken

Lexical unit, the atomic building block of infix expressions.

## Signature

```csharp
public struct FluxToken<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

## Fields

| Field | Type | Description |
|------|------|------|
| `Oper` | `TOper` | Operator enum value |
| `Data` | `TData` | Data value (meaningful only for Immediate-type tokens) |

## Usage

```csharp
// Construct an immediate token
new FluxToken<float, FloatOp> { Oper = FloatOp.Const, Data = 3.14f };

// Construct an operator token
new FluxToken<float, FloatOp> { Oper = FloatOp.Add };
// Data is default(float) = 0f, carries no meaning
```

## Encoding Conventions

- **Immediate Token**: `Oper`'s `GetKind()` returns `OpType.Immediate`; `Data` carries a concrete value
- **Operator Token**: `Oper`'s `GetKind()` returns `OpType.Instruction`; `Data` is ignored
- **Bracket Token**: `Oper`'s `GetPair()` returns `PairRole = Left/Right`

## See Also

- [IDefinition](./idefinition) — operator enum and definition linked to Token.Oper
- [FluxAssembler](./flux-assembler) — compilation entry point for token arrays
