# Example: Burst Jobs Evaluation

> **Required package:** `com.twds0x13.fluxformula.burst` (3.3+)
> This package depends on `com.unity.burst` and `com.unity.collections`, pulled automatically by UPM. Pure .NET projects (non-Unity) do not need this package.

The following examples demonstrate three usage modes of `FluxBurstInstance<TData, TDef>`: synchronous evaluation, async Job scheduling, and multi-Job concurrency.

## Synchronous Evaluation

Call `Run()` to evaluate synchronously on the current thread. Suitable for single evaluations or Editor scripts.

```csharp
using FluxFormula.Core;
using FluxFormula.Burst;
using Unity.Collections;

var assembler = new FluxAssembler<float, MathDef>(new MathDef());
var formula = assembler.Compile(new FluxToken<float>[]
{
    new() { Oper = (byte)MathOp.Const, Data = 100f },
    new() { Oper = (byte)MathOp.Add },
    new() { Oper = (byte)MathOp.Const, Data = 50f },
});

using var job = assembler.CreateBurstInstance(formula);
float result = job.Run();           // 150
Debug.Log(result);
```

Use `Set` and `SetIndex` to inject variable values before `Run`, matching the `FluxInstance` chaining API:

```csharp
var formula = assembler.Compile(lexer.Lex("atk * 2 + bonus"));
using var job = assembler.CreateBurstInstance(formula);

job.Set("atk", 80f).Set("bonus", 25f);
float damage = job.Run();          // 185
```

## Async Job Scheduling

Call `Schedule()` to submit evaluation to the Unity Job system without blocking the main thread.

```csharp
using var job = assembler.CreateBurstInstance(formula)
    .Set("atk", 120f)
    .Set("def", 40f);

var handle = job.Schedule();
// main thread continues other work
handle.Complete();
float damage = job.Result;         // reads result from R1 bus
```

## Multi-Job Concurrency

Each `FluxBurstInstance` holds independent `NativeArray<byte>` and `NativeArray<TData>`, safe for concurrent scheduling.

```csharp
var job1 = assembler.CreateBurstInstance(damageFormula)
    .Set("atk", 100f);
var job2 = assembler.CreateBurstInstance(healFormula)
    .Set("wis", 60f);

var h1 = job1.Schedule();
var h2 = job2.Schedule();
JobHandle.CompleteAll(h1, h2);

float dmg = job1.Result;   // damage result
float heal = job2.Result;  // healing result

job1.Dispose();
job2.Dispose();
```

`Schedule()` accepts a dependency for chaining multiple Jobs:

```csharp
var handle1 = job1.Schedule();
var handle2 = job2.Schedule(handle1);  // job2 waits for job1
handle2.Complete();
```

## Notes

- Always call `Dispose()` after use to release `NativeArray`. Prefer `using` declarations.
- JIT is unavailable inside Jobs. `FluxBurstEvaluator` is a pure interpreter compiled by Burst to near-JIT performance.
- `Set(string)` resolves variable names on the managed heap. Prefer `SetIndex` by slot index in hot paths.
- Use Burst Inspector to verify zero managed escapes in the `Execute` method.

## See Also

- [FluxChain API](../api/flux-chain) — chain formula API
- [FluxAssembler](../api/flux-assembler) — compilation entry point
- [Burst Documentation](https://docs.unity3d.com/Packages/com.unity.burst@latest)
