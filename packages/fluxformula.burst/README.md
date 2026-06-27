# FluxFormula Burst

Burst-compiled evaluator for FluxFormula. Opt-in — add `Unity.Burst` and `FluxFormula.Burst` to your assembly references.

## What's in this package

- `FluxBurstEvaluator<TData, TDef>` — Burst-compatible formula interpreter. Takes raw `byte*` instruction stream instead of `ReadOnlySpan<Instruction>`, registers passed via `TData*` from caller. Zero managed allocation, fully compatible with Unity Jobs and Burst.
- `FluxBurstAssemblerExtensions` — extension methods on `FluxAssembler<TData, TDef>`: `CreateBurstInstance()` for synchronous evaluation and `ScheduleBurst()` for jobified parallel execution.

## Usage

```csharp
using FluxFormula.Core;
using FluxFormula.Burst;
using Unity.Burst;
using Unity.Jobs;

// Synchronous
var assembler = new FluxAssembler<float, FloatMathDef>(default);
var formula = assembler.Compile(new FluxLexer<float>(config).Lex("[atk] * 2 + [bonus]"));
using var instance = assembler.CreateBurstInstance(formula);
instance.Set("atk", 100f).Set("bonus", 50f);
float result = instance.Run();

// Asynchronous (jobified)
var job = assembler.ScheduleBurst(formula, new[] { ("atk", 100f), ("bonus", 50f) });
job.Complete();
float result2 = job.Result;
```

## Constraints

- JIT compilation is unavailable inside Burst jobs. Formulas must be pre-compiled before entering job context.
- `FluxBurstInstance` implements `IDisposable`. Always call `Dispose()` or use `using`.
- Requires `com.unity.burst` and `com.unity.collections`.

## Dependencies

- `com.twds0x13.fluxformula.core` ≥ 3.3.0
- `com.twds0x13.fluxformula` ≥ 3.3.0
- `com.unity.burst` ≥ 1.6.0

## License

MIT
