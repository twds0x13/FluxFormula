# FluxAssembler

Compilation entry point. Compiles lexical tokens into bytecode and instantiates executors.

## Signature

```csharp
public readonly unsafe ref struct FluxAssembler<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
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

### Compile (Token Path)

```csharp
public FluxFormula<TData, TDef> Compile(
    ReadOnlySpan<FluxToken<TData>> tokens,
    string[] varNames = null)
```

Compiles an infix token sequence into bytecode `Formula`. Internally executes the shunting-yard algorithm, allocates and populates an `Instruction[]` buffer. The Formula can be cached and reused.

### Instantiate

```csharp
public FluxInstance<TData, TDef> Instantiate(
    FluxFormula<TData, TDef> formula,
    bool jit = false)
```

Activates an existing Formula as an executable Instance.

- `jit: false` (default): Uses the interpreter path, stack-allocated registers
- `jit: true`: Attempts JIT first (Expression Tree compiled to delegate); auto-degrades if the platform does not support it (AOT)

### Build

```csharp
public FluxInstance<TData, TDef> Build(
    ReadOnlySpan<FluxToken<TData>> tokens,
    bool jit = false)
```

Combined `Compile()` + `Instantiate()` call. Suitable for one-shot formulas that don't need caching.

```csharp
var runner = new FluxAssembler<float, MathDef>(def);
float r = runner.Build(tokens, jit: true).Run();
```

## Formula Type Classification

`Compile()` inspects the first token to determine `Formula` or `Modifier` (internal `FluxType` enum — v3.0.0 made it `internal`; external consumers distinguish via `FluxFormula` / `FluxModifier` types):

| First Token | Produced External Type |
|-------------|----------------------|
| Immediate (e.g., Const) | `FluxFormula<TData, TDef>` |
| Unary prefix operator (arity=1) | `FluxFormula<TData, TDef>` |
| Left bracket (PairRole=Left) | `FluxFormula<TData, TDef>` |
| Binary operator (arity≥2, non-bracket) | `FluxModifier<TData, TDef>` |

## v3.0.0 Changes

- `FluxAssembler<TData, TOper, TDef>` → `FluxAssembler<TData, TDef>` (3 params → 2 params)
- `LexResult<TData, TOper>` → `LexResult<TData>`
- `FluxToken<TData, TOper>` → `FluxToken<TData>` (`Oper` field is now `byte`)
- Compile-time cross-definition safety: `FluxFormula<float, MathDef>` and `FluxFormula<float, GameDef>` are different types — accidental connection won't compile

## See Also

- [FluxFormula](./flux-formula) — immutable bytecode container produced by Compile
- [FluxInstance](./flux-instance) — streaming executor returned by Instantiate
- [IDefinition](./idefinition) — custom operator definition interface
