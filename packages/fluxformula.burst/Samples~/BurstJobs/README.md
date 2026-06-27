# Burst Jobs

Demonstrates three usage modes of `FluxBurstInstance<TData, TDef>`:

- **BurstSyncSample** — Synchronous evaluation via `Run()`. Single shot or Editor scripts.
- **BurstJobSample** — Async Job scheduling via `ScheduleBurst()` convenience method and manual `Schedule()` + `Complete()`.
- **BurstMultiJobSample** — Multi-Job concurrency with `NativeBytecodeCache` for shared bytecode.

## Setup

1. Ensure `com.unity.burst` and `com.unity.collections` are installed (pulled automatically by `com.twds0x13.fluxformula.burst`).
2. Attach any sample MonoBehaviour to a GameObject.
3. Right-click the component header → choose a `[ContextMenu]` action, or call from your own code.

## Key Points

- Always call `Dispose()` or use `using` — `FluxBurstInstance` holds `NativeArray` resources.
- JIT is unavailable inside Burst jobs. `FluxBurstEvaluator` is a pure interpreter compiled by Burst to near-JIT performance.
- `Set(string)` resolves variable names on the managed heap. In hot paths, prefer `SetIndex` by slot index.
- `NativeBytecodeCache` shares bytecode across instances with reference counting — create one cache, pass to all instances.
