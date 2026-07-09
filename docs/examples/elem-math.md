# 示例：元素伤害公式

使用自定义 `ElemValue` 结构体和 `LiteralScanner` 实现带元素标签的字面量语法。

## 场景

游戏中的元素伤害公式: `[atk] * 2.5:fire + [bonus] - [def]`。`2.5:fire` 是一个火元素倍率。纯魔法攻击对纯物理防御是真实伤害：减法中元素不相等则忽略减值。

这就需要两个自定义能力：词法层的 `:tag` 立即数语法，以及运算符层的元素感知求值逻辑。

::: tip 模板优先
对于格式固定的字面量（如 `<float X> <float Y>`），优先使用 `[LiteralTemplate]`。
本示例使用委托是因为 `:tag` 后缀语法无法用模板表达。
:::

## TData 结构体

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

## 操作符枚举

```csharp
public enum ElemOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return = 255,
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

## 定义体

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

## 运算符语义

| 操作符 | 同元素 | 异元素 |
|--------|--------|--------|
| Add | 叠加 Amount，保留左元素 | 保留左元素，数值直接累加 |
| Sub | 全额减免: `a.Amount - b.Amount` | 忽略右操作数: 纯魔法对物理防御是真实伤害 |
| Mul | 乘 Amount，保留右元素（乘数决定攻击属性） | 同左 |
| Div | 除 Amount，保留右元素 | 同左 |
| Neg | 取反 Amount，保留元素 | — |

## 使用

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
// 100:Physical * 2.5:Fire = 250:Fire (Mul: 保留乘数元素)
// 250:Fire + 50:Ice      = 300:Fire (Add: 保留左元素)
// 300:Fire - 30:Fire     = 270:Fire (Sub: 同元素全额减免)
```

## 要点

- `LiteralScanner` 接收 `ReadOnlySpan<char>`，可控制扫描边界，支持任意语法
- 返回 `pos`（未匹配）让词法器继续尝试其他规则；返回 `> pos` 表示消费字符
- `TData` 的 `unmanaged` 约束排除了 `string` 等引用类型，元素标签必须编码为枚举
- `:tag` 仅附加在数字后面（`1.5:fire`），不独立出现（`[bonus]:ice` 中的 `:ice` 不会被解析为立即数）
- 格式固定的字面量优先用 `[LiteralTemplate]`；手动委托适用于不规则语法
