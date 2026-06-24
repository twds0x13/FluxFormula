# Platform Adaptation: JIT Detection & Fallback

`FluxPlatform` is the global JIT-availability switch. Its core design question: **how to detect JIT unavailability at runtime, fall back automatically, and avoid wasted retries?**

## Platforms Without JIT

`Expression.Compile()` is unsupported on:

- **IL2CPP** (Unity's AOT backend): iOS, WebGL, most consoles
- **NativeAOT** (.NET native AOT deployment)
- **Mono Full AOT** (some Android configurations)

On these platforms, `Expression.Compile()` throws `PlatformNotSupportedException`.

## One-Shot Detection + Global Fallback

```csharp
internal static class FluxPlatform
{
    private static volatile bool _jitDisabled;
    public static bool IsJitDisabled => _jitDisabled;
    public static void DisableJit() => _jitDisabled = true;
}
```

- **`volatile`**: Multi-thread visibility (future-proofing for async compilation).
- **Irreversible**: Once disabled, JIT is never re-attempted for the process lifetime.
- **Manual trigger**: Called by `FluxAssembler.Instantiate` on JIT compilation failure, or proactively by users on known-AOT platforms.

## Fallback Chain

```
FluxAssembler.Instantiate(jit: true)
  └→ FluxJITCompiler.Compile()
       └→ Expression.Compile() throws
            └→ FluxPlatform.DisableJit()
                 └→ All subsequent Instantiate calls skip JIT path
```

Fallback is transparent and automatic. The caller gets a `FluxInstance` that runs on the interpreter path. The only difference is performance (2ns → 27ns), not correctness.

## Why Not Default `jit: true`?

`jit` defaults to `false` because:
1. **Safe default**: interpreter works everywhere; JIT does not.
2. **Explicit opt-in**: users must acknowledge their target platform supports JIT.
3. **First-failure cost**: the exception throw/catch on first JIT attempt has measurable overhead.

## Test Coverage Limitation

`DisableJit()` must NOT be called in unit tests — it is a global irreversible switch that poisons JIT coverage for the entire test suite. JIT fallback correctness is verified indirectly through IL2CPP/WebGL integration tests and `JitConsistencyTests`. See [[test-coverage-boundary]].
