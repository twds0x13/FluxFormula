# Feature: Lexer Layer + Streaming Injection Optimization

> Status: A completed (v1.1.0–v1.3.0), B partially completed | Started: 2026-06-18

## Feature Breakdown

| # | Feature | Description | Status |
|---|------|------|:--:|
| A | **Lexer** | String → `FluxToken[]`, configurable symbol mapping rules | ✅ Delivered |
| B | **Streaming Injection Optimization** | Unify interpreter/JIT injection paths, reduce unnecessary copies | ⚠️ Partial |

---

# A. Lexer

## Positioning

```
"(1 + 2) * 3.f"
     │
     ▼
  Lexer.Parse("(1 + 2) * 3.f")
     │  1. Tokenize
     │  2. Look up symbol table → TOper
     │  3. Parse literals → TData
     ▼
  FluxToken<float, FloatOp>[]
     │
     ▼
  FluxAssembler.Compile()  ← existing, unchanged
     │
     ▼
  Instruction[]
```

## Input / Output Example

```
Input: "(1 * 2) + (3 / 4)"
Output: [
  { Oper: LParen },
  { Oper: Const, Data: 1 },
  { Oper: Mul },
  { Oper: Const, Data: 2 },
  { Oper: RParen },
  { Oper: Add },
  { Oper: LParen },
  { Oper: Const, Data: 3 },
  { Oper: Div },
  { Oper: Const, Data: 4 },
  { Oper: RParen },
]
```

## Customizable Dimensions

| Dimension | Example | Difficulty |
|------|------|:--:|
| **Operator symbols** | `+`→Add, `*`→Mul, `&&`→And, `**`→Pow | Low |
| **Bracket types** | `()`, `[]`, `{}` mapped to different Pairs | Low |
| **Number formats** | `42`, `3.14f`, `0xFF`, `1e-4` | Medium |
| **Identifiers/variables** | `PI`→3.14159, `x`→register index | Medium |
| **Comments** | `//` line comments, `/* */` block comments | Medium |
| **Function calls** | `sin(x)` → OpPair EmitOnMatch | High (grammar support needed) |
| **Custom syntax formats** | XML-like `<add><c>1</c><c>2</c></add>` | High (structural parsing needed) |

## Technical Options

### Option 1: Hand-written Recursive Descent Tokenizer (Recommended Starting Point)

- **Zero dependencies**, fully controllable
- Reference: .NET `StringTokenizer` or Unity `Lexer` pattern
- Suitable for: mathematical expressions, C#-like formats
- Limitation: complex nested formats (XML) require additional handling

### Option 2: Regex Tokenization + Config Table

```csharp
var lexer = new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
{
    TokenPatterns = new[]
    {
        new(@"\d+\.?\d*f?",         FloatOp.Const,  TokenType.Immediate),
        new(@"\+",                  FloatOp.Add,    TokenType.Operator),
        new(@"\-",                  FloatOp.Sub,    TokenType.Operator),
        new(@"\*",                  FloatOp.Mul,    TokenType.Operator),
        new(@"/",                   FloatOp.Div,    TokenType.Operator),
        new(@"\(",                  FloatOp.LParen, TokenType.Operator),
        new(@"\)",                  FloatOp.RParen, TokenType.Operator),
    },
    LiteralParser = s => float.Parse(s.TrimEnd('f')),
    IgnorePattern = @"\s+",
});
```

### Option 3: Sprache / Pidgin (C# Parser Combinator)

- NuGet packages, Unity-compatible (pure C#, no native dependencies)
- **Sprache**: object-oriented style, suitable for rapid prototyping
- **Pidgin**: value-type style, higher performance ceiling
- Suitable for: nested structures (function calls, conditional expressions)

### Comparison

| | Hand-written Tokenizer | Regex + Config Table | Sprache/Pidgin |
|------|:--:|:--:|:--:|
| Dependencies | Zero | Zero | NuGet package |
| Math expressions | ✅ | ✅ | ✅ |
| Custom operators | Code changes | Config table | Combinators |
| XML/nested formats | ❌ Difficult | ❌ Difficult | ✅ |
| Function calls (sin(x)) | Medium | Medium | ✅ |
| Performance | Best | Good | Good |
| Learning curve | Low | Low | Medium |

### Actual Implementation

**Final choice: Hand-written Span scanner (Option 1)**. Zero dependencies, `ReadOnlySpan<char>` character-by-character scanning. Zero Regex, zero Parser Combinator dependency.

```
┌────────────────────────────────────────┐
│  FluxLexer (Recommended approach)       │
│  User fills in LexerConfig config table │
│  Internal hand-written ReadOnlySpan     │
│  character scanner                      │
│  Zero dependencies, zero Regex          │
└────────────────────────────────────────┘
```

Users provide a `LexerConfig` and are ready to go. The lexer internally sorts operators by descending length, attempting longer symbols first (`**` before `*`).

### Architecture Interface Draft

```csharp
/// <summary>
/// Lexical analyzer: converts strings to FluxToken streams.
/// </summary>
public class FluxLexer<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
{
    /// <summary>Build from config table (recommended, zero Pidgin knowledge)</summary>
    public FluxLexer(LexerConfig<TData, TOper> config);

    /// <summary>Bridge from a user-written Pidgin parser (advanced mode)</summary>
    public static FluxLexer<TData, TOper> FromPidgin(
        Parser<char, FluxToken<TData, TOper>> customParser,
        Func<string, TData> literalParser);

    /// <summary>Parse string into token array</summary>
    public FluxToken<TData, TOper>[] Lex(string source);
}

/// <summary>
/// Pure config-driven lexical rule table.
/// Users need no Pidgin knowledge — fill in the table and go.
/// </summary>
public class LexerConfig<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
{
    /// <summary>Whitespace/comment patterns (skipped)</summary>
    public string WhitespacePattern = @"\s+";

    /// <summary>Literal → TData conversion function</summary>
    public Func<string, TData> LiteralParser;

    /// <summary>Operator rule list</summary>
    public List<OperatorRule<TOper>> Operators = new();

    /// <summary>Bracket rule list</summary>
    public List<BracketRule<TOper>> Brackets = new();

    /// <summary>Number/identifier regex pattern</summary>
    public string LiteralPattern = @"\d+(\.\d+)?f?";
}

/// <summary>Operator mapping rule</summary>
public record OperatorRule<TOper>(
    string Symbol,    // e.g. "+", "*", "&&", "**"
    TOper Oper        // Associated enum value
);

/// <summary>Bracket mapping rule</summary>
public record BracketRule<TOper>(
    string Open,      // e.g. "(", "[", "{"
    string Close,     // e.g. ")", "]", "}"
    TOper LeftOper,   // Left bracket enum
    TOper RightOper   // Right bracket enum
);
```

## Usage Examples

### Layer 1: Fill in the Table (Recommended)

```csharp
var lexer = new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
{
    LiteralPattern = @"\d+(\.\d+)?f?",
    LiteralParser  = s => float.Parse(s.TrimEnd('f')),

    Operators =
    {
        new("+", FloatOp.Add),
        new("-", FloatOp.Sub),
        new("*", FloatOp.Mul),
        new("/", FloatOp.Div),
    },
    Brackets =
    {
        new("(", ")", FloatOp.LParen, FloatOp.RParen),
    },
});

var tokens = lexer.Lex("(1.5f + 2f) * 3f");
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);
float r = runner.Build(tokens, jit: true).Run(); // 10.5
```

### Layer 2: Hand-written Pidgin Parser (Advanced)

```csharp
// User-written Pidgin parser
var myParser = OneOf(
    DecimalNum.Select(v => Token(MyOp.Const, v)),
    Char('+').ThenReturn(Token(MyOp.Add, 0f)),
    // ...
);

// Bridge via FromPidgin, enjoying the same downstream pipeline as FluxLexer
var lexer = FluxLexer<float, FloatOp>.FromPidgin(myParser, s => float.Parse(s));
var tokens = lexer.Lex("1 + 2");
```

---

# B. Streaming Injection Optimization

(Original content retained, to be revisited after A is complete)

## Current Pain Points

1. Interpreter path: `buffer.ToArray()` copies the entire formula buffer
2. Every `Set()` returns a new `FluxInjector` copy (`readonly struct`)
3. Double scan in `CreateInjector()`
4. Interpreter and JIT injection paths are not unified

## Pending Approaches

(To be continued after your feedback on Part A)

---

## Implementation Checklist

- [ ] A. Lexer approach confirmed (Option 2 or Option 3)
- [ ] A. Interface design finalized
- [ ] A. Implementation + unit tests
- [ ] B. Injection path unification
- [ ] Documentation update (guide/lexer.md)
