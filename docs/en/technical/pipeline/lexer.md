# Lexer

`FluxLexer<TData>` is a hand-written, zero-allocation scanner. Its core design question: **how do you efficiently convert infix expression strings to token sequences without regex?**

## Why Not Regex?

Regex in .NET produces heap allocations (`Match` objects, `Group` collections). While compile-time allocations are acceptable (token array allocation already occurs), a hand-written scanner provides three advantages regex cannot:

1. **Precise error locations**: The scan loop knows exact character position for column/line error messages.
2. **Zero intermediate strings**: `ReadOnlySpan<char>` slices pass directly to `LiteralParser`; `ToString()` only at the final parse step.
3. **Context-aware disambiguation**: Operator resolution (e.g., `-` as unary negation in `OperandExpected` position) happens during scanning.

## Core Data Structures

```csharp
public readonly struct LexResult<TData> where TData : unmanaged
{
    public readonly FluxToken<TData>[] Tokens;
    public readonly string[] VarNames;
}
```

`FluxToken<TData>` is 16 bytes: `byte Oper` + padding + `TData Data`. Key design: tokens do not store strings. Operators are identified by `byte` (via Definition's `ResolveToken`), literal values are parsed to `TData` during scanning.

## Scan Loop

```
while (pos < input.Length):
    Whitespace → skip
    Digit/dot  → TryScanLiteral → FluxToken(oper, parsedValue)
    Operator   → TryScanOperator (longest match)
    Bracket    → match bracket pair
    Variable   → TryScanVariable → FluxToken(LiteralOper, default)
```

Zero heap allocation during the scan. The only allocations are the final `FluxToken[]` and `string[] VarNames`.

## Longest-Match Operator Scanning

When input is `select(a, b, c)`, the scanner tries `select` before `s` or `se`. All registered operator texts are checked for prefix matches; the longest wins. This avoids ambiguity when both `select` and `s` are registered as operators.

## Variable Patterns

Variables are configured via `VariablePatternRule` (`["[", "]"]`, `["{var:", "}"]`, etc). On prefix match, the scanner extracts the name between prefix and suffix. Variables map to `LiteralOper` tokens; name-to-slot mapping happens during compilation.

## Implicit Multiplication

`ImplicitOperators` allows auto-insertion of operators between adjacent operands: `"2(3)"` → `2 * 3`, `"(a)(b)"` → `a * b`. Insertion triggers when a literal/right-paren is followed by left-paren/variable-prefix.
