# FluxLexer / LexerConfig

When you need to turn a string expression into a token stream, `FluxLexer` is the entry point to the entire pipeline. It does not rely on dictionary lookups or heap-allocated collections: all rules are pre-indexed at construction time, and the `Lex()` hot path performs only handwritten span traversal.

## Signature

```csharp
public class FluxLexer<TData>
    where TData : unmanaged

public class LexerConfig<TData>
    where TData : unmanaged

public readonly struct LexResult<TData>
    where TData : unmanaged
```

**Associated rule types:**

```csharp
public struct VariablePatternRule          // Variable pattern: prefix + suffix
public readonly struct BracketRule         // Bracket pair: open/close symbols + opcodes
public readonly struct OperatorRule        // Operator syntax view: symbol + opcode + Slots/Aux
```

## LexerConfig Properties

| Property | Type | Description |
|----------|------|-------------|
| `LiteralOper` | `byte` | Opcode for literal values (e.g. `(byte)MathOp.Const`) |
| `LiteralScanner` | `LiteralScanner<TData>` | Literal scanner delegate. Auto-generated when `[LiteralTemplate]` is present (optional); otherwise required |
| `Operators` | `List<OperatorRule>` | Operator mapping list (auto-sorted by length descending; no manual sorting needed) |
| `Brackets` | `List<BracketRule>` | Bracket mapping list |
| `ImplicitOperators` | `List<byte>` | Operators eligible for implicit insertion (e.g. `(byte)MathOp.Mul`). With a single entry, `2(3)` and `(a)(b)` auto-insert the operator; multiple ambiguous entries throw |
| `VariablePatterns` | `List<VariablePatternRule>` | Variable pattern list, e.g. `new("[", "]")` matches `[x]` |
| `WhitespacePattern` | `string` | (Deprecated, unused) Current implementation uses `char.IsWhiteSpace` per-character skip, not replaceable. Default `@"\s+"` |

### Auxiliary Types

**OperatorRule:**
| Field | Type | Description |
|-------|------|-------------|
| `Symbol` | `string` | Infix symbol (e.g. `"+"`, `"cross"`, `"?"`) |
| `Oper` | `byte` | Backend opcode |
| `Slots` | `sbyte[]` | Operand position offset array (infix = 0). null means use IFluxDefinition defaults |
| `Aux` | `AuxRule[]` | Auxiliary symbol constraints (brackets/separators). null means none |
| `BracketOpen` / `BracketClose` | `string` | Function-call bracket symbols (e.g. `"("`, `")"`). null means no bracket syntax |

**BracketRule:**
| Field | Type | Description |
|-------|------|-------------|
| `Open` / `Close` | `string` | Opening/closing bracket symbols |
| `LeftOper` / `RightOper` | `byte` | Opcodes for left/right bracket tokens |

**VariablePatternRule:**
| Field | Type | Description |
|-------|------|-------------|
| `Prefix` / `Suffix` | `string` | Variable delimiters (e.g. `"[", "]"` or `"{var:", "}"`) |

## Methods

### FluxLexer Constructor

```csharp
public FluxLexer(LexerConfig<TData> config)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `config` | `LexerConfig<TData>` | Lexical rule configuration. Must not be null |

Pre-indexes all operators (descending by length), brackets, and variable patterns at construction time. Subsequent `Lex()` calls are allocation-free.

### Lex

```csharp
public LexResult<TData> Lex(string source)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `source` | `string` | Source string. Empty string returns empty result |

Returns `LexResult<TData>` containing `Tokens` (`FluxToken<TData>[]`) and `VarNames` (`string[]`, null for non-variable positions).

### CreateDefaultNumberScanner (static)

```csharp
public static LiteralScanner<TData> CreateDefaultNumberScanner(
    Func<string, TData> parser)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `parser` | `Func<string, TData>` | String-to-TData conversion function |

Returns a scanner delegate matching the `\d+(\.\d+)?[fF]?` format. Equivalent to manually setting `LexerConfig.LiteralScanner`.

## Usage

#### Basic Four-Function Arithmetic

```csharp
var config = new LexerConfig<float>
{
    LiteralOper    = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer = new FluxLexer<float>(config);
var result = lexer.Lex("1 + 2 * 3");
// result.Tokens → [Const(1), Add, Const(2), Mul, Const(3)]
```

#### Implicit Multiplication

```csharp
config.ImplicitOperators.Add((byte)MathOp.Mul);
// "2(3)" is auto-parsed as "2 * (3)"
// "(a)(b)" auto-inserts the multiplication operator
```

#### Custom Variable Syntax

```csharp
config.VariablePatterns.Add(new VariablePatternRule("{var:", "}"));
// Matches "{var:damage}" → variable name "damage"
```

## See Also

- [FluxToken](./flux-token) — Lexical token struct
- [FluxAssembler](./flux-assembler) — Compilation entry point that consumes LexResult
- [IFluxDefinition](./idefinition) — Operator behavior definition
