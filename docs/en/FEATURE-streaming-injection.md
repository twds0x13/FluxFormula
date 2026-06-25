# Feature: Lexer Layer + Streaming Injection Optimization

> Status: A completed (v1.1.0–v1.3.0), B partially completed | Started: 2026-06-18 | Code examples updated to v3.0.0 API

## Feature Breakdown

| # | Feature | Description | Status |
|---|------|------|:--:|
| A | **Lexer** | String → `FluxToken[]`, customizable symbol mapping | ✅ Delivered |
| B | **Streaming Injection** | Unified interpreter/JIT injection path, reduce unnecessary copies | ⚠️ Partial |

---

# A. Lexer

## Pipeline

```
"(1 + 2) * 3.f"
     │
     ▼
  Lexer.Parse("(1 + 2) * 3.f")
     │  1. Tokenize
     │  2. Symbol table → byte opcode
     │  3. Parse literal → TData
     ▼
  FluxToken<float>[]
     │
     ▼
  FluxAssembler.Compile()
     │
     ▼
  Instruction[]
```

## Input / Output Example

```
Input: "(1 * 2) + (3 / 4)"
Output: [
  { Oper: (byte)MathOp.LParen },
  { Oper: (byte)MathOp.Const, Data: 1 },
  { Oper: (byte)MathOp.Mul },
  { Oper: (byte)MathOp.Const, Data: 2 },
  { Oper: (byte)MathOp.RParen },
  { Oper: (byte)MathOp.Add },
  { Oper: (byte)MathOp.LParen },
  { Oper: (byte)MathOp.Const, Data: 3 },
  { Oper: (byte)MathOp.Div },
  { Oper: (byte)MathOp.Const, Data: 4 },
  { Oper: (byte)MathOp.RParen },
]
```

## Customizable Dimensions

| Dimension | Example | Difficulty |
|------|------|:--:|
| **Operator symbols** | `+`→Add, `*`→Mul, `&&`→And, `**`→Pow | Low |
| **Bracket types** | `()`, `[]`, `{}` mapped to different Pairs | Low |
| **Number formats** | `42`, `3.14f`, `0xFF`, `1e-4` | Medium |
| **Identifiers/variables** | `PI`→3.14159, `x`→register index | Medium |
| **Comments** | `//` line, `/* */` block | Medium |
| **Function calls** | `sin(x)` → OpPair EmitOnMatch | High |
| **Custom syntax** | XML-like `<add><c>1</c><c>2</c></add>` | High |

## Implementation

**Final choice: Hand-written span scanner.** Zero dependencies, `ReadOnlySpan<char>` character-by-character scan. Zero regex, zero parser combinator dependency.

Users fill in a `LexerConfig` table. The lexer internally sorts operators by descending length for greedy matching (`**` attempted before `*`).

## Usage

```csharp
var lexer = new FluxLexer<float, MathDef>(new LexerConfig<float, MathDef>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralParser = s => float.Parse(s.TrimEnd('f')),

    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets =
    {
        new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen),
    },
});

var tokens = lexer.Lex("(1.5f + 2f) * 3f");
var runner = new FluxAssembler<float, MathDef>(def);
float r = runner.Build(tokens.Tokens, jit: true).Run(); // 10.5
```

---

# B. Streaming Injection Optimization

(Content preserved; to continue after A completes)

## Current Pain Points

1. Interpreter path: `buffer.ToArray()` copies entire formula buffer
2. Each `Set()` returns a new `FluxInjector` copy
3. Two scans in `CreateInjector()`
4. Interpreter and JIT injection paths not unified

---

## Checklist

- [x] A. Lexer approach confirmed (hand-written span)
- [x] A. Interface design finalized
- [x] A. Implementation + unit tests
- [ ] B. Injection path unified
- [ ] Docs update (guide/lexer.md)
