<p align="center">
  <img src="logo.png" alt="FluxFormula" width="160" />
</p>

# FluxFormula

[中文](./README.md)

[![CI](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml/badge.svg)](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-green)](https://twds0x13.github.io/FluxFormula/)
[![Coverage](https://img.shields.io/badge/coverage-96.9%25-brightgreen)](./docs/technical/test-coverage-boundary.md)

A high-performance linear formula compilation pipeline for Unity (zero-GC at runtime, one-time allocations at compile time). Define custom operator sets, compile infix expressions to compact bytecode, execute via interpreter or JIT backend

## Features

- **Zero GC at Runtime**: `ref struct`, `stackalloc`, and unsafe pointer operations eliminate all heap allocations at runtime. A single `Instruction[]` allocation plus literal string parsing at compile time, pure stack thereafter
- **Dual Backend**: Interpreter for full platform compatibility (including IL2CPP/AOT), JIT via LINQ Expression Tree compilation to delegate, with automatic fallback on platforms that do not support runtime code generation
- **Custom Instruction Set**: Implement the `IFluxExprDefinition<TData>` interface to define domain-specific operators. A single implementation yields both interpreter and JIT execution paths
- **Compact Bytecode**: `Instruction` is an 8-byte fixed-size struct with explicit memory layout. 256 virtual registers, maximum arity 6, immediate operands inlined into the instruction buffer
- **Hand-Written Lexer**: A `ReadOnlySpan<char>` based zero-allocation scanner with no regex dependency. Configurable operators, brackets, variable patterns, and implicit operators

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

## Compatibility

CI runs the full test suite on:

| Environment | Tested Versions |
|-------------|----------------|
| Unity | 2021.3 LTS · 2022.3 LTS · 6000.0 |
| .NET SDK | 8.0 · 9.0 |

The Core package (`fluxformula.core`) targets netstandard2.1, compatible with .NET Core 3.0+ and all modern .NET runtimes. Unity supports both Mono and IL2CPP scripting backends.

Minimum Unity version: 2021.3

## Quick Start

```csharp
using FluxFormula.Core;
using System;
using System.Globalization;
using System.Linq.Expressions;

// 1. Define the operator enum (underlying type must be : byte)
public enum MathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
}

// 2. Implement IFluxExprDefinition<float>
public readonly struct MathDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)MathOp.Return;

    public int GetArity(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 2, MathOp.Sub => 2, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((MathOp)op) switch
    {
        MathOp.Const  => OpType.Immediate,
        MathOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 1, MathOp.Sub => 1,
        MathOp.Mul => 2, MathOp.Div => 2,
        MathOp.Neg => 3,
        _          => 0,
    };

    public OpPair GetPair(byte op) => ((MathOp)op) switch
    {
        MathOp.LParen => new OpPair { PairRole = Pair.Left },
        MathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)MathOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
    {
        MathOp.Neg => Associativity.Right,
        _          => Associativity.Left,
    };

    public OperandPosition GetFirstPosition(byte op) => (MathOp)op switch
    {
        MathOp.Add => OperandPosition.Left,
        MathOp.Sub => OperandPosition.Left,
        MathOp.Mul => OperandPosition.Left,
        MathOp.Div => OperandPosition.Left,
        _          => OperandPosition.Right,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((MathOp)op) switch
        {
            MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            MathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            MathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            MathOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return ((MathOp)op) switch
        {
            MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero),
                nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
}

// 3. Configure Lexer, compile a formula, inject variables, execute
var config = new LexerConfig<float>
{
    LiteralOper    = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators      = { new("+", (byte)MathOp.Add), new("-", (byte)MathOp.Sub),
                       new("*", (byte)MathOp.Mul), new("/", (byte)MathOp.Div) },
    Brackets       = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
    ImplicitOperators = { (byte)MathOp.Mul },
};

var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);
var lexResult = new FluxLexer<float>(config).Lex("([atk] * 2 + [bonus]) / 100");

float result = runner.Instantiate(runner.Compile(lexResult))
    .Set("atk", 150f)
    .Set("bonus", 25f)
    .Run();
// result = 3.25
```

Separate compilation from execution (compile once, reuse):

```csharp
var formula = runner.Compile(lexResult);        // compile (cacheable)
var inst    = runner.Instantiate(formula);       // instantiate (lightweight, repeatable)
float r     = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

## Documentation

Full API reference, advanced configuration, and usage guides: <https://twds0x13.github.io/FluxFormula/>

## License

MIT License © 2026 twds0x13
