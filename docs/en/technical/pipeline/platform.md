# Platform Adaptation: JIT Detection, IL Availability & Fallback

`FluxPlatform` is the global platform capability detector and JIT degradation switch. Its core design question: **how do you detect JIT and IL emission availability at runtime, degrade tier by tier automatically, and avoid wasted retries?**

## Dual-Flag System

```csharp
internal static class FluxPlatform
{
    private static volatile bool _jitDisabled;

    /// <summary>Whether Expression.Compile() is unavailable (degradation detected).</summary>
    public static bool IsJitDisabled => _jitDisabled;

    /// <summary>Whether DynamicMethod / ILGenerator is available.</summary>
    public static bool IsIlSupported =>
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    public static void DisableJit() => _jitDisabled = true;
}
```

Two flags control different compilation paths:

| Flag | Meaning | Detection | Controls |
|------|---------|-----------|----------|
| `IsIlSupported` | DynamicMethod available | `RuntimeFeature.IsDynamicCodeSupported` | IL emission path |
| `IsJitDisabled` | Expression.Compile() has failed | Runtime exception triggers `DisableJit()` | Expression Tree path |

`IsIlSupported` is **pre-detected** (result determined at process start, zero runtime overhead); `IsJitDisabled` is a **runtime flag** (set after the first JIT failure).

## IL Availability Detection

`RuntimeFeature.IsDynamicCodeSupported` is a static property provided by the .NET runtime:

- **Mono / CoreCLR**: returns `true`; `DynamicMethod` and `ILGenerator` are available
- **IL2CPP / NativeAOT**: returns `false`; any `DynamicMethod` construction throws `PlatformNotSupportedException`

`CompileDelegate` short-circuits early at the entry point via `IsIlSupported`, avoiding useless try-catch on IL2CPP platforms:

```csharp
if (FluxPlatform.IsIlSupported)
{
    // IL path entered only on Mono/CoreCLR
}
// IL2CPP skips directly to Expression Tree
```

## Platforms Without JIT

`Expression.Compile()` is unsupported on:

- **IL2CPP** (Unity's AOT backend): iOS, WebGL, most consoles
- **NativeAOT** (.NET native AOT deployment)
- **Mono Full AOT** (some Android configurations)

On these platforms, `Expression.Compile()` calls throw `PlatformNotSupportedException`. Note: IL2CPP platforms have `IsIlSupported` as false, so the IL path is never entered. NativeAOT and Mono Full AOT may have `IsIlSupported` true depending on runtime configuration; the IL path's degradation is handled by try-catch.

## One-Shot Detection + Global Fallback

`DisableJit()` design points:

- **`volatile`**: ensures multi-thread visibility. While not critical for Unity's main-thread scenarios, it reserves correctness for potential async compilation use cases.
- **Irreversible**: once JIT unavailability is detected, no further attempts are made for the entire process lifetime. There is no `EnableJit()`; JIT capability does not recover at runtime.
- **Manual trigger**: `DisableJit()` is called by `CompileDelegate` when Expression Tree compilation fails. It can also be called proactively by users (e.g., to skip an unnecessary first attempt on known AOT platforms).

## Three-Tier Degradation Chain

```
FluxAssembler.Instantiate(jit: true)
  └→ TryResolveJitDelegate (FormulaCache lookup)
       └→ CompileDelegate (compiler selector)
            ├─ (1) FluxILCompiler.Compile()       ← IL emission (when IsIlSupported is true)
            │    └→ PlatformNotSupportedException → degrade to (2)
            └─ (2) FluxJITCompiler.Compile()      ← Expression Tree (universal fallback)
                 └→ PlatformNotSupportedException → FluxPlatform.DisableJit()
                      └→ (3) fall back to interpreter path
```

Fallback is **transparent and automatic**. The caller does not need to choose between `Instantiate(jit: true)` and `Instantiate(jit: false)`. On JIT failure, `Instantiate` internally falls back to the interpreter path, and the returned `FluxInstance` runs on the interpreter.

## Why Not Default `jit: true`?

The `jit` parameter of `Instantiate` defaults to `false`. This is because:

1. **Safe default**: the interpreter works on every platform. JIT does not. Defaulting to JIT would guarantee a first-call failure on IL2CPP platforms.
2. **Explicit opt-in**: users must explicitly state their target platform supports JIT. This is surfaced in Unity's Inspector as a `FluxAsset` JIT toggle with a UI hint.
3. **Fallback cost**: the first JIT failure's exception throw and catch has measurable overhead. If the target platform is known to lack JIT, pass `false` directly to avoid waste.

## Unity Integration

On the Unity side, `FluxAsset` provides a `UseJit` property, toggleable in the Inspector. When enabled, the Asset panel shows a platform compatibility warning ("JIT is unavailable on IL2CPP platforms"). At runtime, `FluxAssembler.Instantiate(formula, jit: asset.UseJit)` passes the user's choice.

## Test Coverage Limitation

`DisableJit()` must NOT be called in unit tests. It is a global irreversible switch that would contaminate the entire test suite's JIT coverage measurement. JIT fallback correctness is indirectly ensured by:

- IL2CPP/WebGL platform integration tests (not run in CI)
- `JitConsistencyTests` verifying JIT-to-interpreter semantic equivalence (both paths produce the same result in the same process)

See [[test-coverage-boundary]].
