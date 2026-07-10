# FluxAssembler

Compilation entry point. Compiles lexical tokens into bytecode and instantiates executors.

## Signature

```csharp
public readonly unsafe struct FluxAssembler<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

Two generic parameters: data type `TData` + definition `TDef`. v3.0.0 removed the `TOper` generic parameter — the operator enum is now an internal implementation detail of the definition; the framework only sees `byte`.

## Construction

```csharp
public FluxAssembler(TDef definition)
```

Takes an operator definition instance (value type, no heap allocation).

## Methods

### Compile (Lexer Path)

```csharp
public FluxFormula<TData, TDef> Compile(LexResult<TData> lexResult)
```

Accepts the `LexResult` returned by `FluxLexer.Lex()` and compiles it directly to bytecode. LexResult carries the token array and variable name information; Compile writes variable names into `FluxFormula.VariableSlots`.

### Instantiate

```csharp
public FluxInstance<TData, TDef> Instantiate(
    FluxFormula<TData, TDef> formula,
    bool jit = false)
```

Activates an existing Formula as an executable Instance.

- `jit: false` (default): Uses the interpreter path, stack-allocated registers
- `jit: true`: Attempts JIT first (Expression Tree compiled to delegate); auto-degrades if the platform does not support it (AOT)

## Formula Type Classification

`Compile()` inspects the first token to determine `Formula` or `Modifier` (internal `FluxType` enum — v3.0.0 made it `internal`; external consumers distinguish via `FluxFormula` / `FluxModifier` types):

> **v5.5+**: If the first token comes from an `OperatorRule` with `Slots` declared, `Slots[0] < 0` determines whether a left operand is needed; otherwise falls back to `IFluxDefinition.GetFirstPosition`.

| First Token | Produced External Type |
|-------------|----------------------|
| Immediate (e.g., Const) | `FluxFormula<TData, TDef>` |
| Function-style prefix operator (Slots[0] ≥ 0) | `FluxFormula<TData, TDef>` |
| Left bracket (PairRole=Left) | `FluxFormula<TData, TDef>` |
| Infix operator (Slots[0] < 0 or GetFirstPosition=Left) | `FluxModifier<TData, TDef>` |

## See Also

- [FluxFormula](./flux-formula) — immutable bytecode container produced by Compile
- [FluxInstance](./flux-instance) — streaming executor returned by Instantiate
- [IDefinition](./idefinition) — custom operator definition interface
