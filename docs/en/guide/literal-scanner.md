# Literal Scanner

Controls how the lexer recognizes literals such as numbers and keywords. Starting from v5.0, the source-generator-driven `[LiteralTemplate]` is available: declare a format template on your TData struct and the compiler auto-generates a zero-allocation span scanner at compile time.

## `[LiteralTemplate]` Auto-Generation

Add an attribute to your struct. The source generator produces the scanner at compile time. `LexerConfig.LiteralScanner` is no longer required:

```csharp
[LiteralTemplate("<float X> <float Y>")]
public struct Point2D
{
    public float X;
    public float Y;
}

// LiteralScanner does not need to be set
var config = new LexerConfig<Point2D>
{
    LiteralOper = 0,
    Operators = { new("+", 1), new("-", 2) },
};
var lexer = new FluxLexer<Point2D>(config);
var result = lexer.Lex("3.5 -2.1");
// result.Tokens[0].Data → Point2D { X = 3.5, Y = -2.1 }
```

**Runtime priority**: the lexer constructor first checks `LiteralScanners.TryGetScanner<TData>()` (hits when `[LiteralTemplate]` is present), falls back to `config.LiteralScanner` manual delegate, and throws `ArgumentException` if neither is available.

## Template Syntax

### Compact Format

```csharp
[LiteralTemplate("<float Damage>|<optional>draw <int Count>|</optional>idx:<int Index>")]
public struct SpellCard { ... }
```

`<type fieldname>` declares a field. Everything else is literal text matched character by character. The example above matches `10.5|draw 2|idx:1` or `10.5|idx:0` (the `draw` segment is optional).

### XML Format

Semantically equivalent to compact format. Useful when precise whitespace control is needed:

```xml
<literal-template>
  <field type="float" name="X"/>
  <text>, </text>
  <field type="float" name="Y"/>
</literal-template>
```

Matches `3.5, -2.1`. `<text>` elements wrap exact-match characters, `<field>` declares fields, `<optional>` wraps optional blocks.

### Multi-line Templates

Templates support C# raw string literal multi-line syntax. Line breaks are normalized to spaces during parsing:

```csharp
[LiteralTemplate("""
    <float X>
    <float Y>
    """)]
public struct PointMultiLine { public float X; public float Y; }
```

## Optional Blocks `<optional>`

Segments wrapped in `<optional>...</optional>` may be absent from the input. Matching logic: save the current position, attempt to match the block contents, continue on success, restore position and skip the block on failure.

```csharp
[LiteralTemplate("<float Damage>|<optional>draw <int DrawsProvide>|</optional>idx:<int StartIndex>")]
public struct SpellCard
{
    public float Damage;
    public int DrawsProvide;
    public int StartIndex;
}
```

`"10.5|draw 2|idx:1"` matches all fields. `"10.5|idx:0"` omits the `draw` segment; `DrawsProvide` retains its `default` value.

## Nested Structs

A type name in a template can reference another struct with `[LiteralTemplate]`. The generator recursively produces nested scan code:

```csharp
[LiteralTemplate("<float X> <float Y> <float Z>")]
public struct Vec3 { public float X, Y, Z; }

[LiteralTemplate("(<Vec3 Pos>)")]
public struct Entity { public Vec3 Pos; }

[LiteralTemplate("[<Entity Member>]")]
public struct Team { public Entity Member; }
```

`[(10 20 30)]` → Team → Entity → Vec3: three levels of recursion. The dependency graph is topologically sorted automatically. Circular dependencies trigger FLX002 compile errors.

## External Types

For third-party structs you cannot modify, use `[ExternalLiteralTemplate]`:

```csharp
[assembly: ExternalLiteralTemplate(typeof(UnityEngine.Vector3),
    "<float x> <float y> <float z>")]

// or on a class/struct
[ExternalLiteralTemplate(typeof(SomeExternalStruct), "<int A> <int B>")]
public class MyBehaviour { ... }
```

Priority B semantics: if a type has both `[LiteralTemplate]` and `[ExternalLiteralTemplate]`, the latter overrides the former.

## Type Aliases

Use `[LiteralTypeAlias]` to give built-in types domain-specific names. Purely cosmetic — does not change parsing logic:

```csharp
[assembly: LiteralTypeAlias("Distance", "float")]
[assembly: LiteralTypeAlias("Health", "int")]

// Aliases can be used in templates
[LiteralTemplate("<Distance Range> <Health HP>")]
public struct WeaponStats { public float Range; public int HP; }
```

## Enum Tags

v5.5+ supports `[LiteralTag]` attribute on enum members, enabling templates to recognize string labels directly:

```csharp
public enum Element : byte
{
    Physical = 0,
    [LiteralTag("fire")]  Fire,
    [LiteralTag("ice")]   Ice,
    [LiteralTag("magic")] Magic,
}

[LiteralTemplate("<float Amount><optional>:<Element Element></optional>")]
public struct ElemValue
{
    public float Amount;
    public Element Element;
}
```

The generated scanner includes a `switch(new string(src.Slice(...)))` block mapping `"fire"` to `Element.Fire`. The template matches `42`, `-5`, `1.5:fire`, `100:ice`.

::: tip
`[LiteralTag]` eliminates manual delegates for most tagged literal formats.
:::

## Built-in Types

The source generator supports 12 C# built-in unmanaged types, each with a corresponding `LiteralTemplateRegistry.Scan_Xxx` method:

| Alias | C# Type | Recognized Format |
|------|---------|-------------------|
| `float` | `float` | `-?\d+(\.\d+)?[fF]?` |
| `double` | `double` | `-?\d+(\.\d+)?([eE][+-]?\d+)?[dD]?` |
| `int` | `int` | `-?\d+` |
| `uint` | `uint` | `\d+` |
| `long` | `long` | `-?\d+[lL]?` |
| `ulong` | `ulong` | `\d+[uU]?[lL]?` |
| `short` | `short` | `-?\d+` |
| `ushort` | `ushort` | `\d+` |
| `byte` | `byte` | `\d+` |
| `sbyte` | `sbyte` | `-?\d+` |
| `bool` | `bool` | `true` / `false` |
| `char` | `char` | Single character |

All built-in scanners are zero-allocation span methods annotated with `AggressiveInlining`.

## Compiler Diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| FLX001 | Error | Template syntax or format error |
| FLX002 | Error | Circular dependency between template types |
| FLX003 | Error | `readonly struct` cannot use `[LiteralTemplate]` (field assignment requires a mutable struct) |
| FLX004 | Warning | Template references a type without `[LiteralTemplate]` or `[ExternalLiteralTemplate]` registration (field is skipped) |

## Manual Delegate: Advanced / Fallback

Use a handwritten `LiteralScanner<TData>` delegate when the literal syntax is too irregular for a template. When `[LiteralTemplate]` is present this field is not needed, but setting both is not an error: generated scanners take priority.

### Signature

```csharp
public delegate int LiteralScanner<TData>(
    ReadOnlySpan<char> src,  // Full source text
    int pos,                 // Current scan position
    out TData value          // Parsed value on match
);
```

- **Returns `pos`**: no match — lexer continues trying variables, operators, brackets
- **Returns `> pos`**: match succeeded — characters from `pos` to the returned position are consumed
- **`out TData value`**: written with the parsed value on match; `default` on no match

### Default Scanner

For simple numeric formats, use `CreateDefaultNumberScanner` instead of writing a scanner from scratch:

```csharp
config.LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
    s => float.Parse(s.TrimEnd('f', 'F')));
```

Equivalent to matching `\d+(\.\d+)?[fF]?` character by character, then calling the provided parser to convert to `TData`. `CreateDefaultNumberScanner` internally calls `ToString()` on the matched span and passes the string to the parser, producing a one-time compile-time allocation.

### Hex Integers

Match `0xFF` format:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 2 >= src.Length) return pos;
    if (src[pos] != '0' || (src[pos + 1] != 'x' && src[pos + 1] != 'X'))
        return pos;

    int end = pos + 2;
    while (end < src.Length && IsHexDigit(src[end])) end++;
    if (end == pos + 2) return pos;

    value = ParseHex(src.Slice(pos + 2, end - pos - 2));
    return end;
};

static bool IsHexDigit(char c) =>
    char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
```

Key: when the `0x` prefix check fails, return `pos` — the lexer will then retry and recognize `0` as a regular decimal digit.

### Keyword Literals

Match `true` / `false` as literals and map them to `1` / `0`:

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

`Span.SequenceEqual` is a zero-allocation way to perform prefix matching on strings.

### Do Nothing

A scanner that always returns `pos` — the lexer falls back to operators and other matchers:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out float v) =>
{
    v = 0;
    return pos;
};
```

## Notes

- Scanners should advance character by character. Regex introduces heap allocations that negate Span's zero-allocation advantage
- `ToString()` / `float.Parse` and similar operations produce one-time compile-time allocations, not runtime hot-path allocations
- Custom scanners are fully compatible with `VariablePatterns`: the lexer tries the scanner first, variable patterns only on no match
- The `TData : unmanaged` constraint excludes reference types like `string`. Use `enum` or `byte` fields to encode extra information
- When to use a manual delegate: irregular syntax that templates cannot express, or when the source generator is unavailable (pre-C# 12)
