# 示例：法术上下文

演示双字段立即数语法和链式修正。`float` 字段为伤害修正值（正数增伤、负数减伤），`int` 字段为抽牌提供量。每张卡自动消耗 1 抽作为施法成本，通过 Connect 串联形成施法队列。

## 场景

Noita 式法术修正系统。每张法术卡有两个属性：
- **伤害修正**：浮点数，正值 = 增加最终伤害，负值 = 降低最终伤害
- **抽牌提供**：整数，本张卡提供的抽牌次数。施法自动消耗 1 抽，净变化 = 提供量 - 1

`10.5|0` 是纯伤害修正法术：提供 0 抽，净消耗 1 抽。`0|2` 是二重施法：`1 + 2 - 1 = 2`，后续两次施法免费。`-5|2` 是含 tradeoff 的抽牌法术：`1 + 2 - 1 = 2`，以降低 5 点伤害为代价换取 2 次免费施法。`20|1` 是自偿法术：`1 + 1 - 1 = 1`，提供 1 抽恰好抵消自身成本。

**法术回绕**（Noita 机枪法杖）：当链末剩余抽数 > 0 时，while 循环将输出重新注入链首。链中 DrawsLeft 归零后自动透传，while 循环自然终止。

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

`Add` 同时作用于 Damage 和 DrawsLeft 两个字段。DrawsLeft 计算自动扣除 1 抽施法成本。

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

    // 扫描整数（抽牌提供量）
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
- `10.5|0` — 伤害 10.5，不提供额外抽牌（施法消耗 1 抽，净 -1）
- `0|2` — 不改变伤害，提供 2 抽：`1 + 2 - 1 = 2`
- `-5|2` — 降低 5 伤害，提供 2 抽：`1 + 2 - 1 = 2`
- 省略 `|draws` 时默认为 0

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
            DrawOp.Add => a.DrawsLeft <= 0
                ? a
                : new DrawState(a.Damage + b.Damage, a.DrawsLeft + b.DrawsLeft - 1),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var argA = regs[inst.Arg0];
        var argB = regs[inst.Arg1];
        var dmgA = Expression.Field(argA, nameof(DrawState.Damage));
        var dmgB = Expression.Field(argB, nameof(DrawState.Damage));
        var drawA = Expression.Field(argA, nameof(DrawState.DrawsLeft));
        var drawB = Expression.Field(argB, nameof(DrawState.DrawsLeft));
        var drawZero = Expression.LessThanOrEqual(drawA, Expression.Constant(0));
        var one = Expression.Constant(1);
        var ctor = typeof(DrawState).GetConstructor(new[] { typeof(float), typeof(int) });

        return ((DrawOp)op) switch
        {
            DrawOp.Add => Expression.Condition(
                drawZero,
                argA,
                Expression.MemberInit(
                    Expression.New(ctor),
                    Expression.Bind(typeof(DrawState).GetField(nameof(DrawState.Damage)),
                        Expression.Add(dmgA, dmgB)),
                    Expression.Bind(typeof(DrawState).GetField(nameof(DrawState.DrawsLeft)),
                        Expression.Subtract(Expression.Add(drawA, drawB), one)))),
            _ => Expression.Constant(default(DrawState)),
        };
    }
}
```

`Add` 中 `- 1` 是隐式施法成本。`b.DrawsLeft` 是卡面的抽牌提供量，净变化 = `提供量 - 1`。伤害修正直接累加，不受成本影响。

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

// 构建法术卡：Modifier 形式为 "[prev] + 卡面修正"
// 卡1: +10 伤害修正, 0 抽（仅消耗施法成本）
// 卡2: +7 伤害修正,  0 抽
// 卡3: +5 伤害修正,  0 抽
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|0"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|0"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|0"));

// 所有卡串联为一条链
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 7 抽初始状态：会触发法术回绕（Noita 机枪法杖）
DrawState state = runner.Instantiate(chain)
    .Set("prev", new DrawState(0, 7))
    .Run();
// → (22.5 dmg, 4 draws)  链末仍有 4 抽剩余

// 法术回绕：剩余抽数 > 0 时重新从链首开始
while (state.DrawsLeft > 0)
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
}
// 回绕 1: (22.5, 4) → (45.0, 1)  第二轮抽完 3 张卡
// 回绕 2: (45.0, 1) → (55.5, 0)  链中抽数耗尽，仅执行卡1后自动透传
// → (55.5 dmg, 0 draws)
```

## 原理

### Connect 不合并字节码

`Connect()` 不在链创建时合并 `Instruction[]`。每张卡作为独立的 `Modifier` 保留自己的字节码片段，存储在 `ChainLink[]` 数组中。物理拼接推迟到求值时刻，且仅当链长超过 `MergeThreshold`（默认 8）时才会合并为原子公式。

### Per-link JIT 缓存复用

每条 `ChainLink` 的 delegate 通过 `DualHash64` 独立缓存在 `FormulaCache` 中。`Instantiate(chain)` 首次运行时编译所有 link，后续同链的 `Instantiate` 直接命中缓存。不同卡组共享同一张卡时（如卡1 在多个卡组中出现），delegate 跨链复用。

### while 回绕零重编译

回绕循环中的 `runner.Instantiate(chain).Set("prev", state).Run()` 走已编译 delegate。`Instantiate` 是轻量操作：`ref struct` 栈分配 + 缓存命中的 delegate 查找。每次回绕的执行成本 = N 次函数指针调用（N = 链中卡数），编译在首次 `Instantiate` 时完成。

```csharp
// 编译：仅发生一次，在首次 Instantiate(chain) 时
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 回绕：以下循环中无任何编译行为
while (state.DrawsLeft > 0)
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
}
```

### R1 总线传递上下文

每张卡的 `[prev] + 卡面修正` 通过 R1 寄存器读取上一张卡的输出。`Set("prev", state)` 将当前上下文注入链首卡的第一 Immediate 槽位；求值后 R1 携带最终 `DrawState` 返回。整条链的上下文传递即 R1 的链式读写，无堆分配。

### 隐式施法成本

每张卡自动消耗 1 抽。`10.5|0` 的第二字段 `0` 表示"本卡提供 0 抽"，净消耗 1 抽。`0|2`：剩余 1 抽 + 提供 2 抽 - 成本 1 抽 = 2 抽。施法成本是常数，卡面数值即卡面数值。

## 要点

- `float|int` 双字段立即数是 LiteralScanner 的另一种应用：`|` 作为字段分隔符
- `Add` 同时作用于伤害修正和抽数修正，隐式扣除 1 抽施法成本
- 二重施法（`0|2`）和 tradeoff（`-5|2`）是同一格式的自然推论
- 法术回绕：while 循环将链输出重新注入链首，链中 DrawsLeft 归零后自动透传
- Connect 保持每张卡独立，per-link delegate 缓存在 FormulaCache 中跨链复用
- 回绕 100 次是 100 次 delegate 调用，不是 100 次编译
