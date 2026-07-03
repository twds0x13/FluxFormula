# Example: Custom Literal Syntax

Using `LiteralScanner` to extend immediate literal syntax with inline type tags.

## Scenario

Elemental damage formulas in games: `[atk] * 1.5:fire + [bonus]:ice - [def]:fire`. `1.5:fire` is a fire-typed multiplier, `[def]:ice` is an ice-typed defense value. Pure magic damage against pure physical defense is true damage: subtraction ignores the right operand when elements differ.

This requires two custom capabilities: `:tag` literal syntax at the lexer layer, and element-aware evaluation at the operator layer.

## TData Struct

```csharp
public enum Element : byte { Physical, Fire, Ice, Magic }

public struct ElemValue : IEquatable<ElemValue>
{
    public float Amount;
    public Element Element;

    public ElemValue(float amount, Element element = Element.Physical)
        => (Amount, Element) = (amount, element);

    public readonly bool Equals(ElemValue other)
        => Amount == other.Amount && Element == other.Element;

    public override readonly string ToString()
        => Element == Element.Physical ? $"{Amount:F2}" : $"{Amount:F2}:{Element}";
}
```

`ElemValue` satisfies the `unmanaged` constraint (one float field plus an enum). `sizeof(ElemValue) = 8` bytes, consuming 1 Instruction slot per Immediate in the bytecode.

## Operator Enum

```csharp
public enum ElemOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return,
}
```

## LiteralScanner Implementation

This is the core of the example. `LiteralScanner` is a delegate `(ReadOnlySpan<char>, int, out TData) -> int`: it receives the source span and a start position, returns the end position (`pos` on no match), and outputs the parsed `TData` value.

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out ElemValue value) =>
{
    value = default;
    if (pos >= src.Length || !char.IsDigit(src[pos])) return pos;

    // Scan numeric part: \d+(\.\d+)?
    int start = pos;
    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    if (pos < src.Length && src[pos] == '.')
    {
        pos++;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    }

    float amount = float.Parse(src.Slice(start, pos - start));

    // Optional element tag: :<tag>
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
            _       => Element.Physical,
        };
    }

    value = new ElemValue(amount, elem);
    return pos;
};
```

See [Custom Literal Scanner](../guide/literal-scanner.md) for the delegate signature, return value conventions, and additional simple examples.

## Definition

```csharp
public readonly struct ElemDef : IFluxExprDefinition<ElemValue>
{
    public byte GetReturnOp() => (byte)ElemOp.Return;

    public int GetArity(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Add => 2, ElemOp.Sub => 2, ElemOp.Mul => 2,
        ElemOp.Div => 2, ElemOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Const  => OpType.Immediate,
        ElemOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((ElemOp)op) switch
    {
        ElemOp.Add => 1, ElemOp.Sub => 1, ElemOp.Mul => 2,
        ElemOp.Div => 2, ElemOp.Neg => 3, _ => 0,
    };

    public OpPair GetPair(byte op) => ((ElemOp)op) switch
    {
        ElemOp.LParen => new OpPair { PairRole = Pair.Left },
        ElemOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)ElemOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public byte ResolveToken(byte op, TokenContext ctx)
        => op == (byte)ElemOp.Sub && ctx == TokenContext.OperandExpected
            ? (byte)ElemOp.Neg : op;

    public ElemValue Compute(byte op, Instruction inst, Span<ElemValue> regs)
    {
        ElemValue a = regs[inst.Arg0];
        ElemValue b = regs[inst.Arg1];
        return ((ElemOp)op) switch
        {
            ElemOp.Add => new ElemValue(
                a.Amount + b.Amount, a.Element),
            ElemOp.Sub => new ElemValue(
                a.Amount - (a.Element == b.Element ? b.Amount : 0f), a.Element),
            ElemOp.Mul => new ElemValue(
                a.Amount * b.Amount, a.Element),
            ElemOp.Div => new ElemValue(
                Math.Abs(b.Amount) < float.Epsilon ? float.NaN : a.Amount / b.Amount, a.Element),
            ElemOp.Neg => new ElemValue(-a.Amount, a.Element),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var argA = regs[inst.Arg0];
        var argB = regs[inst.Arg1];
        var amtA = Expression.Field(argA, nameof(ElemValue.Amount));
        var amtB = Expression.Field(argB, nameof(ElemValue.Amount));
        var elemA = Expression.Field(argA, nameof(ElemValue.Element));
        var elemB = Expression.Field(argB, nameof(ElemValue.Element));
        var sameElement = Expression.Equal(
            Expression.Convert(elemA, typeof(byte)),
            Expression.Convert(elemB, typeof(byte)));

        Expression body = ((ElemOp)op) switch
        {
            ElemOp.Add => Expression.MemberInit(
                Expression.New(typeof(ElemValue).GetConstructor(new[] { typeof(float), typeof(Element) })),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Amount)),
                    Expression.Add(amtA, amtB)),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Element)), elemA)),
            ElemOp.Sub => Expression.MemberInit(
                Expression.New(typeof(ElemValue).GetConstructor(new[] { typeof(float), typeof(Element) })),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Amount)),
                    Expression.Subtract(amtA,
                        Expression.Condition(sameElement, amtB, Expression.Constant(0f)))),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Element)), elemA)),
            ElemOp.Mul => Expression.MemberInit(
                Expression.New(typeof(ElemValue).GetConstructor(new[] { typeof(float), typeof(Element) })),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Amount)),
                    Expression.Multiply(amtA, amtB)),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Element)), elemA)),
            ElemOp.Div => Expression.MemberInit(
                Expression.New(typeof(ElemValue).GetConstructor(new[] { typeof(float), typeof(Element) })),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Amount)),
                    Expression.Condition(
                        Expression.LessThan(
                            Expression.Call(typeof(Math).GetMethod("Abs", new[] { typeof(float) }), amtB),
                            Expression.Constant(float.Epsilon)),
                        Expression.Constant(float.NaN),
                        Expression.Divide(amtA, amtB))),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Element)), elemA)),
            ElemOp.Neg => Expression.MemberInit(
                Expression.New(typeof(ElemValue).GetConstructor(new[] { typeof(float), typeof(Element) })),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Amount)),
                    Expression.Negate(amtA)),
                Expression.Bind(typeof(ElemValue).GetField(nameof(ElemValue.Element)), elemA)),
            _ => Expression.Constant(default(ElemValue)),
        };
        return body;
    }
}
```

**Operator semantics**:

| Operator | Same element | Different element |
|----------|-------------|-------------------|
| Add | Sum Amount, keep left element | Same: amounts add, element follows the left |
| Sub | Full reduction: `a.Amount - b.Amount` | Ignore right operand: magic vs. physical defense is true damage |
| Mul | Multiply Amount, keep left element | Same: multipliers are element-agnostic |
| Div | Divide Amount, keep left element | Same |
| Neg | Negate Amount, keep element | — |

## Lexer Configuration

```csharp
var config = new LexerConfig<ElemValue>
{
    LiteralOper   = (byte)ElemOp.Const,
    LiteralScanner = /* see above */,
    Operators =
    {
        new("+", (byte)ElemOp.Add), new("-", (byte)ElemOp.Sub),
        new("*", (byte)ElemOp.Mul), new("/", (byte)ElemOp.Div),
    },
    Brackets =
    {
        new("(", ")", (byte)ElemOp.LParen, (byte)ElemOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};
```

`LiteralScanner` is the sole literal scanning entry point; it must be set.

## Usage

```csharp
var def    = new ElemDef();
var runner = new FluxAssembler<ElemValue, ElemDef>(def);
var lexer  = new FluxLexer<ElemValue>(config);

// Formula: [atk] * 1.5:fire + [bonus]:ice - [def]:fire
var lexResult = lexer.Lex("[atk] * 1.5:fire + [bonus]:ice - [def]:fire");
var formula   = runner.Compile(lexResult);

// atk = 100(phys), bonus = 50(ice), def = 30(fire)
ElemValue result = runner.Instantiate(formula)
    .Set("atk",    new ElemValue(100f, Element.Physical))
    .Set("bonus",  new ElemValue(50f,  Element.Ice))
    .Set("def",    new ElemValue(30f,  Element.Fire))
    .Run();
// Evaluation:
// 100:Physical * 1.5:Fire = 150:Fire    (Mul: keep left element)
// 150:Fire + 50:Ice        = 200:Fire    (Add: different elements, amounts sum)
// 200:Fire - 30:Fire       = 170:Fire    (Sub: same element, full reduction)
// 200:Fire - 30:Ice        = 200:Fire    (Compare: ice defense does not block fire)
```

## Key Points

- `LiteralScanner` receives `ReadOnlySpan<char>`, directly controls scan boundaries, and supports arbitrary syntax
- Returning `pos` (no match) lets the lexer continue trying other rules; returning `> pos` means the scanner consumed characters from `pos` to the return value
- `ToString()` / `float.Parse` allocations at compile time are outside the hot path and acceptable
- The `unmanaged` constraint on `TData` excludes reference types like `string`. Element tags must be encoded as enums or `byte`

## Extension: Element Relationship Lookup Table

The current example uses `a.Element == b.Element` for same-element checks. For complex counter systems (e.g., Wu Xing five-element cycles), upgrade to a record lookup table:

```csharp
sealed record ElementRule(byte Source, byte Target, float DefenseMultiplier);

static readonly ElementRule[] ElementTable =
{
    new((byte)Element.Fire, (byte)Element.Fire,   1.0f),  // Same: full defense
    new((byte)Element.Fire, (byte)Element.Ice,    0.5f),  // Fire beats Ice: half defense
    new((byte)Element.Fire, (byte)Element.Water,  0.0f),  // Water beats Fire: no defense
    new((byte)Element.Ice,  (byte)Element.Fire,   2.0f),  // Fire beats Ice: double penetration
};

static float GetDefenseMultiplier(Element attacker, Element defender)
{
    foreach (var rule in ElementTable)
        if (rule.Source == (byte)attacker && rule.Target == (byte)defender)
            return rule.DefenseMultiplier;
    return 0f; // Default: no cross-element defense
}
```

Subtract then queries the table:

```csharp
float mult = GetDefenseMultiplier(a.Element, b.Element);
return new ElemValue(a.Amount - b.Amount * mult, a.Element);
```

This pattern has been validated in a full Wu Xing cyclic counter system. The lookup table is a relational model: `(source, target) -> multiplier`, trivially extensible to CSV or ScriptableObject-driven external configuration.
