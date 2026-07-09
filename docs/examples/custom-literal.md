# 示例：自定义立即数语法

演示如何通过 `LiteralScanner` 扩展立即数字面量语法，在数值中嵌入类型标签。

## 场景

游戏中的元素伤害公式：`[atk] * 1.5:fire + [bonus]:ice - [def]:fire`。`1.5:fire` 是一个火元素倍率，`[def]:ice` 是冰元素防御值。纯魔法攻击对纯物理防御是真实伤害：减法中元素不相等则忽略减值。

这需要两个自定义能力：词法层的 `:tag` 立即数语法，以及运算符层的元素感知求值逻辑。

::: tip 模板优先
对于格式固定的字面量（如 `<float X> <float Y>`），优先使用 `[LiteralTemplate]` 替代手写委托（参见[字面量扫描器](../guide/literal-scanner.md)）。本示例使用委托是因为 `:element` tag 语法无法用模板表达。
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

    public readonly bool Equals(ElemValue other)
        => Amount == other.Amount && Element == other.Element;

    public override readonly string ToString()
        => Element == Element.Physical ? $"{Amount:F2}" : $"{Amount:F2}:{Element}";
}
```

`ElemValue` 满足 `unmanaged` 约束（仅含 float 和枚举字段）。`sizeof(ElemValue) = 8` 字节，在字节码的 Immediate 中占用 1 个 Instruction 槽。

## 操作符枚举

```csharp
public enum ElemOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return,
}
```

## LiteralScanner 实现

这是本文档的核心。`LiteralScanner` 是一个委托 `(ReadOnlySpan<char>, int, out TData) -> int`：接收源码和起始位置，返回匹配结束位置（未匹配时返回 `pos`），通过 `out` 输出解析后的 TData。

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out ElemValue value) =>
{
    value = default;
    if (pos >= src.Length || !char.IsDigit(src[pos])) return pos;

    // 扫描数字部分: \d+(\.\d+)?
    int start = pos;
    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    if (pos < src.Length && src[pos] == '.')
    {
        pos++;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    }

    float amount = float.Parse(src.Slice(start, pos - start));

    // 可选元素标签 : <tag>
    Element elem = Element.Physical;
    if (pos < src.Length && src[pos] == ':')
    {
        pos++;
        int tagStart = pos;
        while (pos < src.Length && char.IsLetter(src[pos])) pos++;
        elem = src.Slice(tagStart, pos - tagStart).ToString() switch
        {
            "fire" => Element.Fire,
            "ice"  => Element.Ice,
            "magic" => Element.Magic,
            _      => Element.Physical,
        };
    }

    value = new ElemValue(amount, elem);
    return pos;
};
```

`LiteralScanner` 的签名、返回值约定和更多简单示例见 [字面量扫描器](../guide/literal-scanner.md)。

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
        ElemOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)ElemOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;
    public OperandPosition GetFirstPosition(byte op) => OperandPosition.Left;

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
        // 访问 ElemValue 的 Amount 和 Element 字段
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

**运算符语义**：

| 操作符 | 同元素 | 异元素 |
|--------|--------|--------|
| Add | 叠加 Amount，保留左元素 | 同左：火伤+冰伤叠加数值，元素取左 |
| Sub | 全额减免：`a.Amount - b.Amount` | 忽略右操作数：纯魔法对物理防御是真实伤害 |
| Mul | 乘 Amount，保留左元素 | 同左：倍率不关心元素 |
| Div | 除 Amount，保留左元素 | 同左 |
| Neg | 取反 Amount，保留元素 | — |

## Lexer 配置

```csharp
var config = new LexerConfig<ElemValue>
{
    LiteralOper   = (byte)ElemOp.Const,
    LiteralScanner = /* 见上节 */,
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

`LiteralScanner` 是字面量扫描入口。有 `[LiteralTemplate]` 时无需设置此字段，委托方式为手动回退路径。

## 使用

```csharp
var def    = new ElemDef();
var runner = new FluxAssembler<ElemValue, ElemDef>(def);
var lexer  = new FluxLexer<ElemValue>(config);

// 公式: [atk] * 1.5:fire + [bonus]:ice - [def]:fire
var lexResult = lexer.Lex("[atk] * 1.5:fire + [bonus]:ice - [def]:fire");
var formula   = runner.Compile(lexResult);

// atk = 100(物), bonus = 50(冰), def = 30(火)
ElemValue result = runner.Instantiate(formula)
    .Set("atk",    new ElemValue(100f, Element.Physical))
    .Set("bonus",  new ElemValue(50f,  Element.Ice))
    .Set("def",    new ElemValue(30f,  Element.Fire))
    .Run();
// 计算过程:
// 100:Physical * 1.5:Fire = 150:Fire    (Mul: 保留左元素)
// 150:Fire + 50:Ice        = 200:Fire    (Add: 异元素累加 Amount)
// 200:Fire - 30:Fire       = 170:Fire    (Sub: 同元素全额减免)
// 200:Fire - 30:Ice        = 200:Fire    (对比: 冰防不挡火攻)
```

## 要点

- `LiteralScanner` 接收 `ReadOnlySpan<char>`，可直接控制扫描边界，支持任意语法
- 返回 `pos`（未匹配）让词法器继续尝试其他匹配规则；返回 `> pos` 表示消费了 `pos` 到返回值之间的字符
- 编译期的 `ToString()` / `float.Parse` 分配在热路径之外，是可接受的
- TData 的 `unmanaged` 约束排除了字符串等引用类型。元素标签必须编码为枚举或 `byte`
- 格式固定的字面量优先用 `[LiteralTemplate]`；手动委托适用于不规则语法（如本示例的 `:tag` 后缀）

## 扩展：元素关系查找表

当前示例用 `a.Element == b.Element` 判断同元素。对于复杂的克制链（金木水火土循环克制），可升级为 record 查找表：

```csharp
sealed record ElementRule(byte Source, byte Target, float DefenseMultiplier);

static readonly ElementRule[] ElementTable =
{
    new((byte)Element.Fire, (byte)Element.Fire,   1.0f),  // 同元素：全额防御
    new((byte)Element.Fire, (byte)Element.Ice,    0.5f),  // 火克冰：半额防御
    new((byte)Element.Fire, (byte)Element.Water,  0.0f),  // 火被水克：防御无效
    new((byte)Element.Ice,  (byte)Element.Fire,   2.0f),  // 冰被火克：双倍穿透
};

static float GetDefenseMultiplier(Element attacker, Element defender)
{
    foreach (var rule in ElementTable)
        if (rule.Source == (byte)attacker && rule.Target == (byte)defender)
            return rule.DefenseMultiplier;
    return 0f; // 默认：互不克制
}
```

Subtract 改为从表中查询：

```csharp
float mult = GetDefenseMultiplier(a.Element, b.Element);
return new ElemValue(a.Amount - b.Amount * mult, a.Element);
```

此模式已在完整的五行循环克制系统中验证。查找表本质是关系模式：`(source, target) -> multiplier`，可轻松扩展为 CSV 或 ScriptableObject 驱动的外部配置。
