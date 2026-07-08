# FluxInstance

ref struct streaming executor. Stack-allocated, zero GC.

## Signature

```csharp
public ref struct FluxInstance<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
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
var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
float r = runner.Instantiate(formula).Run();

// Named variable injection
float r = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("def", 50f)
    .Run();

```

## See Also

- [FluxAssembler](./flux-assembler) — produces FluxInstance via Instantiate/Build
- [FluxFormula](./flux-formula) — bytecode container wrapped by Instance
