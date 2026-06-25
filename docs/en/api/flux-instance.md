# FluxInstance

ref struct streaming executor. Stack-allocated, zero GC.

## Signature

```csharp
public ref struct FluxInstance<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
```

`ref struct` can only exist on the stack — cannot be boxed, cannot be a class field — ensuring zero heap allocation on the execution path.

## Methods

### Set

```csharp
public FluxInstance<TData, TDef> Set(string name, TData value)
```

Injects a value by variable name. Uses an inline binary search to locate the variable slot. All variables sharing the same name are written. Throws `ArgumentException` if `name` was not present in the Lexer's `VariablePatterns`.

```csharp
var inst = runner.Instantiate(formula);
float r = inst.Set("atk", 150f).Set("def", 50f).Run();
```

### SetIndex

```csharp
public FluxInstance<TData, TDef> SetIndex(int index, TData value)
```

Injects a value by positional index (the `index`-th Immediate data slot). No variable name validation.

- JIT path: writes to a compact payload array (linear index)
- Interpreter path: writes into the formula buffer at pre-scanned offsets

### Run

```csharp
public readonly TData Run()
```

Starts the computation engine and returns a `TData` result.

- v3.0.0: `FluxModifier` has no `Instantiate()` method — the "Modifier run standalone" error is now prevented at compile time
- If `_isJit == true`, invokes the JIT-compiled delegate
- Otherwise, creates a `FluxEvaluator`, `stackalloc`s registers, and executes the bytecode loop

### GetBuffer

```csharp
public readonly Instruction[] GetBuffer()
```

Returns the underlying `Instruction[]` buffer. Intended for debugging and benchmarking, not production paths.

## Usage

```csharp
// Single evaluation
float r = runner.Build(tokens).Run();

// Named variable injection
float r = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("def", 50f)
    .Run();

// Index-based injection
float r = runner.Instantiate(formula)
    .SetIndex(0, 3f)
    .SetIndex(1, 4f)
    .Run();
```

## v3.0.0 Changes

- `FluxInstance<TData, TOper, TDef>` → `FluxInstance<TData, TDef>` (3 params → 2 params)
- Removed `Type == Modifier` runtime check — `FluxModifier` type has no `Instantiate()`, ensuring compile-time safety

## See Also

- [FluxAssembler](./flux-assembler) — produces FluxInstance via Instantiate/Build
- [FluxFormula](./flux-formula) — bytecode container wrapped by Instance
