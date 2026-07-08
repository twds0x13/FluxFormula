# Migration Guide

This document tracks breaking changes between major FluxFormula versions and the steps required to migrate.

The current latest version is 5.1.x.

---

## Migrating from 3.0 to 3.2

### Overview

3.2 extracts chained formula representation from `FluxFormula`/`FluxModifier` into an independent `FluxChain<TData, TDef>` type. `FluxFormula` is always atomic, `FluxChain` always chained — the two forms are distinguished by the type system rather than a runtime boolean field.

### Breaking Changes

| Change | Description | Migration |
|--------|-------------|-----------|
| `Connect()` returns `FluxChain` | `FluxFormula.Connect()` and `FluxModifier.Connect()` now return `FluxChain<TData, TDef>` instead of `FluxFormula`/`FluxModifier` | Change receiving variable type from `var`/`FluxFormula` to `FluxChain`, or explicitly call `.ToAtomic()` |
| `IsChained` removed | `FluxFormula` and `FluxModifier` no longer have `IsChained` | Remove all `if (formula.IsChained)` branches — no longer needed |
| `ChainLength` → `FluxChain.Length` | Chain length property moved to `FluxChain` | `chain.ChainLength` → `chain.Length` |
| `GetChainLinks()` → `FluxChain.GetLinks()` | Chain structure access moved to `FluxChain` | `formula.GetChainLinks()` → `chain.GetLinks()` |
| `ToAtomic()` moved to `FluxChain` | `FluxFormula.ToAtomic()` removed; `FluxChain.ToAtomic()` returns `FluxFormula` | `chain.ToAtomic()` for explicit one-time merge |
| `VffResolveResult.Formula` → `.Chain` | VFF resolve result field renamed | `result.Formula` → `result.Chain` (`FluxChain` can be passed directly to `Instantiate`) |

### Additions

- `FluxChain<TData, TDef>` — standalone chain formula type, `Instantiate(FluxChain)` for per-link evaluation
- `FluxChain.GetLinks()` — zero-copy span access to chain structure
- `FluxChain.Connect(FluxModifier)` — chain append returning new `FluxChain`
- `FluxAssembler.Instantiate(FluxChain, bool)` — chain formula instantiation overload

### Eliminated Hidden Allocations

`FluxFormula.Raw()` and `FluxFormula.ToBytes()` no longer trigger hidden `ToAtomic()` allocations for chained formulas — atomic formulas are always O(1).

---

## Migrating from 2.x to 3.0

### Overview

3.0 removes the `TOper` generic parameter from all core types and splits `FluxFormula`/`FluxModifier` into two independent structs. Four runtime exceptions are eliminated and converted to compile errors.

### Breaking Changes

| Change | 2.x Behavior | 3.0 Behavior | Migration |
|--------|-------------|-------------|-----------|
| `TOper` removed | `FluxAssembler<TData, TOper, TDef>` (3 params) | `FluxAssembler<TData, TDef>` (2 params) | Delete all `TOper` parameters. `IFluxExprDefinition<TData, TOper>` → `IFluxExprDefinition<TData>`. Operator enums become `byte` with internal casts |
| `FluxModifier` split | Single `FluxFormula` type with `FluxType` enum | Two types: `FluxFormula` + `FluxModifier` | `Connect(FluxFormula)` → `Connect(FluxModifier)`. Call `.ToModifier()` first |
| `ToMultiplier()` renamed | `ToMultiplier()` returns `FluxFormula` | `ToModifier()` returns `FluxModifier` | Rename calls. Old name retained as `[Obsolete]` |
| `FluxType` internalized | `FluxType` enum was `public` | `FluxType` is `internal` | Remove direct references to `formula.Type` |

### Eliminated Runtime Exceptions

| Original Exception | Eliminated By |
|--------------------|---------------|
| `Connect requires Modifier` | Signature only accepts `FluxModifier` |
| `Modifier cannot run standalone` | `FluxModifier` has no `Instantiate()` |
| Cross-definition Connect unchecked | `FluxFormula<TData, TDef>` type-level distinction |
| `sizeof(TOper) != 1` | TOper gone; `byte` always 1 byte |

### Version Compatibility

| FluxFormula | Unity |
|-------------|-------|
| 3.x | 2021.3+ |

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
| `Connect(next)` | Accepts any `FluxFormula` | Requires `next` to be a Modifier | Call `.ToMultiplier()` on the right-hand formula before passing it |
| `ChainLink` / `IsChained` / `ChainLength` / `GetChainLinks()` | `public` (1.x) → `internal` (early 2.0) | `public` (final 2.0) | Advanced users can access chain structure via `GetChainLinks()` and persist as VFF via `VffFormat.ToBytes()` |

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
