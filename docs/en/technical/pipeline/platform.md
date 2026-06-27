# Platform Adaptation: JIT Detection & Fallback

`FluxPlatform` is the global JIT-availability switch. Its core design question: **how do you detect JIT unavailability at runtime, fall back automatically, and avoid wasted retries?**

## Platforms Without JIT

`Expression.Compile()` is unsupported on:

- **IL2CPP** (Unity's AOT backend): iOS, WebGL, most consoles
- **NativeAOT** (.NET native AOT deployment)
- **Mono Full AOT** (some Android configurations)

On these platforms, any `Expression.Compile()` call throws `PlatformNotSupportedException`.

## One-Shot Detection + Global Fallback

```csharp
internal static class FluxPlatform
{
    private static volatile bool _jitDisabled;

    public static bool IsJitDisabled => _jitDisabled;

    public static void DisableJit() => _jitDisabled = true;
}
```

Design points:

- **`volatile`**: ensures multi-thread visibility. While not critical for Unity's main-thread scenarios, it reserves correctness for potential async compilation use cases.
- **Irreversible**: once JIT unavailability is detected, no further attempts are made for the entire process lifetime. There is no `EnableJit()`; JIT capability does not recover at runtime.
- **Manual trigger**: `DisableJit()` is called by `FluxAssembler.Instantiate` when a JIT compilation exception is caught. It can also be called proactively by users (e.g., to skip an unnecessary first attempt on known IL2CPP platforms).

## Fallback Trigger Chain

```
FluxAssembler.Instantiate(jit: true)
  └→ FluxJITCompiler.Compile()
       └→ Expression.Compile() throws PlatformNotSupportedException
            └→ FluxPlatform.DisableJit()
                 └→ subsequent Instantiate calls skip the JIT path
```

Fallback is **transparent and automatic**. The caller does not need to choose between `Instantiate(jit: true)` and `Instantiate(jit: false)`. On JIT failure, `Instantiate` internally falls back to the interpreter path, and the returned `FluxInstance` runs on the interpreter.

```csharp
public FluxInstance<TData, TDef> Instantiate(FluxFormula<TData, TDef> formula, bool jit = false)
{
    if (jit && !FluxPlatform.IsJitDisabled)
    {
        try
        {
            // JIT compilation...
        }
        catch (Exception ex) when (
            ex is PlatformNotSupportedException
            || ex is NotSupportedException
            || ex is InvalidOperationException)
        {
            FluxPlatform.DisableJit();
            // fall through to interpreter path below
        }
    }
    // interpreter path...
}
```

## Why Not Default `jit: true`?

The `jit` parameter of `Instantiate` defaults to `false`. This is because:

1. **Safe default**: the interpreter works on every platform. JIT does not. Defaulting to JIT would guarantee a first-call failure on IL2CPP platforms.
2. **Explicit opt-in**: users must explicitly state "I confirm my target platform supports JIT." This is surfaced in Unity's Inspector as a `FluxAsset` JIT toggle with a UI hint.
3. **Fallback cost**: the first JIT failure's exception throw and catch has measurable overhead. If the target platform is known to lack JIT, pass `false` directly to avoid waste.

## Unity Integration

On the Unity side, `FluxAsset` provides a `UseJit` property, toggleable in the Inspector. When enabled, the Asset panel shows a platform compatibility warning ("JIT is unavailable on IL2CPP platforms"). At runtime, `FluxAssembler.Instantiate(formula, jit: asset.UseJit)` passes the user's choice.

## Test Coverage Limitation

`DisableJit()` must NOT be called in unit tests. It is a global irreversible switch that would contaminate the entire test suite's JIT coverage measurement. JIT fallback correctness is indirectly ensured by:

- IL2CPP/WebGL platform integration tests (not run in CI)
- `JitConsistencyTests` verifying JIT-to-interpreter semantic equivalence (both paths produce the same result in the same process)

See [[test-coverage-boundary]].
