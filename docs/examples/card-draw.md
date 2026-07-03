# 示例：法术上下文

演示命名字段立即数语法、链式修正和法术回绕追踪。`float` 字段为伤害修正值（正数增伤、负数减伤），`byte` 字段为抽牌提供量。每张卡自动消耗 1 抽作为施法成本，通过 Connect 串联形成施法队列，SpellTracker 实现 Noita 式已消费卡掩码追踪。

## 场景

Noita 式法术修正系统。每张法术卡有两个属性：
- **伤害修正**：浮点数，正值 = 增加最终伤害，负值 = 降低最终伤害
- **抽牌提供**：整数，本张卡提供的抽牌次数。施法自动消耗 1 抽，净变化 = 提供量 - 1

`10.5|idx:0` 是纯伤害修正法术：提供 0 抽，卡索引 0。`0|draw 2|idx:1` 是二重施法：`1 + 2 - 1 = 2`，后续两次施法免费，卡索引 1。`-5|draw 2|idx:2` 是含 tradeoff 的抽牌法术：`1 + 2 - 1 = 2`，以降低 5 点伤害为代价换取 2 次免费施法，卡索引 2。`20|draw 1|idx:3` 是自偿法术：`1 + 1 - 1 = 1`，提供 1 抽恰好抵消自身成本。

**法术回绕**（Noita 机枪法杖）：链公式跑完整条链后，追踪公式用 `ConsumedThisRound` 更新位掩码。掩码未全满时 while 循环回绕链首；掩码全满后终止。链中 `DrawsProvide` 归零后自动透传，每张卡最多被执行 2 次（Noita 双倍利用率）。

## TData 结构体

```csharp
public struct SpellContext : IEquatable<SpellContext>
{
    public float Damage;              // 累积伤害
    public byte DrawsProvide;         // 剩余抽牌数
    public byte ConsumedThisRound;    // 本轮已消费卡数
    public byte StartIndex;           // 本轮起始卡位置
    byte _padding;                    // 保留

    public SpellContext(float damage, int draws, int consumed = 0, int startIndex = 0)
        => (Damage, DrawsProvide, ConsumedThisRound, StartIndex)
            = (damage, (byte)draws, (byte)consumed, (byte)startIndex);

    public readonly bool Equals(SpellContext other)
        => Damage == other.Damage && DrawsProvide == other.DrawsProvide
                                  && ConsumedThisRound == other.ConsumedThisRound
                                  && StartIndex == other.StartIndex;

    public override readonly string ToString()
        => $"({Damage:F1} dmg, {DrawsProvide} draws, {ConsumedThisRound} consumed, start={StartIndex})";
}
```

`sizeof(SpellContext) = 8` 字节（float + byte + byte + byte + 1 padding），在字节码的 Immediate 中占用 1 个 Instruction 槽。

## 操作符枚举

```csharp
public enum SpellOp : byte
{
    Const, Add,
    LParen, RParen, Return,
}
```

`Add` 同时作用于 Damage 和 DrawsProvide，隐式扣除 1 抽施法成本。

## LiteralScanner：`damage|draw N|idx:N` 命名字段格式

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out SpellContext value) =>
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
    if (pos >= src.Length || src[pos] != '|') { value = new SpellContext(damage, 0); return pos; }
    pos++;

    // 可选 'draw' 字段
    int draws = 0;
    if (src.Slice(pos).StartsWith("draw "))
    {
        pos += 5;
        bool neg = false;
        if (pos < src.Length && src[pos] == '-') { neg = true; pos++; }
        while (pos < src.Length && char.IsDigit(src[pos]))
        {
            draws = draws * 10 + (src[pos] - '0');
            pos++;
        }
        if (neg) draws = -draws;

        // 消费 '|' 分隔符
        if (pos < src.Length && src[pos] == '|') pos++;
    }

    // 必填 'idx:' 字段
    if (!src.Slice(pos).StartsWith("idx:"))
    {
        value = new SpellContext(damage, draws);
        return pos;
    }
    pos += 4;
    int index = 0;
    while (pos < src.Length && char.IsDigit(src[pos]))
    {
        index = index * 10 + (src[pos] - '0');
        pos++;
    }

    value = new SpellContext(damage, draws, 0, index);
    return pos;
};
```

格式规则：
- `10.5|idx:0` — 伤害 10.5，不提供额外抽牌，卡索引 0
- `0|draw 2|idx:1` — 不改变伤害，提供 2 抽：`1 + 2 - 1 = 2`，卡索引 1
- `-5|draw 2|idx:2` — 降低 5 伤害，提供 2 抽：`1 + 2 - 1 = 2`，卡索引 2
- `draw` 字段可省略（默认 0），`idx:` 字段必填

## 定义体

```csharp
public readonly struct SpellDef : IFluxExprDefinition<SpellContext>
{
    public byte GetReturnOp() => (byte)SpellOp.Return;

    public int GetArity(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => 2, _ => 0,
    };

    public OpType GetKind(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Const  => OpType.Immediate,
        SpellOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => 1, _ => 0,
    };

    public OpPair GetPair(byte op) => ((SpellOp)op) switch
    {
        SpellOp.LParen => new OpPair { PairRole = Pair.Left },
        SpellOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)SpellOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public SpellContext Compute(byte op, Instruction inst, ReadOnlySpan<SpellContext> regs)
    {
        SpellContext a = regs[inst.Arg0];
        SpellContext b = regs[inst.Arg1];
        return ((SpellOp)op) switch
        {
            SpellOp.Add => AddImpl(a, b),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((SpellOp)op) switch
        {
            SpellOp.Add => Expression.Call(
                typeof(SpellDef).GetMethod(nameof(AddImpl),
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic)!,
                regs[inst.Arg0], regs[inst.Arg1]),
            _ => Expression.Constant(default(SpellContext)),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SpellContext AddImpl(SpellContext a, SpellContext b)
    {
        return b.StartIndex < a.StartIndex || a.DrawsProvide <= 0
            ? a
            : new SpellContext(
                a.Damage + b.Damage,
                a.DrawsProvide + b.DrawsProvide - 1,
                a.ConsumedThisRound + 1,
                a.StartIndex);
    }
}
```

`AddImpl` 是 `Compute` 和 JIT 路径的共享实现。Struct 字段越多，表达式树的手写开销越大：`Expression.Field`、`Expression.Bind`、`MemberInit` 需逐个字段展开，代码量随字段数线性增长。将逻辑提取为静态方法后，`GetExpression` 退化为一行 `Expression.Call`，两个路径保持行为一致。[`AggressiveInlining`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.methodimploptions) 确保 JIT 编译时内联此方法，消除调用开销。

`Add` 的两个透传条件：`b.StartIndex < a.StartIndex`（卡已在前一射击中消费）或 `a.DrawsProvide <= 0`（抽数耗尽）。正常执行时 `ConsumedThisRound` 每卡递增，`StartIndex` 透传不变。伤害修正直接累加。

## 追踪结构体：SpellTracker

```csharp
public struct SpellTracker : IEquatable<SpellTracker>
{
    public SpellContext Context;      // 链公式输出
    public ulong ConsumedMask;        // 已消费位掩码
    public ulong RequiredMask;        // 终止掩码 = (1 << (maxIndex + 1)) - 1

    public SpellTracker(SpellContext context, ulong mask, ulong requiredMask)
        => (Context, ConsumedMask, RequiredMask) = (context, mask, requiredMask);

    public readonly bool Equals(SpellTracker other)
        => Context.Equals(other.Context) && ConsumedMask == other.ConsumedMask
                                         && RequiredMask == other.RequiredMask;

    public override readonly string ToString()
        => $"(mask: 0x{ConsumedMask:X}, req: 0x{RequiredMask:X}, {Context})";
}
```

`SpellTracker` 与 `SpellContext` 分离，链公式的热路径不触碰掩码。

## 追踪操作符

```csharp
public enum TrackerOp : byte
{
    Const, Track, Return,
}
```

`Track` 是一元操作符：接收 `[prev]`（上一轮输出），更新 `ConsumedMask` 并检查终止条件。

## 追踪定义体

```csharp
public readonly struct TrackerDef : IFluxDefinition<SpellTracker>
{
    public byte GetReturnOp() => (byte)TrackerOp.Return;

    public int GetArity(byte op) => ((TrackerOp)op) switch
    {
        TrackerOp.Track => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((TrackerOp)op) switch
    {
        TrackerOp.Const  => OpType.Immediate,
        TrackerOp.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => 0;

    public OpPair GetPair(byte op) => new OpPair { PairRole = Pair.None };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public SpellTracker Compute(byte op, Instruction inst, ReadOnlySpan<SpellTracker> regs)
    {
        SpellTracker t = regs[inst.Arg0];
        if (t.ConsumedMask == t.RequiredMask)
            return t;  // 所有卡已消费，终止

        byte consumed = t.Context.ConsumedThisRound;
        if (consumed <= 0)
            return t;  // 本轮无消费，透传

        // tzcnt：找第一个未消费位 = 本轮起始卡位置
        ulong inverted = ~t.ConsumedMask;
        int pos = 0;
        while ((inverted & 1) == 0) { inverted >>= 1; pos++; }

        ulong mask = t.ConsumedMask;
        for (int i = 0; i < consumed; i++)
            mask |= 1ul << (pos + i);

        var ctx = t.Context;
        ctx.ConsumedThisRound = 0;
        ctx.StartIndex = (byte)(pos + consumed);  // 下一轮从此位置继续
        return new SpellTracker(ctx, mask);
    }
}
```

`Track` 的逻辑分三步：

1. **掩码达到 RequiredMask** → 透传（while 循环终止）
2. **本轮无消费** → 透传（`ConsumedThisRound == 0`）
3. **正常执行**：`tzcnt(~mask)` 定位第一个未消费位作为本轮起始位置，按 `consumed` 数量从该位置起连续置位，`ConsumedThisRound` 归零，`StartIndex` 更新为 `pos + consumed`（下一轮从此位置继续）

`RequiredMask = (1 << (maxIndex + 1)) - 1`，由 C# 层根据链中卡的最大索引计算，在 `SpellTracker` 中跨轮透传。链公式跑完整条链后，追踪公式批量更新掩码。若抽数充足，while 循环回绕使链再跑一轮，掩码单调递增填满后自然终止，最高双倍利用率。

## Lexer 配置

```csharp
var config = new LexerConfig<SpellContext>
{
    LiteralOper   = (byte)SpellOp.Const,
    LiteralParser = _ => default,
    LiteralScanner = /* 见上节 */,
    Operators =
    {
        new("+", (byte)SpellOp.Add),
    },
    Brackets =
    {
        new("(", ")", (byte)SpellOp.LParen, (byte)SpellOp.RParen),
    },
};
```

## 使用

```csharp
var def    = new SpellDef();
var runner = new FluxAssembler<SpellContext, SpellDef>(def);
var lexer  = new FluxLexer<SpellContext>(config);

// 构建法术卡：Modifier 形式为 "[prev] + 卡面修正"
// 卡1: +10 伤害修正, 0 抽（仅消耗施法成本）
// 卡2: +7 伤害修正,  0 抽
// 卡3: +5 伤害修正,  0 抽
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|idx:1"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|idx:2"));

// 所有卡串联为一条链
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 追踪公式：Track [prev]
var trackerDef    = new TrackerDef();
var trackerConfig = new LexerConfig<SpellTracker>
{
    LiteralOper = (byte)TrackerOp.Const,
    LiteralParser = _ => default,
    Operators = { new("Track", (byte)TrackerOp.Track) { Arity = 1 } },
};
var trackerLexer = new FluxLexer<SpellTracker>(trackerConfig);
var tracker      = new FluxAssembler<SpellTracker, TrackerDef>(trackerDef);
var trackFormula = tracker.Compile(trackerLexer.Lex("Track [prev]"));

// 初始状态：7 抽 + 空掩码 + 终止掩码（3 张卡 → 0b111）
SpellContext state = new SpellContext(0, 7, startIndex: 0);
ulong mask = 0;
ulong requiredMask = (1ul << 3) - 1;  // 卡索引 0/1/2 → 低 3 位全 1

// Noita 法术回绕：链公式 → 追踪公式，交替执行
do
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
    var tracked = tracker.Instantiate(trackFormula)
        .Set("prev", new SpellTracker(state, mask, requiredMask))
        .Run();
    state = tracked.Context;
    mask  = tracked.ConsumedMask;
} while (mask != requiredMask);

// 第一枪：mask=0 → 链跑完 3 张卡（已消费卡 0/1/2），掩码 = 0b111
// 等于 requiredMask → 终止
// → 每张卡被执行 2 次（Noita 双倍利用率），因链先跑完一轮、掩码才更新
```

## 原理

### Connect 不合并字节码

`Connect()` 不在链创建时合并 `Instruction[]`。每张卡作为独立的 `Modifier` 保留自己的字节码片段，存储在 `ChainLink[]` 数组中。物理拼接推迟到求值时刻，且仅当链长超过 `MergeThreshold`（默认 8）时才会合并为原子公式。

### Per-link JIT 缓存复用

每条 `ChainLink` 的 delegate 通过 `DualHash64` 独立缓存在 `FormulaCache` 中。`Instantiate(chain)` 首次运行时编译所有 link，后续同链的 `Instantiate` 直接命中缓存。同一张卡出现在多个卡组中时，delegate 跨链复用。

### 追踪公式与掩码管理

链公式的 `Compute(Add)` 在每张卡执行时递增 `ConsumedThisRound`。链跑完后，追踪公式的 `Track` 操作符读取该值，三步完成掩码更新：

1. **tzcnt 定位起始位**：`~ConsumedMask` 的第一个 1 位 = 本轮起始卡位置
2. **连续置位**：从起始位起置 `consumed` 位
3. **归零 + 更新**：`ConsumedThisRound = 0`，`StartIndex = pos + consumed`，等待下一轮

掩码单调递增填充。达到 `RequiredMask` 时终止。

若本轮抽数不足以跑完整条链（如 5 张卡、初始 1 抽），只执行卡 0，`StartIndex` 更新为 `0 + 1 = 1`。下一轮 wand 充能注入新抽数后，链从卡 1 开始：`Add` 中 `b.StartIndex < a.StartIndex` 触发透传，跳过已消费的卡 0。

先执行链公式再执行追踪公式（Noita 原始行为：先施法再结算）。链完整跑一遍后掩码才更新，每张卡在掩码达到 RequiredMask 前最多被执行 2 次。

### while 回绕零重编译

回绕循环中的两条 `Run()` 均走已编译 delegate。`Instantiate` 是轻量操作：`ref struct` 栈分配 + 缓存命中的 delegate 查找。链公式和追踪公式各自编译一次，while 循环内无任何编译行为。

```csharp
// 编译：首次 Instantiate(chain) + 首次 Instantiate(trackFormula)
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 回绕：以下 do-while 中无任何编译行为
do
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
    var tracked = tracker.Instantiate(trackFormula)
        .Set("prev", new SpellTracker(state, mask))
        .Run();
    state = tracked.Context;
    mask  = tracked.ConsumedMask;
} while (mask != requiredMask);
```

### R1 总线传递上下文

每张卡的 `[prev] + 卡面修正` 通过 R1 寄存器读取上一张卡的输出。`Set("prev", state)` 将当前上下文注入链首卡的第一 Immediate 槽位；求值后 R1 携带最终 `SpellContext` 返回。整条链的上下文传递即 R1 的链式读写，无堆分配。

### 隐式施法成本

每张卡自动消耗 1 抽。`10.5|idx:0` 不提供额外抽牌，净消耗 1 抽。`0|draw 2|idx:1`：剩余 1 抽 + 提供 2 抽 - 成本 1 抽 = 2 抽。施法成本是常数，不嵌入卡面数据。

## 要点

- `damage|draw N|idx:N` 命名字段格式：`draw` 可省略（默认 0），`idx:` 必填
- `Add` 同时作用于伤害修正和抽数修正，隐式扣除 1 抽施法成本；`b.StartIndex < a.StartIndex` 时透传跳过已消费卡
- `ConsumedThisRound` 在链内递增，由追踪公式批量消费后归零
- `StartIndex` 由追踪公式更新为 `pos + consumed`，确保下一轮从未消费位置继续
- 二重施法（`0|draw 2`）和 tradeoff（`-5|draw 2`）是同一格式的自然推论
- 法术回绕：链公式 → 追踪公式交替执行，掩码单调递增达到 RequiredMask 后终止
- `SpellTracker` 与 `SpellContext` 分离，链公式热路径不触碰 `ulong` 掩码
- Connect 保持每张卡独立，per-link delegate 缓存在 FormulaCache 中跨链复用
- `RequiredMask = (1 << (maxIndex + 1)) - 1`：由 C# 层根据链中最大卡索引计算，透传至追踪公式作为终止条件
- 回绕 100 次是 200 次 delegate 调用（链 + 追踪），不是 100 次编译
