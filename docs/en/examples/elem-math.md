# Example: Elemental Damage Formula

Using a custom `ElemValue` struct and `LiteralScanner` for inline element tag syntax.

## Scenario

Elemental damage formulas in games: `[atk] * 2.5:fire + [bonus] - [def]`. `2.5:fire` is a fire-typed multiplier. Magic damage against physical defense is true damage: subtraction ignores the right operand when elements differ.

::: tip Template-first
For fixed-format literals, prefer `[LiteralTemplate]`. This example uses a delegate because the `:tag` suffix syntax cannot be expressed with a template.
:::

## TData Struct

```csharp
public enum Element : byte { Physical, Fire, Ice, Magic }

public struct ElemValue : IEquatable<ElemValue>
{
    public float Amount;
    public Element Element;

    public ElemValue(float amount, Element element = Element.Physical)
        => (Amount, Element) = (amount, element);

    public static ElemValue Add(ElemValue a, ElemValue b)
        => new(a.Amount + b.Amount, a.Element);

    public static ElemValue Sub(ElemValue a, ElemValue b)
        => new(a.Amount - (a.Element == b.Element ? b.Amount : 0f), a.Element);

    public static ElemValue Mul(ElemValue a, ElemValue b)
        => new(a.Amount * b.Amount, b.Element);

    public static ElemValue Div(ElemValue a, ElemValue b)
        => new(Math.Abs(b.Amount) < 1e-12f ? float.NaN : a.Amount / b.Amount, b.Element);

    public static ElemValue Neg(ElemValue a)
        => new(-a.Amount, a.Element);

    public readonly bool Equals(ElemValue other)
        => Amount == other.Amount && Element == other.Element;

    public override readonly string ToString()
        => Element == Element.Physical ? $"{Amount:F2}" : $"{Amount:F2}:{Element}";
}
```

## LiteralScanner

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out ElemValue value) =>
{
    value = default;
    if (pos >= src.Length) return pos;

    bool isNeg = src[pos] == '-';
    if (isNeg && pos + 1 < src.Length && !char.IsDigit(src[pos + 1])) return pos;
    if (!char.IsDigit(src[pos]) && !isNeg) return pos;
    int start = pos;
    if (isNeg) pos++;
    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    if (pos < src.Length && src[pos] == '.')
    {
        pos++;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    }
    float amount = float.Parse(src.Slice(start, pos - start), CultureInfo.InvariantCulture);

    Element elem = Element.Physical;
    if (pos < src.Length && src[pos] == ':')
    {
        pos++;
        int tagStart = pos;
        while (pos < src.Length && char.IsLetter(src[pos])) pos++;
        elem = src.Slice(tagStart, pos - tagStart).ToString() switch
        {
            "fire"  => Element.Fire,
            "ice"   => Element.Ice,
            "magic" => Element.Magic,
            _      => Element.Physical,
        };
    }
    value = new ElemValue(amount, elem);
    return pos;
};
```

## Definition

```csharp
public readonly struct ElemDef : IFluxExprDefinition<ElemValue>
{
    // ... standard GetArity/GetKind/GetPrecedence/GetPair/GetAssociativity/
    //     GetFirstPosition/ResolveToken/GetOperatorName ...

    public ElemValue Compute(byte op, Instruction inst, Span<ElemValue> regs)
    {
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((ElemOp)op) switch
        {
            ElemOp.Add => ElemValue.Add(a, b),
            ElemOp.Sub => ElemValue.Sub(a, b),
            ElemOp.Mul => ElemValue.Mul(a, b),
            ElemOp.Div => ElemValue.Div(a, b),
            ElemOp.Neg => ElemValue.Neg(a),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var t = typeof(ElemValue);
        var a = regs[inst.Arg0];
        var b = regs[inst.Arg1];
        return ((ElemOp)op) switch
        {
            ElemOp.Add => Expression.Call(t.GetMethod(nameof(ElemValue.Add))!, a, b),
            ElemOp.Sub => Expression.Call(t.GetMethod(nameof(ElemValue.Sub))!, a, b),
            ElemOp.Mul => Expression.Call(t.GetMethod(nameof(ElemValue.Mul))!, a, b),
            ElemOp.Div => Expression.Call(t.GetMethod(nameof(ElemValue.Div))!, a, b),
            ElemOp.Neg => Expression.Call(t.GetMethod(nameof(ElemValue.Neg))!, a),
            _ => Expression.Constant(default(ElemValue)),
        };
    }
}
```

## Operator Semantics

| Operator | Same element | Different element |
|----------|-------------|-------------------|
| Add | Sum Amount, keep left element | Same: amounts add, element follows left |
| Sub | Full reduction: `a.Amount - b.Amount` | Ignore right: true damage |
| Mul | Multiply Amount, keep right element (multiplier determines type) | Same |
| Div | Divide Amount, keep right element | Same |
| Neg | Negate Amount, keep element | — |

## Usage

```csharp
var def    = new ElemDef();
var runner = new FluxAssembler<ElemValue, ElemDef>(def);
var lexer  = new FluxLexer<ElemValue>(config);

var f = runner.Compile(lexer.Lex("[atk] * 2.5:fire + [bonus] - [def]"));
var result = runner.Instantiate(f)
    .Set("atk",   new ElemValue(100f, Element.Physical))
    .Set("bonus", new ElemValue(50f,  Element.Ice))
    .Set("def",   new ElemValue(30f,  Element.Fire))
    .Run();
// 100:Physical * 2.5:Fire = 250:Fire (Mul: keep multiplier element)
// 250:Fire + 50:Ice      = 300:Fire (Add: keep left element)
// 300:Fire - 30:Fire     = 270:Fire (Sub: same element full reduction)
```

## Key Points

- `LiteralScanner` receives `ReadOnlySpan<char>` and controls scan boundaries directly
- Returning `pos` (no match) lets the lexer try other rules; returning `> pos` means characters consumed
- The `unmanaged` constraint on `TData` excludes reference types; element tags must be enums
- `:tag` only attaches to numbers (`1.5:fire`), never standalone
- For fixed-format literals, prefer `[LiteralTemplate]`; manual delegates for irregular syntax
