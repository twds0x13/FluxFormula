using System;
using System.Globalization;
using FluxFormula.Core;

// ═══════════════════════════════════════════════════════
// Lexer 配置：命名字段立即数格式 damage|draw N|idx:N
// ═══════════════════════════════════════════════════════
//
// ╔═ 模板优先（推荐）══════════════════════════════════╗
// ║ SpellContext 可通过 [LiteralTemplate] 自动生成     ║
// ║ 扫描器，无需手写委托。需在 csproj 中引用           ║
// ║ Source Generator（参见 Core.Tests.csproj）。       ║
// ║ 属性声明见 CardDrawDef.cs：                        ║
// ║                                                    ║
// ║ [LiteralTemplate(                                  ║
// ║   "<float Damage>|                                 ║
// ║    <optional>draw <int DrawsProvide></optional>|    ║
// ║    idx:<int StartIndex>")]                          ║
// ║ public struct SpellContext : IEquatable<...> { }    ║
// ║                                                    ║
// ║ 生成后去掉下文的 LiteralScanner = 赋值即可。        ║
// ╚═════════════════════════════════════════════════════╝
//
// 下方手写委托保留，供理解 LiteralScanner 接口机制参考。

var config = new LexerConfig<SpellContext>
{
    LiteralOper   = (byte)SpellOp.Const,
    LiteralScanner = (ReadOnlySpan<char> src, int pos, out SpellContext value) =>
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
        float damage = float.Parse(src.Slice(start, pos - start), CultureInfo.InvariantCulture);

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
    },
    Operators =
    {
        new("+", (byte)SpellOp.Add),
    },
    Brackets =
    {
        new("(", ")", (byte)SpellOp.LParen, (byte)SpellOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var def    = new SpellDef();
var runner = new FluxAssembler<SpellContext, SpellDef>(def);
var lexer  = new FluxLexer<SpellContext>(config);

// 构建法术卡：Modifier 形式为 "[prev] + 卡面修正"
// 卡1: +10 伤害修正, 0 抽（仅消耗施法成本）
// 卡2: +7 伤害修正,  0 抽
// 卡3: +5 伤害修正,  0 抽
var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));       // Formula（链首）
var mod2  = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();
var mod3  = runner.Compile(lexer.Lex("[prev] + 5|idx:2")).ToModifier();

// 所有卡串联为一条链
var chain = card1.Connect(mod2).Connect(mod3);

// 追踪公式：Track [prev]
var trackerDef    = new TrackerDef();
var trackerConfig = new LexerConfig<SpellTracker>
{
    LiteralOper = (byte)TrackerOp.Const,
    LiteralScanner = LexerConfig<SpellTracker>.CreateDefaultNumberScanner(_ => default),
    Operators = { new("Track", (byte)TrackerOp.Track) },
    VariablePatterns = { new("[", "]") },
};
var trackerLexer = new FluxLexer<SpellTracker>(trackerConfig);
var tracker      = new FluxAssembler<SpellTracker, TrackerDef>(trackerDef);
var trackFormula = tracker.Compile(trackerLexer.Lex("Track [prev]"));

// 初始状态：7 抽 + 空掩码 + 终止掩码（3 张卡 → 0b111）
SpellContext state = new SpellContext(0, 7, startIndex: 0);
ulong mask = 0;
ulong requiredMask = (1ul << 3) - 1;  // 卡索引 0/1/2 → 低 3 位全 1

// Noita 法术回绕：链公式 → 追踪公式，交替执行
int rounds = 0;
do
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
    var tracked = tracker.Instantiate(trackFormula)
        .Set("prev", new SpellTracker(state, mask, requiredMask))
        .Run();
    state = tracked.Context;
    mask  = tracked.ConsumedMask;
    rounds++;
} while ((mask & requiredMask) != requiredMask);

Console.WriteLine($"Rounds: {rounds}");
Console.WriteLine($"Final damage: {state.Damage}");
Console.WriteLine($"Final draws left: {state.DrawsProvide}");
Console.WriteLine($"Mask: 0x{mask:X}, required: 0x{requiredMask:X}");

// 预期：1 轮完成（3 张卡，初始 7 抽足够），最终伤害 = 10+7+5 = 22，掩码 = 0b111
