# Migration Guide

This document tracks breaking changes between major FluxFormula versions and the steps required to migrate.

The current latest version is 5.9.1.

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

## Migrating from 3.x to 4.0

### Overview

4.0 changes the `IFluxDefinition<TData>.Compute()` signature from `ReadOnlySpan<TData>` to `Span<TData>`, enabling in-place register modification within Compute implementations. `FluxExprCompiler` and `IFluxExprDefinition` are renamed from their previous names `FluxJITCompiler`/`IFluxJITDefinition`.

### Breaking Changes

| Change | Description | Migration |
|--------|-------------|-----------|
| `Compute(byte, Instruction, ReadOnlySpan<TData>)` → `Span<TData>` | Register span is now writable | Most implementations require no changes: `Span<TData>` is fully compatible with index-based reads |
| `FluxJITCompiler` → `FluxExprCompiler` | Class renamed | Replace all type references |
| `IFluxJITDefinition` → `IFluxExprDefinition` | Interface renamed | Replace all interface implementations |

### Additions

- `Span<TData>` register parameter supports in-place modification for scenarios requiring state accumulation within Compute

---

## Migrating from 4.x to 5.1

### Overview

5.0 removes `LiteralParser` and `LiteralPattern`, introducing a source-generator-driven literal template system. The `[Template]` attribute enables the compiler to auto-generate zero-allocation span scanners, making the `LexerConfig.LiteralScanner` delegate optional.

### Breaking Changes

| Change | 4.x Behavior | 5.1 Behavior | Migration |
|--------|-------------|-------------|-----------|
| `LexerConfig.LiteralParser` | Present | Removed | Use `LexerConfig.LiteralScanner` delegate, or add `[Template]` to the TData struct |
| `LexerConfig.LiteralPattern` | Present | Removed | Replaced by `[Template]` template strings |
| `LexerConfig.LiteralScanner` | Required | Optional (auto-generated when `[Template]` is present) | Existing manual delegates continue to work unchanged |

### Additions

- `[Template("<float X> <float Y>")]` — marks a struct with a template; source generator produces scan code at compile time
- `[ExternalTemplate(typeof(T), "...")]` — registers templates for third-party types you cannot modify
- `[TypeAlias("Alias", "float")]` — custom type alias, purely cosmetic
- `SerializerRegistry` — zero-allocation `Scan_Xxx` methods for 12 built-in types (float, double, int, uint, long, ulong, short, ushort, byte, sbyte, bool, char)
- Compiler diagnostics: SSR001 (template syntax error), SSR002 (circular dependency), SSR003 (readonly struct not allowed), SSR004 (reference to unregistered type)
- `CompactToXml` / `XmlTemplateParser` — internal template parsing pipeline
- `CodeEmitter` — AST to C# source emitter

### Runtime Priority

`FluxLexer<TData>` constructor selects scanners in the following order:
1. Generated `SerializerScanners.TryGetScanner<TData>()` (hits when `[Template]` is present)
2. `config.LiteralScanner` manual delegate (fallback)
3. Throws `ArgumentException` if neither is available

---

## Migrating from 5.1 to 5.9

### Overview

5.2 through 5.9 introduced no breaking changes, but added three major systems. Existing code upgrading from 5.1 to 5.9 requires no modifications. The additions are listed below for optional adoption.

### Additions

| Version | Feature | Guide |
|---------|---------|-------|
| 5.2.0 | BlobRegistry system: `IFluxBlobRegistry` interface + source generator + `FluxBlobScanner` multi-mod discovery. Replaces the old C# byte[] embedding; .blob binary files with zero expansion | [Blob Registry](/guide/blob-registry) |
| 5.4.0 | `FluxCurryEvaluator`: progressive curry-style binding, functional State→State forking | [Curry Evaluator](/guide/curry-evaluator) |
| 5.4.0 | `FluxStepEvaluator`: single-step debugger for per-instruction tracing | [Step Debugger](/guide/step-debugger) |
| 5.7.0 | `[Tag]` attribute: enum-tag-based literal scanning | [Literal Scanner](/guide/literal-scanner) |
| 5.9.0 | Out-of-order variable binding in curry evaluator (by name instead of injection order) | [Curry Evaluator](/guide/curry-evaluator) |

### Example

```csharp
// Curry evaluation (5.4.0)
var curry = assembler.Instantiate(formula, curry: true)
    .Bind("atk", 100f)
    .Bind("bonus", 25f);
float result = curry.Result;

// Step debugging (5.4.0)
var step = assembler.Instantiate(formula, step: true);
while (!step.IsCompleted) step = step.Step();
```

---

## Migrating from 5.x to 6.0

### Overview

6.0 replaces the in-house `LiteralScannerGenerator` with the independent [SourceSerializer](https://github.com/twds0x13/SourceSerializer) v1.2.0 library. Attribute namespaces migrate from `FluxFormula.Core` to `SourceSerializer`.

### Breaking Changes

| Old Name | New Name |
|----------|----------|
| `[LiteralTemplate("...")]` | `[Template("...")]` |
| `[LiteralTag("tag")]` | `[Tag("tag")]` |
| `[LiteralTypeAlias("Alias", "float")]` | `[TypeAlias("Alias", "float")]` |
| `[ExternalLiteralTemplate(typeof(T), "...")]` | `[ExternalTemplate(typeof(T), "...")]` |
| `LiteralScanners.TryGetScanner<T>()` | `SerializerScanners.TryGetScanner<T>()` |
| `LiteralTemplateRegistry.Scan_Xxx` | `SerializerRegistry.Scan_Xxx` |
| `FluxFormula.LiteralScanner.Generator` (SG reference) | `SourceSerializer.Generator` |

Diagnostic codes `FLX001`-`FLX004` are replaced by `SSR001`-`SSR004`. The `LexerConfig.LiteralScanner` delegate and `LiteralScanner<TData>` type remain unchanged.

### New Features

| Feature | Description |
|---------|-------------|
| `SerializerEmitters.TryGetEmitter<T>()` | Serialization direction: write struct instance back to `StringBuilder` |
| `string` built-in type | Added `string` as the 13th built-in type |
| Generic collection auto-resolution | `List<T>` / `Dictionary<K,V>` fields auto-synthesize parsers |
| `[TemplateIgnore]` | Mark fields to skip serialization, prevent SSR004 error |
| SSR004 Error | Missing template dependency upgraded from Warning to Error |
| SSR005 Error | New: scalar field inside `<repetition>` detection |

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
