<p align="center">
  <img src="logo.png" alt="FluxFormula" width="160" />
</p>

# FluxFormula

[中文](./README.md)

[![CI](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml/badge.svg)](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-green)](https://twds0x13.github.io/FluxFormula/)
[![Coverage](https://img.shields.io/badge/coverage-97.3%25-brightgreen)](./docs/technical/test-coverage-boundary.md)

A high-performance linear formula compilation pipeline for Unity (zero-GC at runtime, one-time allocations at compile time). Define custom operator sets, compile infix expressions to compact bytecode, execute via interpreter or JIT backend

## Features

- **Zero GC at Runtime**: `ref struct`, `stackalloc`, and unsafe pointer operations eliminate all heap allocations at runtime. A single `Instruction[]` allocation plus literal string parsing at compile time, pure stack thereafter
- **Dual Backend**: Interpreter for full platform compatibility (including IL2CPP/AOT), JIT via LINQ Expression Tree compilation to delegate, with automatic fallback on platforms that do not support runtime code generation
- **Custom Instruction Set**: Implement the `IFluxExprDefinition<TData>` interface to define domain-specific operators. A single implementation yields both interpreter and JIT execution paths
- **Compact Bytecode**: `Instruction` is an 8-byte fixed-size struct with explicit memory layout. 256 virtual registers, maximum arity 6, immediate operands inlined into the instruction buffer
- **Hand-Written Lexer**: A `ReadOnlySpan<char>` based zero-allocation scanner with no regex dependency. Configurable operators, brackets, variable patterns, and implicit operators
- **Three-Mode Evaluator**: Hot-path interpreter at full speed, curry evaluator for progressive variable binding with forking support, and step debugger for per-instruction inspection. All three share the same register machine core

## Why FluxFormula

If your game involves heavy formula evaluation — damage formulas, skill modifiers, probability checks — parsing strings on every frame will drag down performance. FluxFormula lets you write formulas in natural infix notation, compile them to bytecode, and evaluate them with zero allocation at runtime. Your operator definition gives you both interpreter and JIT paths for free; a single JIT invocation takes just a few nanoseconds, with automatic fallback on platforms that do not support runtime code generation.

## Performance

BenchmarkDotNet on Intel Core Ultra 9 275HX, .NET 9, ShortRun:

| Stage | Operation | Time | Allocation |
|------|------|------|------|
| Lexer | Simple expression | ~103 ns | 392 B |
| Lexer | Complex expression | ~422 ns | 1080 B |
| Compile | Simple expression | ~34 ns | 112 B |
| Compile | Complex expression | ~119 ns | 496 B |
| Interpreter | Simple eval | ~27 ns | **0 B** |
| Interpreter | Complex eval | ~42 ns | **0 B** |
| JIT | Simple eval | ~2 ns | **0 B** |
| JIT | Complex eval | ~4 ns | **0 B** |

One-time compilation cost: ~30–110 ns + a few hundred bytes. Execution: zero allocation. JIT is ~15× faster than the interpreter.
## Package Structure

This is a monorepo containing five independent packages:

| Package | Purpose | Dependencies |
|---------|---------|-------------|
| `fluxformula.core` | Pure C# formula pipeline (zero Unity dependency) | None |
| `fluxformula` | Unity integration (ScriptableObject container + editor) | Core |
| `fluxformula.burst` | Burst/Jobs evaluator: multi-threaded, zero-allocation concurrent execution | Core + Burst + Collections |
| `fluxformula.addressables` | Optional Addressables loading support | Core + FluxFormula + Addressables |
| `fluxformula.addressables.unitask` | UniTask async loading extensions (install if your project already uses UniTask) | Addressables |

## Compatibility

CI runs the full test suite on:

| Environment | Tested Versions |
|-------------|----------------|
| Unity | 2021.3 LTS · 2022.3 LTS · 6000.0 |
| .NET SDK | 8.0 · 9.0 |

The Core package (`fluxformula.core`) targets netstandard2.1, compatible with .NET Core 3.0+ and all modern .NET runtimes. Unity supports both Mono and IL2CPP scripting backends.

## Installation

**Minimum install (Core runtime only):**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core"
  }
}
```

**Unity users (recommended):**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula"
  }
}
```

**For Addressables loading, add the third package:**

```json
"com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables"
```

**Full install (all packages):**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula",
    "com.twds0x13.fluxformula.burst": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.burst",
    "com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables",
    "com.twds0x13.fluxformula.addressables.unitask": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables.unitask"
  }
}
```

Minimum Unity version: 2021.3

## Quick Start

A Noita-style spell modification system, built with FluxFormula. Each card has a damage modifier and draw count; cards chain together into a single cast:

```csharp
// Three spell cards: +10 / +7 / +5 damage, no extra draws
var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
var mod2  = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();
var mod3  = runner.Compile(lexer.Lex("[prev] + 5|idx:2")).ToModifier();

// Chain them together
var chain = card1.Connect(mod2).Connect(mod3);

// Execute: 7 draws, 3 cards, spell wrapping until all cards are consumed
SpellContext state = new(0, 7);
do {
    state = runner.Instantiate(chain).Set("prev", state).Run();
} while (/* mask not full */);
```

[CardDraw full runnable code](https://twds0x13.github.io/FluxFormula/examples/card-draw) · [More examples](https://twds0x13.github.io/FluxFormula/examples/float-math)

If you just want the minimal API shape:

```csharp
var config = new LexerConfig<float>();          // operator and bracket config
var lexer  = new FluxLexer<float>(config);      // lexer
var def    = new MathDef();                     // operator definition (+ - * /)
var runner = new FluxAssembler<float, MathDef>(def); // compiler + instance factory

var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
float result = runner.Instantiate(runner.Compile(lexResult))
    .Set("atk", 150f).Set("bonus", 25f).Run();
// result = 3.25
```

Separate compilation from execution (compile once, reuse):

```csharp
var formula = runner.Compile(lexResult);        // compile (cacheable)
var inst    = runner.Instantiate(formula);       // instantiate (lightweight, repeatable)
float r     = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

Full walkthrough in the [Getting Started guide](https://twds0x13.github.io/FluxFormula/guide/getting-started).

## Documentation

Full API reference, advanced configuration, and usage guides: <https://twds0x13.github.io/FluxFormula/>

## License

MIT License © 2026 twds0x13
