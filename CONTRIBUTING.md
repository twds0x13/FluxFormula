# Contributing to FluxFormula

FluxFormula is a solo-maintained project. Contributions are welcome, but please understand that review and response times may vary. This guide covers the basics of reporting issues and submitting code.

## Before You Start

- Check the [documentation](https://twds0x13.github.io/FluxFormula/) and [FAQ](https://twds0x13.github.io/FluxFormula/faq) — most questions about how things work are answered there.
- Search [existing issues](https://github.com/twds0x13/FluxFormula/issues) to avoid duplicates.
- For questions and discussion, use [GitHub Discussions](https://github.com/twds0x13/FluxFormula/discussions).

## Reporting Bugs

Use the [Bug Report](https://github.com/twds0x13/FluxFormula/issues/new?template=bug_report.yml) template. The most important thing is a **minimal reproduction** — a small, self-contained C# code snippet that triggers the issue. Reports without one are hard to act on.

## Suggesting Features

Use the [Feature Request](https://github.com/twds0x13/FluxFormula/issues/new?template=feature_request.yml) template. Focus on describing the problem and use case — that helps more than a detailed API proposal.

## Development Setup

```bash
git clone https://github.com/twds0x13/FluxFormula.git
cd FluxFormula
dotnet restore standalone-tests/FluxFormula.Tests.csproj
dotnet build standalone-tests/FluxFormula.Tests.csproj
```

Prerequisites:
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download) (for standalone tests)
- [Unity 2021.3+](https://unity.com/) (for in-editor testing; not required for running unit tests)

## Project Overview

```
FluxFormula/
├── com.twds0x13.fluxformula/        # UPM package
│   ├── Runtime/                     # FluxFormula.Core assembly
│   │   ├── FluxAssembler.cs         # Compilation entry point
│   │   ├── FluxCompiler.cs          # Shunting-yard algorithm (internal)
│   │   ├── FluxEvaluator.cs         # Interpreter execution (internal)
│   │   ├── FluxJITCompiler.cs       # LINQ Expression Tree compiler (internal)
│   │   ├── FluxFormula.cs           # Immutable bytecode container
│   │   ├── FluxInstance.cs          # ref struct streaming executor
│   │   ├── FluxLexer.cs             # Hand-written span lexer
│   │   ├── FluxToken.cs             # Lexical token
│   │   ├── Instruction.cs           # 8-byte instruction struct
│   │   └── ...
│   ├── Editor/                      # Unity Editor extensions
│   └── Tests/                       # In-package tests
├── standalone-tests/                # Dotnet test project (no Unity needed)
└── docs/                            # VitePress documentation (zh-CN + en)
```

### Core Constraints

When contributing code, keep these in mind:

- **Zero GC on the hot path**: `FluxEvaluator`, `FluxInstance.Set`/`Run`, and `FluxInjector` use `stackalloc` and `fixed` pointers — no `new`, no LINQ, no boxing.
- **ref struct**: `FluxAssembler` and `FluxInstance` are ref structs. They cannot be boxed, captured in lambdas, or stored as class fields. This is intentional.
- **unmanaged generics**: `TData`, `TOper`, `TDef` all satisfy the `unmanaged` constraint. Do not introduce managed-type parameters on public hot-path types.
- **Enum `: byte`**: The framework reads the opcode via `*(byte*)&oper`. Operator enums must use `: byte` as the underlying type.

## Testing

The standalone test suite runs without Unity:

```bash
dotnet test standalone-tests/FluxFormula.Tests.csproj
```

For a single test:

```bash
dotnet test standalone-tests/FluxFormula.Tests.csproj --filter "FullyQualifiedName~SmokeTest"
```

Tests cover compilation, interpreter, JIT, lexer, Connect, and serialization paths. If you fix a bug or add a feature, adding a test is strongly appreciated but not a hard requirement — mention it in the PR if a test is impractical.

## Pull Requests

- Branch from `main`, target `main`.
- Keep changes focused. One concern per PR.
- Match the surrounding code style.
- For changes touching `FluxEvaluator`, `FluxCompiler`, or `FluxJITCompiler`, a note about performance impact in the description is helpful.

I review PRs as time permits. If a PR has been sitting without response for more than a week, feel free to ping the issue or discussion thread.

## Documentation

The docs site is built with [VitePress](https://vitepress.dev/) from the `docs/` directory. Both Chinese (`zh-CN`, default) and English (`en`) locales are maintained. When changing documented behavior, updating both locales is appreciated but not mandatory.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
