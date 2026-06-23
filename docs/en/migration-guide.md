# Migration Guide

This document tracks breaking changes between major FluxFormula versions and the steps required to migrate.

The current latest version is 2.0.0.

---

## Migrating from 1.5 to 1.7

No breaking changes.

### Additions

- **Compile-cache pipeline**: `DualHash64` + `FormulaCache`. Compilation and JIT delegates are automatically cached, zero user intervention.
- **Blob build pipeline**: `FluxBlobBuilder` scans all `FluxAsset` → concatenates blob → generates C# offset table. Triggers automatically before Play Build.
- **VFF virtual formulas**: `VffFormat` provides persistent formula references + parameter overrides, DLL-style symbol resolution.
- **FluxConfig global configuration**: `FluxConfig` replaces hardcoded constants. Unity integration via `FluxConfigAsset` ScriptableObject injection.
- **MaxRegister on-demand allocation**: Formula header stores compile-time max register; runtime allocates only what's needed.
- **Per-link JIT chain evaluation**: JIT path no longer forces ToAtomic — each link gets its own delegate, chained via `SetIndex(0, prevResult)`.
- **FluxFormula.Addressables.UniTask**: UniTask-based async loading extension package.

---

## Migrating from 1.x to 2.0

### Overview

2.0 moves the internal chain representation off the public API surface, unifies the external interface to a single form, and tightens `Connect` call constraints.

Scope: code directly referencing `ChainLink`, `IsChained`, or `ChainLength` requires adjustment. Code using `Connect` to chain formulas must pass a Modifier as the right-hand argument.

### Breaking Changes

| Change | 1.x Behavior | 2.0 Behavior | Migration |
|--------|-------------|-------------|-----------|
| `IsChained` | `public bool` | `internal` | Remove external references. Consumers no longer see dual atomic/chain modes |
| `ChainLength` | `public int` | `internal` | Same as above |
| `GetChainLinks()` | `public` | `internal` | Same as above |
| `ChainLink` struct | Publicly referenceable | `internal struct` | Same as above |
| `Connect(next)` | Accepts any `FluxFormula` | Requires `next` to be a Modifier | Call `.ToMultiplier()` on the right-hand formula before passing it |

### Behavioral Changes (Non-Signature)

- `Raw()` / `ToBytes()`: chained formulas are automatically merged into atomic form before returning, no longer returning empty or corrupt data.
- `Connect` semantics clarified: the left formula's R1 output flows into the right Modifier's Bus(R1) register. Passing a non-Modifier no longer silently overwrites the first operand; an explicit exception is thrown instead.

### Version Compatibility

| FluxFormula | Unity |
|-------------|-------|
| 2.0 | 2021.3+ |

### Additions

- `Connect` Modifier syntax is safer: `formula.Connect(modifier)` clearly expresses "left output injects into right" intent
- All chain-tracking APIs internalized; public API surface reduced by 6 methods, lowering cognitive overhead

---

## Migrating from 1.2 to 1.3

No breaking changes.

## Migrating from 1.1 to 1.2

No breaking changes.
