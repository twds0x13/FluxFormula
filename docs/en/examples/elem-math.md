# Example: Elemental Damage Formula

Using `[LiteralTemplate]` + `[LiteralTag]` source generator for inline element tag syntax.

## Scenario

Elemental damage formulas in games: `[atk] * 2.5:fire + [bonus] - [def]`. `2.5:fire` is a fire-typed multiplier. Magic damage vs. physical defense is true damage: subtraction ignores the right operand when elements differ.

v5.5+ `[LiteralTag]` brings enum labels into the template system, eliminating manual `LiteralScanner` delegates.

## TData Struct

```csharp
public enum Element : byte
{
    Physical = 0,
    [LiteralTag("fire")]  Fire,
    [LiteralTag("ice")]   Ice,
    [LiteralTag("magic")] Magic,
}

[LiteralTemplate("<float Amount><optional>:<Element Element></optional>")]
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

## Literal Scanning

The `[LiteralTemplate]` + `[LiteralTag]` attributes trigger compile-time code generation.
`LexerConfig.LiteralScanner` is not needed. Template `<float Amount><optional>:<Element Element></optional>` recognizes `42`, `1.5:fire`, `-3:ice`.

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
        ElemOp.RParen => new OpPair { PairRole = Pair.Right, TargetLeft = (byte)ElemOp.LParen },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;
    public OperandPosition GetFirstPosition(byte op) => OperandPosition.Left;

    public byte ResolveToken(byte op, TokenContext ctx)
        => op == (byte)ElemOp.Sub && ctx == TokenContext.OperandExpected
            ? (byte)ElemOp.Neg : op;

    public string GetOperatorName(byte op) => ((ElemOp)op).ToString();

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

## Lexer Configuration

```csharp
var config = new LexerConfig<ElemValue>
{
    LiteralOper = (byte)ElemOp.Const,
    // Scanner auto-injected by [LiteralTemplate] Source Generator
    Operators =
    {
        new("+", (byte)ElemOp.Add, slots: new sbyte[] { -1, +1 }),
        new("-", (byte)ElemOp.Sub, slots: new sbyte[] { -1, +1 }),
        new("*", (byte)ElemOp.Mul, slots: new sbyte[] { -1, +1 }),
        new("/", (byte)ElemOp.Div, slots: new sbyte[] { -1, +1 }),
    },
    Brackets = { new("(", ")", (byte)ElemOp.LParen, (byte)ElemOp.RParen) },
    VariablePatterns = { new("[", "]") },
};
```

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
// → 270.00:Fire
```

## Key Points

- `[LiteralTemplate]` + `[LiteralTag]` replace manual delegates for `:tag` suffix syntax
- Mul/Div preserve the multiplier/divisor element type; Add/Sub preserve the left operand element
- JIT path uses `Expression.Call` to static methods, avoiding verbose `Expression.MemberInit`
- Full source at `examples/ElemMath/`
