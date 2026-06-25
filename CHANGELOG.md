## [3.0.3](https://github.com/twds0x13/FluxFormula/compare/v3.0.2...v3.0.3) (2026-06-25)


### Bug Fixes

* replace all remaining sequential GUIDs with random GUIDs across fluxformula, addressables, and core packages ([b9d7f94](https://github.com/twds0x13/FluxFormula/commit/b9d7f94a86b88c8f9c3fa455e270eaf0a0723611))

## [3.0.2](https://github.com/twds0x13/FluxFormula/compare/v3.0.1...v3.0.2) (2026-06-25)


### Bug Fixes

* replace hand-crafted sequential GUIDs with valid random GUIDs, add missing .meta for README/LICENSE ([afbd4e4](https://github.com/twds0x13/FluxFormula/commit/afbd4e479e5e5ce9bf86d89cac8b6af22e9ccb0a))

## [3.0.1](https://github.com/twds0x13/FluxFormula/compare/v3.0.0...v3.0.1) (2026-06-25)

### Bug Fixes

* add 17 missing .meta files across all four packages, resolving empty file imports in UPM ([3c1cb15](https://github.com/twds0x13/FluxFormula/commit/3c1cb15dd43c734e39d130366c2227216c35f516))

# [3.0.0](https://github.com/twds0x13/FluxFormula/compare/v2.1.0...v3.0.0) (2026-06-24)


* refactor!: remove TOper generic parameter, replace with TDef in FluxFormula signature ([2a3cfab](https://github.com/twds0x13/FluxFormula/commit/2a3cfabd3d2b553d6da35debd7af7a8837702d07))
* refactor!: split FluxFormula/FluxModifier types, make FluxType internal ([766e41d](https://github.com/twds0x13/FluxFormula/commit/766e41d1b6da6ddd779401dc32148e496d795fa2))


### Bug Fixes

* add (FluxType) cast in FromBytes, add .ToModifier() in benchmarks Connect ([8c2bebb](https://github.com/twds0x13/FluxFormula/commit/8c2bebbc43f93ab3ff38cf72d0d998ceae94f76f))
* add FluxFormula.Editor to InternalsVisibleTo for FluxFormula.Type access ([0ffbbab](https://github.com/twds0x13/FluxFormula/commit/0ffbbab9eef7ce9675cbf6a8d74dd2b99fb72539))
* add FluxModifier.cs to benchmarks and tests csproj file lists ([bdf7252](https://github.com/twds0x13/FluxFormula/commit/bdf7252727ba5fda04e74529983042d7e4f0592c))
* remove all residual TOper references across the entire repo ([c2fc2c7](https://github.com/twds0x13/FluxFormula/commit/c2fc2c7cc2890766c3defe865371a258bb517851))


### Features

* complete v3.0.0 security cleanup + pipeline docs + coverage to 97.9% ([f5c4bdb](https://github.com/twds0x13/FluxFormula/commit/f5c4bdbe6540e2335cde48df084c74c49a181318))


### BREAKING CHANGES

* TOper generic parameter removed from all core types.

- IFluxJITDefinition<TData, TOper> → IFluxJITDefinition<TData> (all TOper params → byte)
- FluxAssembler<TData, TOper, TDef> → FluxAssembler<TData, TDef> (3 params → 2)
- FluxFormula<TData, TOper> → FluxFormula<TData, TDef>
- FluxInstance<TData, TOper, TDef> → FluxInstance<TData, TDef>
- OpPair<TOper> → OpPair (TOper fields → byte)
- And all other types: FluxToken, FluxLexer, LexerConfig, LexResult, FluxCompiler,
  FluxEvaluator, FluxJITCompiler, VffResolveResult, OperatorRule, BracketRule

Compile-time safety: FluxFormula<TData, TDef> prevents cross-definition
Connect at the type level. Definition is now a complete, self-contained plugin.

* Formula/Modifier type split — FluxModifier<TData, TDef>
introduced as independent public struct. FluxType enum is now internal.

- New FluxModifier<TData, TDef> struct — no Instantiate()/Run(),
  only Connect(FluxModifier) and ToFormula(string)
- FluxFormula.Connect signature: Connect(FluxFormula) → Connect(FluxModifier)
  — type system guarantees RHS is Modifier, eliminates runtime check
- Rename ToMultiplier() → ToModifier(), returns FluxModifier
  (old name retained as [Obsolete])
- FluxType enum: public → internal; FluxFormula.Type → internal;
  ChainLink.Type → internal
- FormulaHeader.Type, VffLinkEntry.Type: FluxType → byte
  for serialization-facing public APIs
- FluxInstance.Run(): InvalidOperationException throw → Debug.Assert

v3.0.0 — 4 runtime exceptions eliminated, all converted to compile errors.

# [2.1.0](https://github.com/twds0x13/FluxFormula/compare/v2.0.0...v2.1.0) (2026-06-24)


### Features

* add VFF encoder, BinaryBuilder interface, and make ChainLink public ([fe210c9](https://github.com/twds0x13/FluxFormula/commit/fe210c9be5b736e3b949d9985a88dfe9da966c40))

# [2.0.0](https://github.com/twds0x13/FluxFormula/compare/v1.5.0...v2.0.0) (2026-06-23)


### Bug Fixes

* resolve 7 compilation errors, add VFF/FormulaCache API docs, translate FEATURE-streaming-injection ([bae97a8](https://github.com/twds0x13/FluxFormula/commit/bae97a88fa16b6e528279d1bc6f169262e854772))
* rework remaining Unity test failures ([1b4aaca](https://github.com/twds0x13/FluxFormula/commit/1b4aacad2fbbf7ece4cab79f1517a7c91be7b8cc))
* update Unity-side tests for Connect Modifier-only guard ([5bcd75f](https://github.com/twds0x13/FluxFormula/commit/5bcd75f023580f4c48543cfb13d9c0f2379f1ada))


### Features

* add Addressables/UniTask samples, extend FluxConfig with file path settings ([791fb2b](https://github.com/twds0x13/FluxFormula/commit/791fb2bcbee488b255b167cf7bdf2132f0d8ac33))
* blob pipeline, format centralization, register semantics, global config ([9cecc9d](https://github.com/twds0x13/FluxFormula/commit/9cecc9d693349e5bda064d01ee06244a17d62334))
* enforce Connect() only accepts Modifier as second argument ([5b8ed3c](https://github.com/twds0x13/FluxFormula/commit/5b8ed3c3e2861eec97d5c18fa2907c710eb5dd70))
* extend benchmark auto-sync to README files, use flat table format [skip test] ([2f27ad0](https://github.com/twds0x13/FluxFormula/commit/2f27ad066629506f78e380b5ddbcc268a3f34a36))
* FluxFormula.Addressables.UniTask package ([9b78b97](https://github.com/twds0x13/FluxFormula/commit/9b78b97661eb2d6d57a01aae92e8863fb7f2ddac))
* multi-arity operators (Select/Lerp/Sum6), ternary ?:, coverage 96.8->97.3% ([8ec1912](https://github.com/twds0x13/FluxFormula/commit/8ec1912d816d7d8cf43143170a664e59194f95c1))
* support recursive VFF resolution with cycle detection ([5bd4c49](https://github.com/twds0x13/FluxFormula/commit/5bd4c49098afc8131ffbec06e0f7f909f4f3296e))


### BREAKING CHANGES

* table, behavioral notes, and version compatibility.
All four package.json bumped to 2.0.0 with internal dependency
versions aligned.

# [1.5.0](https://github.com/twds0x13/FluxFormula/compare/v1.4.0...v1.5.0) (2026-06-20)


### Features

* per-link JIT chain evaluation ([1a5df32](https://github.com/twds0x13/FluxFormula/commit/1a5df32e4c032f7305f2d26b0b2f797083c30606))

# [1.4.0](https://github.com/twds0x13/FluxFormula/compare/v1.3.2...v1.4.0) (2026-06-20)


### Bug Fixes

* quote benchmark filter to prevent shell glob expansion ([9cf5585](https://github.com/twds0x13/FluxFormula/commit/9cf55857fa56aa929890ff0cf70a3f8ebda4311d))
* replace Unsafe.SizeOf<T> with sizeof in unsafe blocks ([f8dfd32](https://github.com/twds0x13/FluxFormula/commit/f8dfd3296fb6b4c6c293f8debf9dce0b47d61571))
* update .releaserc to monorepo package paths ([4776dec](https://github.com/twds0x13/FluxFormula/commit/4776dece9ae611daf94014a565045758903e1cf9))
* update CI and all paths from old com.twds0x13.fluxformula/ to monorepo packages/ ([d646bd7](https://github.com/twds0x13/FluxFormula/commit/d646bd776894609708393a22abc29b99b5823917))


### Features

* compile-cache pipeline — DualHash, FormulaCache, ChainLink, delegate caching ([5541a3f](https://github.com/twds0x13/FluxFormula/commit/5541a3ffe2cad66dc1f4a816e9a6fe52dee3838e))

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.2] - 2026-06-18

### Fixed

- Invalidate Unity Library cache when UPM package sources change

## [1.3.1] - 2026-06-18

### Fixed

- Same-name variables now share injected value across all occurrences
- Replace `Dictionary` with inline binary search for variable lookup, reducing GC allocation

## [1.3.0] - 2026-06-18

### Added

- Named variable injection with configurable lexer patterns
- `FluxPlatform` class for JIT capability detection and auto-degradation
- `Connect()` empty formula guard

## [1.2.0] - 2026-06-18

### Added

- `TokenContext` disambiguation: `ResolveToken(TOper, TokenContext)` allows same-symbol operators to resolve to different semantics based on parser context (e.g. `-` → unary negate vs binary subtract)
- Implicit operator insertion in `FluxLexer`: automatically inserts multiplication between juxtaposed tokens (e.g. `2[atk]` → `2*[atk]`)

### Fixed

- Replace C# 12 collection expressions with `new[]` syntax for Unity 2021.3 compatibility

## [1.1.0] - 2026-06-18

### Added

- `FluxLexer<TData, TOper>`: config-driven lexical analyzer. Hand-written `ReadOnlySpan<char>` scanner, zero regex, zero allocation. Configurable operators, brackets, variable patterns, and literal parsers

## [1.0.1] - 2026-06-18

### Fixed

- `Connect()` empty formula guard: prevent `new Instruction[负数]` crash when connecting empty formulas
- JIT AOT fallback: `FluxPlatform.DisableJit()` auto-called on `Expression.Compile()` failure, subsequent instantiation skips JIT silently
- `TOper` `sizeof` validation in `FluxFormula` static constructor: throws `TypeInitializationException` with descriptive error message if underlying type is not `byte`

## [1.0.0] - 2026-04-16

### Added

- Initial release: high-performance, zero-GC linear formula pipeline for Unity
- Custom instruction set via `IFluxJITDefinition<TData, TOper>` interface
- Shunting-yard compiler producing 8-byte compact bytecode
- Interpreter backend: `stackalloc` registers + `fixed` pointer loop
- JIT backend: LINQ Expression Tree → compiled delegate
- `FluxType.Formula` / `FluxType.Modifier` type system with `Connect()` composition
- Unity Editor window (`FluxAssetEditor`) for visual formula creation and testing
- `ScriptableObject` asset container (`FluxAsset`) with `ToBytes()` / `FromBytes()` serialization

[1.3.2]: https://github.com/twds0x13/FluxFormula/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/twds0x13/FluxFormula/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/twds0x13/FluxFormula/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/twds0x13/FluxFormula/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/twds0x13/FluxFormula/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/twds0x13/FluxFormula/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/twds0x13/FluxFormula/releases/tag/v1.0.0
