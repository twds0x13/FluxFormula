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
