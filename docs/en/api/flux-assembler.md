# FluxAssembler

Compilation entry point. Compiles lexical tokens into bytecode and instantiates executors.

## Signature

```csharp
public readonly unsafe ref struct FluxAssembler<TData, TOper, TDef>
    where TData : unmanaged
    where TOper : unmanaged, Enum
    where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
```

## Constructor

```csharp
public FluxAssembler(TDef definition)
```

Takes an operator definition instance (value type, no heap allocation).

## Methods

### Compile (Lexer Path)

```csharp
public FluxFormula<TData, TOper> Compile(LexResult<TData, TOper> lexResult)
```

Accepts the `LexResult` returned by `FluxLexer.Lex()` and compiles it directly to bytecode. LexResult carries the token array and variable name information; Compile writes variable names into `FluxFormula.VariableSlots`.

### Compile (Token Path)

```csharp
public FluxFormula<TData, TOper> Compile(
    ReadOnlySpan<FluxToken<TData, TOper>> tokens,
    string[] varNames = null)
```

Compiles an infix token sequence into bytecode `Formula`. Internally executes the shunting-yard algorithm, allocates and populates an `Instruction[]` buffer. The Formula can be cached and reused.

### Instantiate

```csharp
public FluxInstance<TData, TOper, TDef> Instantiate(
    FluxFormula<TData, TOper> formula,
    bool jit = false)
```

Activates an existing Formula as an executable Instance.

- `jit: false` (default): Uses the interpreter path, stack-allocated registers
- `jit: true`: Attempts JIT first (Expression Tree compiled to delegate); auto-degrades if the platform does not support it (AOT)

### Build

```csharp
public FluxInstance<TData, TOper, TDef> Build(
    ReadOnlySpan<FluxToken<TData, TOper>> tokens,
    bool jit = false)
```

Combined `Compile()` + `Instantiate()` call. Suitable for one-shot formulas that don't need caching.

```csharp
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);
float r = runner.Build(tokens, jit: true).Run();
```

## Formula Type Classification

`Compile()` inspects the first token to determine `Formula` or `Modifier`:

| First Token | FluxType |
|-------------|----------|
| Immediate (e.g., Const) | `Formula` |
| Unary prefix operator (arity=1) | `Formula` |
| Left bracket (PairRole=Left) | `Formula` |
| Binary operator (arity≥2, non-bracket) | `Modifier` |
