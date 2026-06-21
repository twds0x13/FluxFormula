# Migration Guide

This document tracks breaking changes between major FluxFormula versions and the steps required to migrate.

The current latest version is 1.7.0. There are no known breaking changes at this time. The template below is prepared for future version iterations.

---

## Migrating from 1.5 to 1.7

No breaking changes.

### Additions

- **Compile-cache pipeline**: `DualHash64` + `FormulaCache` + `ConnectCache`. Compilation and JIT delegates are automatically cached, zero user intervention.
- **Blob build pipeline**: `FluxBlobBuilder` scans all `FluxAsset` → concatenates blob → generates C# offset table. Triggers automatically before Play Build.
- **VFF virtual formulas**: `VffFormat` provides persistent formula references + parameter overrides, DLL-style symbol resolution.
- **FluxConfig global configuration**: `FluxConfig` replaces hardcoded constants. Unity integration via `FluxConfigAsset` ScriptableObject injection.
- **MaxRegister on-demand allocation**: Formula header stores compile-time max register; runtime allocates only what's needed.
- **Per-link JIT chain evaluation**: JIT path no longer forces ToAtomic — each link gets its own delegate, chained via `SetIndex(0, prevResult)`.
- **FluxFormula.Addressables.UniTask**: UniTask-based async loading extension package.

---

## Migrating from 1.x to 2.0

> Template section. Fill in when 2.0 is released.

### Overview

Brief summary of the impact scope and core changes.

### Breaking Changes

| Change | 1.x Behavior | 2.0 Behavior | Migration |
|--------|-------------|-------------|-----------|
| Example | | | |

### Deprecations

List deprecated but still functional APIs, with the planned removal version.

### Additions

List new APIs that replace old behavior or fill gaps.

---

## Migrating from 1.2 to 1.3

No breaking changes.

## Migrating from 1.1 to 1.2

No breaking changes.
