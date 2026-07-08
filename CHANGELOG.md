## [5.1.1](https://github.com/twds0x13/FluxFormula/compare/v5.1.0...v5.1.1) (2026-07-08)

### Bug Fixes

* resolve LiteralScanner build errors and source generator warnings ([fe9d1da](https://github.com/twds0x13/FluxFormula/commit/fe9d1da4d0b9489b2346002edc444c2b2bb15a9c))

## [5.1.0](https://github.com/twds0x13/FluxFormula/compare/v5.0.0...v5.1.0) (2026-07-08)

### Features

* **core:** add literal template source generator for zero-alloc span scanning ([6f54aaa](https://github.com/twds0x13/FluxFormula/commit/6f54aaaeb3d0bc1670b372cac7114afc24c2de39))

## [5.0.0](https://github.com/twds0x13/FluxFormula/compare/v4.0.0...v5.0.0) (2026-07-03)

### ⚠ BREAKING CHANGES

* **core:** LexerConfig.LiteralParser and LiteralPattern are removed.
Use CreateDefaultNumberScanner(parser) to set LiteralScanner instead.

### Code Refactoring

* **core:** remove LiteralParser and LiteralPattern, LiteralScanner is now required ([6cd5891](https://github.com/twds0x13/FluxFormula/commit/6cd5891a3e194cf9542c6ba25077e45cc053efe4))

## [4.0.0](https://github.com/twds0x13/FluxFormula/compare/v3.7.0...v4.0.0) (2026-07-03)

### ⚠ BREAKING CHANGES

* **core:** all IFluxDefinition implementations must accept Span<TData>.

### Features

* **core:** change Compute to Span<TData>, enable R0 interrupt from definitions ([6266288](https://github.com/twds0x13/FluxFormula/commit/62662883cb681869e87ff88b1c21f3184d1cfc25))

## [3.7.0](https://github.com/twds0x13/FluxFormula/compare/v3.6.1...v3.7.0) (2026-06-29)

### Features

* **samples:** add FloatMath sample to fluxformula package ([297bafd](https://github.com/twds0x13/FluxFormula/commit/297bafd1bc198d0b22d84858aa4337e943443d15))

### Bug Fixes

* **ci:** flatten dotnet-coverage run to single line ([9206e69](https://github.com/twds0x13/FluxFormula/commit/9206e691e22455d9bc036f74942ce27e14cc591a))

## [3.6.1](https://github.com/twds0x13/FluxFormula/compare/v3.6.0...v3.6.1) (2026-06-29)

### Bug Fixes

* add missing LowLevel.Unsafe using, avoid ref struct in lambda ([5ccd7eb](https://github.com/twds0x13/FluxFormula/commit/5ccd7ebee1f0ff2ed544a2a9391d0229f61b2638))
* assign struct fields in constructors, add missing asmdef reference ([1c7f2ac](https://github.com/twds0x13/FluxFormula/commit/1c7f2ac7785b9528b99232343b962589f2357703))
* **burst:** propagate NaN to R0 error register on division-by-zero ([822e79c](https://github.com/twds0x13/FluxFormula/commit/822e79cd1cfa9d7fea0c4453e04b00ee02e5d429))
* **core:** remove stray com.unity.collections dependency from package.json ([250ef39](https://github.com/twds0x13/FluxFormula/commit/250ef39e5965c4d399d944d32a06a1add834ed7a))
* implement ScheduleBurst, add 45 tests across plugin packages ([dde38bf](https://github.com/twds0x13/FluxFormula/commit/dde38bf7e47853d2c9708429632f44b8a4cf31a8))
* ref struct cannot be class field, add Unity.Addressables ref to asmdef ([22063ac](https://github.com/twds0x13/FluxFormula/commit/22063ac8d986396d8ad9bac3c2d81410751277db))

## [3.6.0](https://github.com/twds0x13/FluxFormula/compare/v3.5.0...v3.6.0) (2026-06-27)

### Features

* unify caches to 256 slots + graceful overflow for NativeBytecodeCache* **burst:** extract INativeBytecodeCache interface, unify to 256 slots, graceful overflow* **il:** add FluxILCompiler: DynamicMethod + ILGenerator JIT path
### Bug Fixes

* IL stloc type mismatch on Mono verifier, fix NativeBytecodeCache Dispose test for struct copy semantics* use non-generic CreateDelegate(Type) for Unity CI, fix benchmarks CompiledFunc refs* Unity CI test compatibility: void* cast, missing isCached, Capacity public, Throws syntax* lower burst collections dependency to 1.2.4 + squash fix commits* **il:** revert GetInterfaceMap, keep constrained.callvirt

  GetInterfaceMap in ref struct static initializer crashes on Linux CoreCLR .NET 9; the original constrained.callvirt approach is correct.* **il:** exclude DynamicMethod sites from code coverage instrumentation

  dotnet-coverage IL instrumentation cannot handle DynamicMethod sites statically.* **il:** resolve Compute via GetInterfaceMap, use direct call instead of constrained.callvirt

  Mono JIT has a bug with constrained.callvirt + default interface method in DynamicMethod context.
### Code Refactoring

* rename FluxJITCompiler→FluxExprCompiler, IFluxJITDefinition→IFluxExprDefinition
### Documentation

* clarify FluxChain evaluation: prefer Instantiate overload, not ToAtomic roundtrip* remove AI-generated CHANGELOG noise, restore to semantic-release format* add IL inline operators example with verified sample project [skip test]* add IL compiler pipeline page, update platform/jit/overview for three-tier JIT

# Changelog

## Breaking Changes by Version

> **升级前必读。** 每个大版本的破坏性变更、迁移成本和推荐操作。

### v3.2.x — FluxChain 独立类型

| 变更 | 影响 | 迁移 |
|------|------|------|
| `FluxChain<TData,TDef>` 独立类型 | `FluxFormula.Connect()` 现在返回 `FluxChain` 而非 `FluxFormula`。`IsChained`/`ChainLength`/`GetChainLinks()` 从 `FluxFormula`/`FluxModifier` 移除 | `var chain = formula.Connect(modifier)` → 类型变为 `FluxChain`。`chain.GetChainLinks()` → `chain.GetLinks()`。`chain.ChainLength` → `chain.Length`。`chain.ToAtomic()` 显式转为 `FluxFormula` |

### v2.x → v3.0.0

| 变更 | 影响 | 迁移 |
|------|------|------|
| `TOper` 泛型参数移除 | 所有类型签名减少一个泛型参数。`FluxAssembler<TData, TOper, TDef>` → `FluxAssembler<TData, TDef>`（三参数→两参数） | 删除所有 `TOper` 参数。`IFluxExprDefinition<TData, TOper>` → `IFluxExprDefinition<TData>`。操作符枚举改为 `byte`，定义体内部强转。旧枚举可保留为 `const byte` 容器 |
| `FluxModifier<TData,TDef>` 独立 struct | Formula/Modifier 分属两个类型。`FluxType` 枚举变为 `internal` | `Connect(FluxFormula)` → `Connect(FluxModifier)`，需先调 `.ToModifier()`。`ToMultiplier()` → `ToModifier()`（旧名保留 `[Obsolete]`）。Modifier 无 `Instantiate()` 方法 |

### v1.x → v2.0.0

| 变更 | 影响 | 迁移 |
|------|------|------|
| 四包拆分（Core / Unity / Addressables / UniTask） | 原单包分为四层。Core 零 UnityEngine 依赖 | 直接引用 `FluxFormula.Core` 命名空间即可。Unity 端额外引用 `FluxFormula` 包 |
| Blob 管线 + FormulaCache 替代 ConnectCache | ConnectCache 删除，FormulaCache.Instance 为唯一缓存入口 | 无需改动。序列化路径自动走 blob |
| `IFluxFileFormatter` 替代 `IFluxBinaryBuilder` | 接口重命名，`FluxArtifactKind` 枚举区分 .ff/.vff | 将 `IFluxBinaryBuilder` 引用改为 `IFluxFileFormatter` |

---

# [3.4.0](https://github.com/twds0x13/FluxFormula/compare/v3.3.0...v3.4.0) (2026-06-26)

### Features

* add type fingerprint to .ff format — prevents cross-definition bytecode injection ([593f102](https://github.com/twds0x13/FluxFormula/commit/593f10252995ad506c868c3ea76386ce7565e4a7))

### Bug Fixes

* quote dotnet test in coverage CI to prevent MSBuild split arg error ([9fb1058](https://github.com/twds0x13/FluxFormula/commit/9fb1058b02b5a702acdf9c872a7de9cc33b07115))

# [3.3.0](https://github.com/twds0x13/FluxFormula/compare/v3.2.0...v3.3.0) (2026-06-26)

### Features

* add fluxformula.burst package — Burst/Jobs interpreter for Unity ([9bce25e](https://github.com/twds0x13/FluxFormula/commit/9bce25ee47fc5cf1973049ad94c8386c5668e0f2))
* FormulaCache thread-safe read — ReaderWriterLockSlim with Interlocked stats ([c323594](https://github.com/twds0x13/FluxFormula/commit/c323594c17ce091a83f33d4feb0b59ca81b2b3df))
* split FluxChain from FluxFormula — eliminate dual-representation and hidden ToAtomic allocations ([b4c8409](https://github.com/twds0x13/FluxFormula/commit/b4c840982d1a9c26e7024b7350d9d6df488b2df4))

### Bug Fixes

* Unity test residual — EvalFormula(FluxChain) overload, FluxChain field access via ToAtomic ([b964dc8](https://github.com/twds0x13/FluxFormula/commit/b964dc8279edd7ac108090523c05c447e01fdeb5))
* add FluxChain.cs to benchmark csproj, fix Unity test files for FluxChain API ([3402905](https://github.com/twds0x13/FluxFormula/commit/3402905c79ede2af5b4d8632e454cdaa384cfd85))

# [3.2.0](https://github.com/twds0x13/FluxFormula/compare/v3.1.1...v3.2.0) (2026-06-25)

### Features

* LiteralScanner delegate replaces hardcoded literal scanning ([ec7e4f3](https://github.com/twds0x13/FluxFormula/commit/ec7e4f39d01333dab821e80f337260e753f7ad39))

## [3.1.1](https://github.com/twds0x13/FluxFormula/compare/v3.1.0...v3.1.1) (2026-06-25)

### Bug Fixes

* JIT path bytecode caching + Expression.Equal compatibility for custom TData ([8abba8a](https://github.com/twds0x13/FluxFormula/commit/8abba8a1c03a0450ae0e4c4b7bf14735181dc37a))

# [3.1.0](https://github.com/twds0x13/FluxFormula/compare/v3.0.3...v3.1.0) (2026-06-25)

### Features

* add FluxCompression — Brotli-based blob compression layer ([9d2162b](https://github.com/twds0x13/FluxFormula/commit/9d2162be9e2057d71ca7145d35ac6755e91a6b4b))

## [3.0.3](https://github.com/twds0x13/FluxFormula/compare/v3.0.2...v3.0.3) (2026-06-25)

### Bug Fixes

* replace all remaining sequential GUIDs with random GUIDs across fluxformula, addressables, and core packages ([ea3837c](https://github.com/twds0x13/FluxFormula/commit/ea3837cd9d1d48580e43847e9250c83d5924e484))

## [3.0.2](https://github.com/twds0x13/FluxFormula/compare/v3.0.1...v3.0.2) (2026-06-25)

### Bug Fixes

* replace hand-crafted sequential GUIDs with valid random GUIDs, add missing .meta for README/LICENSE ([cb028ff](https://github.com/twds0x13/FluxFormula/commit/cb028ff3cb30159ddfe4a72531b01b8f798590d5))

## [3.0.1](https://github.com/twds0x13/FluxFormula/compare/v3.0.0...v3.0.1) (2026-06-25)

### Bug Fixes

* add 17 missing .meta files across all four packages, resolving empty file imports in UPM ([2ca63e8](https://github.com/twds0x13/FluxFormula/commit/2ca63e8c2e3cdcae2dc14b4a3d62fa0f786651a8))

# [3.0.0](https://github.com/twds0x13/FluxFormula/compare/v2.1.0...v3.0.0) (2026-06-24)

### Refactors

* **BREAKING:** remove TOper generic parameter, replace with TDef in FluxFormula signature ([1192ed3](https://github.com/twds0x13/FluxFormula/commit/1192ed30f0a204226decd26b2ebdd42452bda93e))
* **BREAKING:** split FluxFormula/FluxModifier types, make FluxType internal ([96d1925](https://github.com/twds0x13/FluxFormula/commit/96d192543c603ef0a6b841205351baacbdba11fb))
* rename IFluxBinaryBuilder → IFluxFileFormatter, add Load method, split overview diagram ([9aa9cad](https://github.com/twds0x13/FluxFormula/commit/9aa9cadc0c1e0346bd1793a52927a4363fd1790e))

### Bug Fixes

* remove all residual TOper references across the entire repo ([fc63225](https://github.com/twds0x13/FluxFormula/commit/fc632252de09dc50ec0eaf6f208d90f8d4d3b86a))
* add (FluxType) cast in FromBytes, add .ToModifier() in benchmarks Connect ([0f08172](https://github.com/twds0x13/FluxFormula/commit/0f0817261a7e0e1748b77f7830af99e85933242f))
* add FluxFormula.Editor to InternalsVisibleTo for FluxFormula.Type access ([23efc83](https://github.com/twds0x13/FluxFormula/commit/23efc8382273c2d8a95f4aaa248e8573ccff5826))
* add FluxModifier.cs to benchmarks and tests csproj file lists ([0d5d53c](https://github.com/twds0x13/FluxFormula/commit/0d5d53c8c8dcb94f9c9308e6b482435954d62a9f))

### Features

* complete v3.0.0 security cleanup + pipeline docs + coverage to 97.9% ([c966839](https://github.com/twds0x13/FluxFormula/commit/c9668390e0a81923fca10afed01d5d398b48b94a))

### BREAKING CHANGES

**TOper 泛型参数移除。** 所有类型签名减少一个泛型参数：

* `IFluxExprDefinition<TData, TOper>` → `IFluxExprDefinition<TData>`（所有 TOper 参数改为 `byte`）
* `FluxAssembler<TData, TOper, TDef>` → `FluxAssembler<TData, TDef>`（三参数→两参数）
* `FluxFormula<TData, TOper>` → `FluxFormula<TData, TDef>`
* `FluxInstance<TData, TOper, TDef>` → `FluxInstance<TData, TDef>`
* 其他受影响类型：FluxToken、FluxLexer、LexerConfig、LexResult、FluxCompiler、FluxEvaluator、FluxExprCompiler、VffResolveResult、OperatorRule、BracketRule

`FluxFormula<TData, TDef>` 在类型层面阻止跨 Definition Connect。Definition 现在是完整、自包含的插件。

**Formula/Modifier 类型分裂。** `FluxModifier<TData, TDef>` 作为独立公开 struct 引入。`FluxType` 枚举变为 `internal`。

* 新 `FluxModifier<TData, TDef>` struct——无 `Instantiate()`/`Run()`，仅 `Connect(FluxModifier)` 和 `ToFormula(string)`
* `FluxFormula.Connect` 签名：`Connect(FluxFormula)` → `Connect(FluxModifier)`——类型系统保证 RHS 为 Modifier，消除运行时检查
* `ToMultiplier()` → `ToModifier()`，返回 `FluxModifier`（旧名保留 `[Obsolete]`）
* 序列化层公开 API（`FormulaHeader.Type`、`VffLinkEntry.Type`）：`FluxType` → `byte`

v3.0.0——4 个运行时异常全部转为编译错误。

# [2.1.0](https://github.com/twds0x13/FluxFormula/compare/v2.0.0...v2.1.0) (2026-06-24)

### Features

* add VFF encoder, BinaryBuilder interface, and make ChainLink public ([a1dfc26](https://github.com/twds0x13/FluxFormula/commit/a1dfc264b5ea5ab1fc1a79f17d472019d5501372))

# [2.0.0](https://github.com/twds0x13/FluxFormula/compare/5a90667...v2.0.0) (2026-06-23)

### Features

* **compile-cache pipeline** — DualHash, FormulaCache, ChainLink, delegate caching ([04e3af5](https://github.com/twds0x13/FluxFormula/commit/04e3af5ddd5bd5e4694e92019b0a1680396f7adc))
* **blob pipeline** — format centralization, register semantics, global config ([c7ab900](https://github.com/twds0x13/FluxFormula/commit/c7ab900d6d8ba81ab08951077aa5ce406690ca7a))
* FluxFormula.Addressables.UniTask package ([fa78bd1](https://github.com/twds0x13/FluxFormula/commit/fa78bd1c0e8aecac88f2d88053a1ab3f929fbe71))
* multi-arity operators (Select/Lerp/Sum6), ternary ?:, coverage 96.8→97.3% ([be1c84d](https://github.com/twds0x13/FluxFormula/commit/be1c84d9601fa400eb892ab84d2732dcca140cda))
* support recursive VFF resolution with cycle detection ([ea40ae3](https://github.com/twds0x13/FluxFormula/commit/ea40ae33e4c75f47e78adcd0a02c54559514955f))
* enforce Connect() only accepts Modifier as second argument ([793c69a](https://github.com/twds0x13/FluxFormula/commit/793c69a65ce47e0a8135b140bc311955980fa1f6))
* per-link JIT chain evaluation ([179968b](https://github.com/twds0x13/FluxFormula/commit/179968b4aaa27108f13a3ce9464caf9699c302a8))
* add Addressables/UniTask samples, extend FluxConfig with file path settings ([c7d726a](https://github.com/twds0x13/FluxFormula/commit/c7d726a530e6ee4453353d595a3021254c849570))

### Bug Fixes

* resolve 7 compilation errors, add VFF/FormulaCache API docs ([e57883b](https://github.com/twds0x13/FluxFormula/commit/e57883b6e25255ffd4791d4a20da3603af56ec16))
* rework remaining Unity test failures ([52d15d9](https://github.com/twds0x13/FluxFormula/commit/52d15d9bdfcea27ad062478ca3a4b7a932e473cc))
* update Unity-side tests for Connect Modifier-only guard ([905f407](https://github.com/twds0x13/FluxFormula/commit/905f407d3a6c4917d07b3320514796ee3adc5094))

---

## 1.x — Pre-Monorepo

> v1.0.0（2026-04-16）至 v1.5.0（2026-06-20）。以下为关键里程碑摘要，完整历史已 squash 为仓库的 `Initial commit`。

| Version | Date | Highlights |
|---------|------|------------|
| **1.0.0** | 2026-04-16 | Initial release: `IFluxExprDefinition`, shunting-yard compiler, interpreter + JIT backends, `FluxAsset` serialization |
| **1.0.1** | 2026-06-18 | Connect empty guard, JIT AOT fallback, TOper sizeof validation |
| **1.1.0** | 2026-06-18 | `FluxLexer` — config-driven scanner, zero allocation |
| **1.2.0** | 2026-06-18 | `TokenContext` disambiguation, implicit operator insertion, C# 12→new[] compat |
| **1.3.0** | 2026-06-18 | Named variable injection, `FluxPlatform` JIT detection |
| **1.3.1** | 2026-06-18 | Same-name variable sharing, Dictionary→binary search |
| **1.3.2** | 2026-06-18 | Unity Library cache invalidation on UPM source changes |
| **1.4.0** | 2026-06-20 | Compile-cache pipeline (DualHash, FormulaCache, ChainLink) |
| **1.5.0** | 2026-06-20 | Per-link JIT chain evaluation |
