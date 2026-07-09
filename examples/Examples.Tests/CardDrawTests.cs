using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class CardDrawTests
{
    private static readonly SpellDef SpellDefIns = default;
    private static readonly TrackerDef TrackerDefIns = default;

    // ═══════════════════════════════════════════════════════
    // Lexer 工厂
    // ═══════════════════════════════════════════════════════

    private static FluxLexer<SpellContext> CreateSpellLexer()
    {
        return new FluxLexer<SpellContext>(new LexerConfig<SpellContext>
        {
            LiteralOper = (byte)SpellOp.Const,
            LiteralScanner = (ReadOnlySpan<char> src, int pos, out SpellContext value) =>
            {
                value = default;
                if (pos >= src.Length || !(char.IsDigit(src[pos]) || src[pos] == '-')) return pos;

                int start = pos;
                if (src[pos] == '-') pos++;
                while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                if (pos < src.Length && src[pos] == '.')
                {
                    pos++;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }
                float damage = float.Parse(src.Slice(start, pos - start), CultureInfo.InvariantCulture);

                if (pos >= src.Length || src[pos] != '|') { value = new SpellContext(damage, 0); return pos; }
                pos++;

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
                    if (pos < src.Length && src[pos] == '|') pos++;
                }

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
        });
    }

    private static FluxLexer<SpellTracker> CreateTrackerLexer()
    {
        return new FluxLexer<SpellTracker>(new LexerConfig<SpellTracker>
        {
            LiteralOper = (byte)TrackerOp.Const,
            LiteralScanner = LexerConfig<SpellTracker>.CreateDefaultNumberScanner(_ => default),
            Operators = { new("Track", (byte)TrackerOp.Track) },
            VariablePatterns = { new("[", "]") },
        });
    }

    // ═══════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════

    private static SpellContext EvalSpell(string expr)
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex(expr);
        var f = runner.Compile(r);
        return runner.Instantiate(f).Run();
    }

    private static SpellTracker EvalTracker(SpellTracker input)
    {
        var lexer  = CreateTrackerLexer();
        var runner = new FluxAssembler<SpellTracker, TrackerDef>(TrackerDefIns);
        var r = lexer.Lex("Track [prev]");
        var f = runner.Compile(r);
        return runner.Instantiate(f).Set("prev", input).Run();
    }

    // ═══════════════════════════════════════════════════════
    // Section 1: Literal Scanner
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Literal_DamageOnly()
    {
        var ctx = EvalSpell("10|idx:0");
        Assert.That(ctx.Damage, Is.EqualTo(10f));
        Assert.That(ctx.DrawsProvide, Is.EqualTo(0));
        Assert.That(ctx.StartIndex, Is.EqualTo(0));
    }

    [Test]
    public void Literal_WithDrawField()
    {
        var ctx = EvalSpell("0|draw 2|idx:1");
        Assert.That(ctx.Damage, Is.EqualTo(0f));
        Assert.That(ctx.DrawsProvide, Is.EqualTo(2));
        Assert.That(ctx.StartIndex, Is.EqualTo(1));
    }

    [Test]
    public void Literal_NegativeDamage()
    {
        var ctx = EvalSpell("-5|draw 2|idx:2");
        Assert.That(ctx.Damage, Is.EqualTo(-5f));
        Assert.That(ctx.DrawsProvide, Is.EqualTo(2));
        Assert.That(ctx.StartIndex, Is.EqualTo(2));
    }

    [Test]
    public void Literal_SelfSustaining()
    {
        var ctx = EvalSpell("20|draw 1|idx:3");
        Assert.That(ctx.Damage, Is.EqualTo(20f));
        Assert.That(ctx.DrawsProvide, Is.EqualTo(1));  // 提供 1 抽刚好抵消成本
        Assert.That(ctx.StartIndex, Is.EqualTo(3));
    }

    [Test]
    public void Literal_DecimalDamage()
    {
        var ctx = EvalSpell("10.5|idx:0");
        Assert.That(ctx.Damage, Is.EqualTo(10.5f));
    }

    // ═══════════════════════════════════════════════════════
    // Section 2: Single Card (Add operator)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Add_TwoCards_AccumulatesDamage()
    {
        var ctx = EvalSpell("10|draw 1|idx:0 + 7|idx:1");
        Assert.That(ctx.Damage, Is.EqualTo(17f));
        Assert.That(ctx.ConsumedThisRound, Is.EqualTo(1));  // 1 次 Add 消耗
    }

    [Test]
    public void Add_ConsumesOneDrawPerCast()
    {
        // 首卡提供 2 抽，净剩 = 2-1=1；次卡无提供，再消耗 1 抽；总共剩余 0
        var ctx = EvalSpell("0|draw 2|idx:0 + 10|idx:1");
        Assert.That(ctx.Damage, Is.EqualTo(10f));
        Assert.That(ctx.DrawsProvide, Is.EqualTo(1));
    }

    [Test]
    public void Add_DrawsExhausted_ShortCircuitsAtFirstCard()
    {
        // 首卡 DrawsProvide=0 → 短路，写入 R0 = a，返回 a（非 default）
        var ctx = EvalSpell("5|idx:0 + 100|idx:1");
        Assert.That(ctx.Damage, Is.EqualTo(5f));         // 返回首卡状态
        Assert.That(ctx.DrawsProvide, Is.EqualTo(0));    // 抽尽
    }

    [Test]
    public void Add_StartIndex_SkipsConsumedCard()
    {
        var ctx = EvalSpell("10|draw 1|idx:0 + 5|idx:0");
        // 两卡同索引 → b.StartIndex(0) >= a.StartIndex(0) → 正常执行
        Assert.That(ctx.Damage, Is.EqualTo(15f));
        Assert.That(ctx.ConsumedThisRound, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // Section 3: Parentheses
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Parentheses_GroupsAddition()
    {
        var ctx = EvalSpell("(10|draw 1|idx:0 + 7|idx:1)");
        Assert.That(ctx.Damage, Is.EqualTo(17f));
    }

    [Test]
    public void Parentheses_Nested()
    {
        var ctx = EvalSpell("((10|draw 1|idx:0 + 7|draw 1|idx:1) + 5|idx:2)");
        Assert.That(ctx.Damage, Is.EqualTo(22f));
        Assert.That(ctx.ConsumedThisRound, Is.EqualTo(2));  // 2 次 Add 操作
    }

    // ═══════════════════════════════════════════════════════
    // Section 4: Variable Injection
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Variables_InjectPrev_AccumulatesCorrectly()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex("[prev] + 10|idx:0");
        var f = runner.Compile(r);

        var initial = new SpellContext(5, 3);  // 5 dmg, 3 draws
        var result  = runner.Instantiate(f).Set("prev", initial).Run();

        // a = SpellContext(5,3), b = SpellContext(10,0)
        // Add => new(5+10=15, 3+0-1=2, 0+1=1, 0)
        Assert.That(result.Damage, Is.EqualTo(15f));
        Assert.That(result.DrawsProvide, Is.EqualTo(2));
        Assert.That(result.ConsumedThisRound, Is.EqualTo(1));
    }

    [Test]
    public void Variables_SetMultipleVars()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex("[a] + [b]");
        var f = runner.Compile(r);

        var a = new SpellContext(10, 2, startIndex: 0);
        var b = new SpellContext(20, 0, startIndex: 1);
        var result = runner.Instantiate(f).Set("a", a).Set("b", b).Run();

        // a.DrawsProvide(2) > 0 → 正常执行: new(10+20=30, 2+0-1=1, 0+1=1, 0)
        Assert.That(result.Damage, Is.EqualTo(30f));
        Assert.That(result.DrawsProvide, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // Section 5: Chain Connect
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Connect_TwoCardChain_CorrectDamage()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);

        var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
        var card2 = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();

        var chain = card1.Connect(card2);
        var state = new SpellContext(0, 5);  // 5 draws
        var result = runner.Instantiate(chain).Set("prev", state).Run();

        // card1: 0+10, 5+0-1=4, consum=1, start=0
        // card2 takes R1 from card1: +7, 4+0-1=3, consum=2, start=0
        Assert.That(result.Damage, Is.EqualTo(17f));
        Assert.That(result.DrawsProvide, Is.EqualTo(3));
        Assert.That(result.ConsumedThisRound, Is.EqualTo(2));
    }

    [Test]
    public void Connect_ThreeCardChain_FullDeck()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);

        var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
        var card2 = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();
        var card3 = runner.Compile(lexer.Lex("[prev] + 5|idx:2")).ToModifier();

        var chain = card1.Connect(card2).Connect(card3);
        var state = new SpellContext(0, 7);
        var result = runner.Instantiate(chain).Set("prev", state).Run();

        Assert.That(result.Damage, Is.EqualTo(22f));
        Assert.That(result.DrawsProvide, Is.EqualTo(4));  // 7 - 3 casts
        Assert.That(result.ConsumedThisRound, Is.EqualTo(3));
    }

    // ═══════════════════════════════════════════════════════
    // Section 6: TrackerDef
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Track_AllConsumed_ReturnsSelf()
    {
        // 掩码已满 → 直接返回
        var ctx     = new SpellContext(10, 0);
        var tracker = new SpellTracker(ctx, 0x7, 0x7);  // mask == required
        var result  = EvalTracker(tracker);

        Assert.That(result.ConsumedMask, Is.EqualTo(0x7));
        Assert.That(result.Context.Damage, Is.EqualTo(10f));
    }

    [Test]
    public void Track_NoConsumption_ReturnsSelf()
    {
        // 本轮无消费 → 透传
        var ctx     = new SpellContext(10, 0, consumed: 0);
        var tracker = new SpellTracker(ctx, 0x0, 0x7);
        var result  = EvalTracker(tracker);

        Assert.That(result.ConsumedMask, Is.EqualTo(0x0));
        Assert.That(result.Context.Damage, Is.EqualTo(10f));
    }

    [Test]
    public void Track_SetsBitsAndAdvancesStart()
    {
        // 链消耗了 2 张卡 → 从 pos=0 开始置位 2 位
        var ctx     = new SpellContext(22, 3, consumed: 2, startIndex: 0);
        var tracker = new SpellTracker(ctx, 0x0, 0x7);
        var result  = EvalTracker(tracker);

        // pos = TrailingZeroCount(~0) = 0
        // mask = 0 | ((1<<2)-1) << 0 = 0b011 (bit 0 + bit 1)
        Assert.That(result.ConsumedMask, Is.EqualTo(0x3));
        Assert.That(result.Context.ConsumedThisRound, Is.EqualTo(0));  // 归零
        Assert.That(result.Context.StartIndex, Is.EqualTo(2));         // pos + consumed = 0 + 2
    }

    [Test]
    public void Track_SecondRound_ContinuesFromLastPosition()
    {
        // 第一轮消耗了 2 张卡 (mask=0x3)，第二轮从 pos=2 开始消耗剩余的 1 张
        var ctx     = new SpellContext(22, 1, consumed: 1, startIndex: 2);
        var tracker = new SpellTracker(ctx, 0x3, 0x7);
        var result  = EvalTracker(tracker);

        // pos = TrailingZeroCount(~0x3) = TrailingZeroCount(0xFFFFFFFC) = 2
        // mask = 0x3 | ((1<<1)-1) << 2 = 0x3 | 0x4 = 0x7
        Assert.That(result.ConsumedMask, Is.EqualTo(0x7));
        Assert.That(result.Context.StartIndex, Is.EqualTo(3));  // pos + consumed = 2 + 1
    }

    [Test]
    public void Track_FinalRound_Done()
    {
        // 最后一轮完成后掩码满 → 终止
        var ctx     = new SpellContext(22, 0, consumed: 1, startIndex: 2);
        var tracker = new SpellTracker(ctx, 0x7, 0x7);  // already done
        var result  = EvalTracker(tracker);

        Assert.That(result.ConsumedMask, Is.EqualTo(0x7));  // 不变
    }

    // ═══════════════════════════════════════════════════════
    // Section 7: Tracker Round-Trip
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Track_MultiRound_CompletesAllCards()
    {
        // 三卡链 (idx:0,1,2)，初始 7 抽 → 一轮跑完
        // 然后追踪更新掩码 → 掩码满 → 终止
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var tracker = new FluxAssembler<SpellTracker, TrackerDef>(TrackerDefIns);

        var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
        var card2 = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();
        var card3 = runner.Compile(lexer.Lex("[prev] + 5|idx:2")).ToModifier();
        var chain = card1.Connect(card2).Connect(card3);

        var trackFormula = tracker.Compile(
            CreateTrackerLexer().Lex("Track [prev]"));

        SpellContext state = new SpellContext(0, 7);
        ulong mask = 0;
        ulong requiredMask = (1ul << 3) - 1;
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

        Assert.That(rounds, Is.EqualTo(1));           // 7 抽足够一轮跑完
        Assert.That(state.Damage, Is.EqualTo(22f));   // 10 + 7 + 5
        Assert.That(mask, Is.EqualTo(0x7));           // 三张卡都已消费
    }

    // ═══════════════════════════════════════════════════════
    // Section 8: JIT Consistency
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterp_SingleCard()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex("10|draw 1|idx:0 + 7|idx:1");
        var f = runner.Compile(r);

        var interp = runner.Instantiate(f, jit: false).Run();
        var jit    = runner.Instantiate(f, jit: true).Run();

        Assert.That(jit.Damage, Is.EqualTo(interp.Damage));
        Assert.That(jit.DrawsProvide, Is.EqualTo(interp.DrawsProvide));
        Assert.That(jit.ConsumedThisRound, Is.EqualTo(interp.ConsumedThisRound));
        Assert.That(jit.StartIndex, Is.EqualTo(interp.StartIndex));
    }

    [Test]
    public void Jit_MatchesInterp_WithVariable()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex("[prev] + 10|idx:0");
        var f = runner.Compile(r);
        var initial = new SpellContext(5, 3);

        var interp = runner.Instantiate(f, jit: false).Set("prev", initial).Run();
        var jit    = runner.Instantiate(f, jit: true).Set("prev", initial).Run();

        Assert.That(jit.Damage, Is.EqualTo(interp.Damage));
        Assert.That(jit.DrawsProvide, Is.EqualTo(interp.DrawsProvide));
    }

    [Test]
    public void Jit_ShortCircuitsOnDrawsExhausted()
    {
        var lexer  = CreateSpellLexer();
        var runner = new FluxAssembler<SpellContext, SpellDef>(SpellDefIns);
        var r = lexer.Lex("5|idx:0 + 100|idx:1");
        var f = runner.Compile(r);

        var interp = runner.Instantiate(f, jit: false).Run();
        var jit    = runner.Instantiate(f, jit: true).Run();

        Assert.That(jit.Damage, Is.EqualTo(interp.Damage));
        Assert.That(jit.DrawsProvide, Is.EqualTo(interp.DrawsProvide));
    }
}
