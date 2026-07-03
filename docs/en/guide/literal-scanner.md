# Custom Literal Scanner

`LiteralScanner` is a required field on `LexerConfig<TData>`: a zero-allocation Span scanner delegate that controls how the lexer recognizes numbers, keywords, and other literals.

## Signature

```csharp
public delegate int LiteralScanner<TData>(
    ReadOnlySpan<char> src,  // Full source text
    int pos,                 // Current scan position
    out TData value          // Parsed value on match
);
```

- **Returns `pos`**: no match. The lexer continues to try variables, operators, brackets
- **Returns `> pos`**: matched. Characters from `pos` to the return value are consumed
- **`out TData value`**: set to the parsed value on match; `default` on no match

## Default Scanner

For simple number formats (integers, floats), use `CreateDefaultNumberScanner` instead of writing a scanner by hand:

```csharp
config.LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
    s => float.Parse(s.TrimEnd('f', 'F')));
```

Its behavior is equivalent to character-by-character matching of `\d+(\.\d+)?[fF]?`:

1. Check if the current position is a digit
2. Scan integer part
3. Optional: `.` + fractional part
4. Optional: `f` or `F` suffix
5. Call the provided parser function to convert to `TData`

`CreateDefaultNumberScanner` internally calls `ToString()` on the matched Span before invoking the parser, producing a one-time allocation at compile time. For zero string-allocation scenarios, write a custom scanner that parses the Span directly.

## Examples

### Hex Integer

Match `0xFF`-style hexadecimal literals:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 2 >= src.Length) return pos;
    if (src[pos] != '0' || (src[pos + 1] != 'x' && src[pos + 1] != 'X'))
        return pos;

    int end = pos + 2;
    while (end < src.Length && IsHexDigit(src[end])) end++;
    if (end == pos + 2) return pos; // No digits after 0x

    value = ParseHex(src.Slice(pos + 2, end - pos - 2));
    return end;
};

static bool IsHexDigit(char c) =>
    char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
```

Key point: returning `pos` on `0x` prefix mismatch lets the lexer fall through and treat `0` as an ordinary decimal digit.

### Keyword Literals

Match `true` / `false` as literal tokens mapping to `1` / `0`:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 4 <= src.Length && src.Slice(pos, 4).SequenceEqual("true"))
    {
        value = 1;
        return pos + 4;
    }
    if (pos + 5 <= src.Length && src.Slice(pos, 5).SequenceEqual("false"))
    {
        value = 0;
        return pos + 5;
    }
    return pos;
};
```

`Span.SequenceEqual` is the zero-allocation approach to prefix matching.

### Do Nothing

A scanner that always returns `pos` causes the lexer to fall through to other matching phases:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out float v) =>
{
    v = 0;
    return pos; // Never matches literals; lexer continues to try operators etc.
};
```

## Notes

- Scanners should advance character by character, not use regex. Regex introduces heap allocations that negate Span's zero-allocation advantage
- `ToString()` / `float.Parse` calls produce one-time allocations at compile time; they never enter the execution hot path
- Custom scanners are fully compatible with `VariablePatterns`: the lexer first tries the scanner, then falls through to variable patterns on no match
- The `TData : unmanaged` constraint excludes reference types like `string`. Encode extra metadata using `enum` or `byte` fields
- `LiteralScanner` must be set; otherwise the constructor throws an `ArgumentException`
