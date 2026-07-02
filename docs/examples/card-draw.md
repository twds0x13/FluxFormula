# 示例：抽牌堆模型

演示双字段立即数语法和链式透传：每张法术卡同时携带伤害值和抽数消耗，通过 Connect 串联为抽牌队列。

## 场景

Noita 式抽牌法术：每张卡有伤害值和抽数消耗。`5.5|-1` 表示造成 5.5 伤害并消耗 1 抽。多张卡串联为队列，抽数耗尽后链自然终止。外部循环读取返回值的剩余抽数，决定是否继续抽牌。

## TData 结构体

```csharp
public struct DrawState : IEquatable<DrawState>
{
    public float Damage;      // 累积伤害
    public int DrawsLeft;     // 剩余抽牌数

    public DrawState(float damage, int draws)
        => (Damage, DrawsLeft) = (damage, draws);

    public readonly bool Equals(DrawState other)
        => Damage == other.Damage && DrawsLeft == other.DrawsLeft;

    public override readonly string ToString()
        => $"({Damage:F1} dmg, {DrawsLeft} draws)";
}
```

`sizeof(DrawState) = 8` 字节（float + int），在字节码的 Immediate 中占用 1 个 Instruction 槽。

## 操作符枚举

```csharp
public enum DrawOp : byte
{
    Const, Add,
    LParen, RParen, Return,
}
```

`Add` 同时作用于 Damage 和 DrawsLeft 两个字段。负数字面量（如 `-5|2`）覆盖所有卡面语义。

## LiteralScanner：`float|int` 双字段格式

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out DrawState value) =>
{
    value = default;
    if (pos >= src.Length || !(char.IsDigit(src[pos]) || src[pos] == '-')) return pos;

    // 扫描浮点数（Damage）
    int start = pos;
    if (src[pos] == '-') pos++;
    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    if (pos < src.Length && src[pos] == '.')
    {
        pos++;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    }
    float damage = float.Parse(src.Slice(start, pos - start));

    // 期望 '|' 分隔符
    if (pos >= src.Length || src[pos] != '|') { value = new DrawState(damage, 0); return pos; }
    pos++;

    // 扫描整数（DrawsLeft）
    int draws = 0;
    bool neg = false;
    if (pos < src.Length && src[pos] == '-') { neg = true; pos++; }
    while (pos < src.Length && char.IsDigit(src[pos]))
    {
        draws = draws * 10 + (src[pos] - '0');
        pos++;
    }
    if (neg) draws = -draws;

    value = new DrawState(damage, draws);
    return pos;
};
```

格式规则：
- `10.5|-1` — 完整格式：伤害 10.5，消耗 1 抽
- `10.5` — 省略 `|draws`，DrawsLeft 默认为 0
- `-5|2` — 负伤害，增加 2 抽
- `|` 后缺失数字时 DrawsLeft 为 0

## 定义体

```csharp
public readonly struct DrawDef : IFluxExprDefinition<DrawState>
{
    public byte GetReturnOp() => (byte)DrawOp.Return;

    public int GetArity(byte op) => ((DrawOp)op) switch
    {
        DrawOp.Add => 2, _ => 0,
    };

    public OpType GetKind(byte op) => ((DrawOp)op) switch
    {
        DrawOp.Const  => OpType.Immediate,
        DrawOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((DrawOp)op) switch
    {
        DrawOp.Add => 1, _ => 0,
    };

    public OpPair GetPair(byte op) => ((DrawOp)op) switch
    {
        DrawOp.LParen => new OpPair { PairRole = Pair.Left },
        DrawOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)DrawOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public DrawState Compute(byte op, Instruction inst, ReadOnlySpan<DrawState> regs)
    {
        DrawState a = regs[inst.Arg0];
        DrawState b = regs[inst.Arg1];
        return ((DrawOp)op) switch
        {
            DrawOp.Add => new DrawState(a.Damage + b.Damage, a.DrawsLeft + b.DrawsLeft),
            _ => default,
        };
    }
}
```

`Add` 同时累加伤害和抽数。负数字面量编码递减语义：`-5|2` 表示 5 点惩罚和 2 抽奖励，`10.5|-1` 表示 10.5 伤害消耗 1 抽。

## Lexer 配置

```csharp
var config = new LexerConfig<DrawState>
{
    LiteralOper   = (byte)DrawOp.Const,
    LiteralParser = _ => default,
    LiteralScanner = /* 见上节 */,
    Operators =
    {
        new("+", (byte)DrawOp.Add),
    },
    Brackets =
    {
        new("(", ")", (byte)DrawOp.LParen, (byte)DrawOp.RParen),
    },
};
```

## 使用

```csharp
var def    = new DrawDef();
var runner = new FluxAssembler<DrawState, DrawDef>(def);
var lexer  = new FluxLexer<DrawState>(config);

// 构建卡组：每张卡是"[prev] + 卡面值 - 卡面消耗"的 Modifier
// 卡1: +10 dmg, -1 draw
// 卡2: +7 dmg,  -1 draw
// 卡3: +5 dmg,  -1 draw
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|-1"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|-1"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|-1"));

// 初始状态：0 伤害，3 抽
var deck = runner.Compile(lexer.Lex("0|3"));
var chain = deck.ToModifier().Connect(mod1.ToModifier())
               .Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 一次抽完所有卡
DrawState result = runner.Instantiate(chain).Run();
// → (22.0 dmg, 0 draws) = 10 + 7 + 5 = 22 伤害，3 - 1 - 1 - 1 = 0 抽

// 外部循环控制逐张抽牌
DrawState state = new(0, 3);
var cards = new[] { mod1, mod2, mod3 };
foreach (var card in cards)
{
    if (state.DrawsLeft <= 0) break;
    state = runner.Instantiate(card).Set("prev", state).Run();
}
// → 每张卡独立执行，抽完自动停止
```

## 要点

- `float|int` 双字段立即数是 LiteralScanner 的另一种应用：`|` 作为字段分隔符，替代 `:tag` 标签模式
- `Add` 同时作用于 Damage 和 DrawsLeft，负数字面量替代所有其他运算
- DrawsLeft 降到 0 后，链中后续卡片仍在求值，但外部循环读取 `state.DrawsLeft <= 0` 后停止
- `DrawState` 同时携带伤害和抽数两个信息维度，Connect 串联时通过 R1 总线传递完整状态
