using System;
using FluxFormula.Core;

// ═══════════════════════════════════════════════════════
// SpellContext 的 [LiteralTemplate] 属性（CardDrawDef.cs）由 Source Generator
// 自动生成字面量扫描器，Lexer 构造函数自动发现并使用，无需手动设置
// LiteralScanner。手动实现版本参见文档 docs/examples/card-draw.md。
// ═══════════════════════════════════════════════════════

var config = new LexerConfig<SpellContext>
{
    LiteralOper   = (byte)SpellOp.Const,
    Operators =
    {
        new("+", (byte)SpellOp.Add, slots: new sbyte[] { -1, +1 }),
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
