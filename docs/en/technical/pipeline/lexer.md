# Lexer

`FluxLexer<TData>` is a hand-written, zero-allocation scanner. Its core design question: **how do you efficiently convert an infix expression string into a token sequence without using regular expressions?**

## Why Not Regex?

Regex in .NET produces heap allocations (`Match` objects, `Group` collections). For FluxFormula's target (compile-time allocation only, zero-GC execution), regex allocation is acceptable since compilation itself allocates the token array. However, a hand-written scanner offers three advantages regex cannot provide:

1. **Precise error locations**: the hand-written loop knows the current character position and can produce error messages with line and column numbers.
2. **Zero intermediate strings**: `ReadOnlySpan<char>` slices are passed directly to `LiteralScanner`; `ToString()` only when calling the parser inside `CreateDefaultNumberScanner`.
3. **Context-aware disambiguation**: operator resolution (e.g., `-` as unary negation in `OperandExpected` position) happens during scanning, eliminating an extra compiler pass.

## Core Data Structures

```csharp
public readonly struct LexResult<TData> where TData : unmanaged
{
    public readonly FluxToken<TData>[] Tokens;  // Token sequence
    public readonly string[] VarNames;          // Variable names (in order of appearance)
}
```

`FluxToken<TData>` itself is 16 bytes (`byte Oper` + padding + `TData Data`):

```csharp
public readonly struct FluxToken<TData> where TData : unmanaged
{
    public readonly byte Oper;   // Operator bytecode (defined by Definition)
    public readonly TData Data;  // Literal value (valid only for Immediate type)
}
```

Key design: tokens do not store strings. Operators are identified by `byte` (converted by the Definition's `ResolveToken`), and literal values are parsed to `TData` during scanning. The original string is no longer needed after scanning completes.

## Scan Loop

The overall structure of the `Lex()` method:

```
while (pos < input.Length):
    if (char.IsWhiteSpace)  → skip
    if (char.IsDigit/dot)   → TryScanLiteral → FluxToken(oper=LiteralOper, data=parsedValue)
    if (operator starts)    → TryScanOperator → FluxToken(oper=operatorByte)
    if (bracket starts)     → try match bracket pair
    if (variable starts)    → TryScanVariable → FluxToken(oper=LiteralOper, data=default)
```

The core loop allocates zero heap memory. The only allocations occur after the loop exits: the `FluxToken[]` array and `string[] VarNames`.

## Operator Scanning: Longest Match

`TryScanOperator` does not use simple prefix matching. It uses a **longest-match strategy**. When the input is `select(a, b, c)`, the scanner first tries to match `select` rather than `s` or `se`:

1. Iterate through all registered operator `TokenText` values and find all prefix matches
2. Choose the longest match (`select` > `s`)
3. If the longest match is a function operator (with parentheses), automatically enter function parameter mode

This avoids ambiguity when users define both `select` and `s` as operators.

## Bracket Matching

Brackets are configured via `BracketRule`:

```csharp
public readonly struct BracketRule
{
    public readonly string Left;    // "("
    public readonly string Right;   // ")"
    public readonly byte LeftOper;  // LParen opcode
    public readonly byte RightOper; // RParen opcode
}
```

The scanner does not track nesting depth. It only converts `(` and `)` into `LParen` and `RParen` opcode tokens. Nesting correctness is verified by the compiler (shunting-yard algorithm) when processing the bracket stack.

## Variable Patterns

Variables are configured via `VariablePatternRule` (e.g., `["[", "]"]` or `["{var:", "}"]`):

```csharp
public readonly struct VariablePatternRule
{
    public readonly string Prefix;  // "["
    public readonly string Suffix;  // "]"
}
```

During scanning, when a prefix start character is encountered, the scanner reads until the suffix ends and extracts the variable name between them. Variables are mapped to `LiteralOper`-type tokens (`Data = default`), and the variable name is added to the `VarNames` list. Variable-to-immediate-slot mapping happens during compilation.

## Literal Scanning

Literals are scanned via the `LiteralScanner` delegate:

```csharp
public LiteralScanner<TData> LiteralScanner;
```

`LiteralScanner` receives a `ReadOnlySpan<char>` and the current position, returning the consumed position and the parsed value. Use `CreateDefaultNumberScanner` for standard number formats. The `ToString()` allocation is the only literal-related heap allocation during compilation (~392B for simple, ~1080B for complex). See [Literal Scanner](../../guide/literal-scanner.md).

## Implicit Multiplication

`ImplicitOperators` configures which operators can be auto-inserted when no explicit symbol is present:

```
Config: ImplicitOperators = { Mul }
Input: "2(3)"  → scanner auto-inserts Mul token between "2" and "("
Input: "(a)(b)" → scanner auto-inserts Mul token between ")("
```

Insertion condition: when a literal/right-paren is followed by a left-paren/variable-prefix, and the implicit operator is registered.

## Source Generator Integration (v5.0+)

v5.0 introduces a source-generator-driven literal template system that moves scanner generation from runtime to compile time.

### Compile-Time Pipeline

`LiteralScannerGenerator` (IIncrementalGenerator) runs during compilation. Three incremental pipelines collect three categories of input:

| Pipeline | Attribute | Collects |
|----------|-----------|----------|
| A | `[LiteralTemplate]` | Directly annotated structs |
| B | `[ExternalLiteralTemplate]` | Third-party type template registrations (overrides Pipeline A) |
| C | `[LiteralTypeAlias]` | Type aliases (e.g. `Distance` → `float`) |

After merging Pipelines A and B (B overrides A), each template passes through two stages:

1. **CompactToXml**: compact format `"<float X> <float Y>"` is converted to standard XML `<literal-template><field type="float" name="X"/>...</literal-template>`
2. **XmlTemplateParser**: XML is parsed into an AST (`LiteralTextNode` / `FieldDirectiveNode` / `OptionalBlockNode`)

`BuildDependencyGraph` then analyzes type reference relationships between templates, detects circular dependencies, produces a topological sort, and `CodeEmitter` compiles the AST into C# span scanner source code, injected as `LiteralScanners.g.cs`.

### Runtime Integration

The generated `LiteralScanners` partial class's static constructor writes each generated scanner into `ScannerRegistry<TData>.Scanner`:

```csharp
static LiteralScanners()
{
    ScannerRegistry<Point2D>.Scanner = (s, p, out v) => Scan_Point2D(s, p, out v);
    ScannerRegistry<Entity>.Scanner = (s, p, out v) => Scan_Entity(s, p, out v);
}
```

`FluxLexer<TData>` constructor selects scanners in priority order:

1. `LiteralScanners.TryGetScanner<TData>()` — hits when `[LiteralTemplate]` is present
2. `config.LiteralScanner` — manual delegate fallback
3. Throws `ArgumentException` if neither is available

### Generated Code Pattern

Each generated `Scan_Xxx` method structure:

- Span bounds check → field-by-field scan
- Built-in type fields call `LiteralTemplateRegistry.Scan_Float` etc. (12 built-in scanners)
- Custom struct fields recursively call previously generated `Scan_OtherStruct`
- Optional blocks: save position → attempt match → continue on success, restore on failure
- Character-by-character exact matching for literal text
- All methods annotated with `[MethodImpl(AggressiveInlining)]`

See [Literal Scanner](../../guide/literal-scanner.md) for built-in scanner recognition formats and further details.
