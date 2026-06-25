# FluxFormula Core

Zero-allocation, pure-C# formula pipeline. No Unity dependency — forkable to any .NET runtime.

```
string → Lex → Compile → Evaluate → result
```

## What's in this package

- `FluxLexer` — handwritten scanner, configurable variable/operator patterns
- `FluxAssembler` — entry point: `Compile()` + `Instantiate()` + `Build()`
- `FluxCompiler` — shunting-yard algorithm, R0 short-circuit support
- `FluxJITCompiler` — LINQ Expression-tree JIT (`FLUX_FAST_EXPRESSION_COMPILER` for FastExpressionCompiler)
- `FluxEvaluator` — interpreted VM (fallback for IL2CPP/AOT)
- `FluxInstance` — `ref struct` fluent API: `Set()` → `Run()`
- `FluxInjector` — unsafe data injection with binary-search slot lookup

## Quick start

```csharp
// 1. Define your operator system (impl IFluxJITDefinition<TData, TOper>)
var def = new FloatMathDef();
var assembler = new FluxAssembler<float, FloatMathDef>(def);

// 2. Write a formula string
var lexResult = new FluxLexer<float>(config).Lex("a + b * 2");

// 3. Compile + instantiate + run
float result = assembler
    .Build(lexResult.Tokens, jit: true)
    .Set("a", 10f)
    .Set("b", 5f)
    .Run();
```

## Standalone testing (no Unity)

```bash
dotnet test tests/FluxFormula.Core.Tests/
```

## License

MIT
