# 示例：元素伤害公式

使用 `[Template]` + `[Tag]` Source Generator 实现带元素标签的字面量语法。

## 场景

游戏中的元素伤害公式: `[atk] * 2.5:fire + [bonus] - [def]`。`2.5:fire` 是一个火元素倍率。纯魔法攻击对纯物理防御是真实伤害：减法中元素不相等则忽略减值。

v5.5+ 的 `[Tag]` 使枚举标签纳入模板系统，无需手写 `LiteralScanner` 委托。

## TData 结构体

```csharp
public enum Element : byte
{
    Physical = 0,
    [Tag("fire")]  Fire,
    [Tag("ice")]   Ice,
    [Tag("magic")] Magic,
}

[Template("<float Amount><optional>:<Element Element></optional>")]
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

## 字面量扫描

`[Template]` + `[Tag]` 属性由 Source Generator 在编译期自动生成扫描代码。
`LexerConfig.LiteralScanner` 无需设置。模板 `<float Amount><optional>:<Element Element></optional>` 识别 `42`、`1.5:fire`、`-3:ice`。

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

## Lexer 配置

```csharp
var config = new LexerConfig<ElemValue>
{
    LiteralOper = (byte)ElemOp.Const,
    // 扫描器由 [Template] Source Generator 自动注入
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

## 使用

```csharp
var def    = new ElemDef();
var runner = new FluxAssembler<ElemValue, ElemDef>(def);
var lexer  = new FluxLexer<ElemValue>(config);

// [atk] * 2.5:fire + [bonus] - [def]
var f = runner.Compile(lexer.Lex("[atk] * 2.5:fire + [bonus] - [def]"));
var result = runner.Instantiate(f)
    .Set("atk",   new ElemValue(100f, Element.Physical))
    .Set("bonus", new ElemValue(50f,  Element.Ice))
    .Set("def",   new ElemValue(30f,  Element.Fire))
    .Run();
// → 270.00:Fire
```

## 要点

- `[Template]` + `[Tag]` 替代手写委托，模板直接表达 `:tag` 后缀语法
- Mul/Div 保留乘数/除数的元素类型，Add/Sub 保留左操作数的元素类型
- JIT 路径通过 `Expression.Call` 调用静态方法，避免繁琐的 `Expression.MemberInit`
- 完整源码见 `examples/ElemMath/`
