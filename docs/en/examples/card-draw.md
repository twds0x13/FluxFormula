# Example: Spell Context

A named-field immediate syntax with chain modification and spell-wrap tracking. The `float` field is a damage modifier (positive = increase, negative = decrease), the `byte` field is draw provision. Each card automatically costs 1 draw to cast; cards are chained via Connect into a cast queue with Noita-style consumed-card mask tracking via SpellTracker.

## Scenario

Noita-style spell correction system. Each spell card has two attributes:
- **Damage modifier**: float, positive = increase final damage, negative = decrease final damage
- **Draw provision**: int, how many draws this card provides. Casting automatically consumes 1 draw; net change = provision − 1

`10.5|idx:0` is a pure damage correction spell: provides 0 draws, card index 0. `0|draw 2|idx:1` is a double cast: `1 + 2 - 1 = 2`, the next two casts are free, card index 1. `-5|draw 2|idx:2` is a tradeoff draw spell: `1 + 2 - 1 = 2`, paying 5 damage for 2 free casts, card index 2. `20|draw 1|idx:3` is a self-paying spell: `1 + 1 - 1 = 1`, providing exactly enough draws to offset its own cost.

**Spell wrapping** (Noita machine-gun wand): the chain formula runs all cards, then the tracker formula updates the bitmask using `ConsumedThisRound`. The while loop wraps back to the chain head while the mask isn't full; terminates when full. `DrawsProvide` pass-through engages mid-chain when draws run out; each card executes at most twice (Noita double utilization).

## TData Struct

```csharp
public struct SpellContext : IEquatable<SpellContext>
{
    public float Damage;              // Accumulated damage
    public byte DrawsProvide;         // Remaining draws
    public byte ConsumedThisRound;    // Cards consumed this round
    public byte StartIndex;           // Starting card position this round
    byte _padding;                    // Reserved

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

`sizeof(SpellContext) = 8` bytes (float + byte + byte + byte + 1 padding), consuming 1 Instruction slot per Immediate.

## Operator Enum

```csharp
public enum SpellOp : byte
{
    Const, Add,
    LParen, RParen, Return,
}
```

`Add` operates on both Damage and DrawsProvide, implicitly deducting the 1-draw casting cost.

## LiteralScanner: `damage|draw N|idx:N` Named-Field Format

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out SpellContext value) =>
{
    value = default;
    if (pos >= src.Length || !(char.IsDigit(src[pos]) || src[pos] == '-')) return pos;

    // Scan float (Damage)
    int start = pos;
    if (src[pos] == '-') pos++;
    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    if (pos < src.Length && src[pos] == '.')
    {
        pos++;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
    }
    float damage = float.Parse(src.Slice(start, pos - start));

    // Expect '|' separator
    if (pos >= src.Length || src[pos] != '|') { value = new SpellContext(damage, 0); return pos; }
    pos++;

    // Optional 'draw' field
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

        // Consume '|' separator
        if (pos < src.Length && src[pos] == '|') pos++;
    }

    // Required 'idx:' field
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

Format rules:
- `10.5|idx:0` — 10.5 damage, 0 draw provision, card index 0
- `0|draw 2|idx:1` — no damage change, 2 draw provision: `1 + 2 - 1 = 2`, card index 1
- `-5|draw 2|idx:2` — decrease 5 damage, 2 draw provision: `1 + 2 - 1 = 2`, card index 2
- `draw` field is optional (defaults to 0), `idx:` field is required

## Definition

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
    public OperandPosition GetFirstPosition(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => OperandPosition.Left,
        _           => OperandPosition.Right,
    };

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public SpellContext Compute(byte op, Instruction inst, Span<SpellContext> regs)
    {
        SpellContext a = regs[inst.Arg0];
        SpellContext b = regs[inst.Arg1];
        return ((SpellOp)op) switch
        {
            SpellOp.Add => EvaluateAdd(a, b, regs),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((SpellOp)op) switch
        {
            SpellOp.Add => Expression.Call(
                typeof(SpellDef).GetMethod(nameof(EvaluateAddJit),
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic)!,
                regs[inst.Arg0], regs[inst.Arg1], regs[Registers.Error]),
            _ => Expression.Constant(default(SpellContext)),
        };
    }

    // Interpreter path: writes R0 when draws exhausted → framework short-circuits
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SpellContext EvaluateAdd(SpellContext a, SpellContext b, Span<SpellContext> regs)
    {
        if (b.StartIndex < a.StartIndex)
            return a;
        if (a.DrawsProvide <= 0)
        {
            regs[Registers.Error] = a;
            return default;
        }
        return new SpellContext(
            a.Damage + b.Damage,
            a.DrawsProvide + b.DrawsProvide - 1,
            a.ConsumedThisRound + 1,
            a.StartIndex);
    }

    // JIT path: writes R0 via ref parameter — Expression tree compiler maps to register write
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SpellContext EvaluateAddJit(SpellContext a, SpellContext b, ref SpellContext r0)
    {
        if (b.StartIndex < a.StartIndex)
            return a;
        if (a.DrawsProvide <= 0)
        {
            r0 = a;
            return default;
        }
        return new SpellContext(
            a.Damage + b.Damage,
            a.DrawsProvide + b.DrawsProvide - 1,
            a.ConsumedThisRound + 1,
            a.StartIndex);
    }
}
```

`EvaluateAdd` and `EvaluateAddJit` implement the Add semantic for the interpreter and JIT paths respectively. Both share the same branching logic (positional skip, draw exhaustion, normal accumulation); they differ only in the R0 write mechanism. The interpreter writes `regs[Registers.Error]` directly via `Span<SpellContext>`, while the JIT path writes through a `ref SpellContext r0` parameter bound to `regs[Registers.Error]` in the expression tree. After the compiled delegate writes R0, the evaluator framework detects a non-default R0 and returns immediately — equivalent to the interpreter's `IsDefault(&regsPtr[Error])` check.

`Add` has two branches: `b.StartIndex < a.StartIndex` (card already consumed in a previous round) → pass-through skip, execution continues to the next card; `a.DrawsProvide <= 0` (draws exhausted) → writes R0 error register, the evaluator framework immediately interrupts the entire chain, subsequent cards do not execute. On normal execution `ConsumedThisRound` increments per card, `StartIndex` passes through unchanged, damage modifiers accumulate directly.

## Tracker Struct: SpellTracker

```csharp
public struct SpellTracker : IEquatable<SpellTracker>
{
    public SpellContext Context;      // Chain formula output
    public ulong ConsumedMask;        // Consumed bitmask
    public ulong RequiredMask;        // Termination mask = (1 << (maxIndex + 1)) - 1

    public SpellTracker(SpellContext context, ulong mask, ulong requiredMask)
        => (Context, ConsumedMask, RequiredMask) = (context, mask, requiredMask);

    public readonly bool Equals(SpellTracker other)
        => Context.Equals(other.Context) && ConsumedMask == other.ConsumedMask
                                         && RequiredMask == other.RequiredMask;

    public override readonly string ToString()
        => $"(mask: 0x{ConsumedMask:X}, req: 0x{RequiredMask:X}, {Context})";
}
```

`SpellTracker` is separated from `SpellContext` — the chain formula's hot path never touches the mask.

## Tracker Operators

```csharp
public enum TrackerOp : byte
{
    Const, Track, Return,
}
```

`Track` is a unary operator: it receives `[prev]` (the previous round's output), updates `ConsumedMask`, and checks the termination condition.

## Tracker Definition

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
    public OperandPosition GetFirstPosition(byte op) => OperandPosition.Left;

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public SpellTracker Compute(byte op, Instruction inst, Span<SpellTracker> regs)
    {
        SpellTracker t = regs[inst.Arg0];
        if ((t.ConsumedMask & t.RequiredMask) == t.RequiredMask)
            return t;  // All cards consumed — terminate

        byte consumed = t.Context.ConsumedThisRound;
        if (consumed <= 0)
            return t;  // No consumption this round — pass through

        // Find first zero bit = this round's starting card position
        int pos = BitOperations.TrailingZeroCount(~t.ConsumedMask);

        ulong mask = t.ConsumedMask | (((1ul << consumed) - 1) << pos);

        var ctx = t.Context;
        ctx.ConsumedThisRound = 0;
        ctx.StartIndex = (byte)(pos + consumed);  // Next round resumes here
        return new SpellTracker(ctx, mask, t.RequiredMask);
    }
}
```

`Track` logic:

1. **Mask equals RequiredMask** → return current value (while loop terminates)
2. **No consumption** → return current value (`ConsumedThisRound == 0`)
3. **Normal execution**: locates the first zero bit as the round's starting position, then sets `consumed` consecutive bits from that position. `ConsumedThisRound` resets to zero, `StartIndex` is set to `pos + consumed` so the next round resumes from there.

`RequiredMask = (1 << (maxIndex + 1)) - 1`, computed by the C# layer from the chain's maximum card index and carried across rounds in `SpellTracker`. The chain formula executes all cards, then the tracker formula batch-updates the mask.

## Lexer Configuration

```csharp
var config = new LexerConfig<SpellContext>
{
    LiteralOper   = (byte)SpellOp.Const,
    LiteralScanner = /* see above */,
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

## Usage

```csharp
var def    = new SpellDef();
var runner = new FluxAssembler<SpellContext, SpellDef>(def);
var lexer  = new FluxLexer<SpellContext>(config);

// Build spell cards: Modifier form is "[prev] + card_face_value"
// Card 1: +10 damage modifier, 0 draws (only the casting cost)
// Card 2: +7 damage modifier,  0 draws
// Card 3: +5 damage modifier,  0 draws
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|idx:1"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|idx:2"));

// Chain all cards together
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// Tracker formula: Track [prev]
var trackerDef    = new TrackerDef();
var trackerConfig = new LexerConfig<SpellTracker>
{
    LiteralOper = (byte)TrackerOp.Const,
    LiteralScanner = LexerConfig<SpellTracker>.CreateDefaultNumberScanner(_ => default),
    Operators = { new("Track", (byte)TrackerOp.Track) { Arity = 1 } },
};
var trackerLexer = new FluxLexer<SpellTracker>(trackerConfig);
var tracker      = new FluxAssembler<SpellTracker, TrackerDef>(trackerDef);
var trackFormula = tracker.Compile(trackerLexer.Lex("Track [prev]"));

// Initial state: 7 draws + empty mask + termination mask (3 cards → 0b111)
SpellContext state = new SpellContext(0, 7, startIndex: 0);
ulong mask = 0;
ulong requiredMask = (1ul << 3) - 1;  // Cards at indices 0/1/2 → low 3 bits all 1

// Noita spell wrapping: chain → tracker, alternating
do
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
    var tracked = tracker.Instantiate(trackFormula)
        .Set("prev", new SpellTracker(state, mask, requiredMask))
        .Run();
    state = tracked.Context;
    mask  = tracked.ConsumedMask;
} while ((mask & requiredMask) != requiredMask);

// First shot: mask=0 → chain runs 3 cards (cards 0/1/2 consumed), mask = 0b111
// Equals requiredMask → terminate
// → Each card executed twice (Noita double utilization): chain runs first, mask updates after
```

## How It Works

### Connect Preserves Per-Card Independence

`Connect()` stores each card as an independent `Modifier` with its own `Instruction[]` in a `ChainLink[]` array. Physical bytecode merging is deferred to evaluation time and only triggered when the chain exceeds `MergeThreshold` (default: 8 links).

### Per-Link JIT Caching

Each `ChainLink`'s delegate is independently cached in `FormulaCache` by `DualHash64`. The first `Instantiate(chain)` compiles all links; subsequent instantiations hit the cache. When the same card appears in multiple decks, its delegate is reused across chains.

### Tracker Formula & Mask Management

The chain formula's `Compute(Add)` increments `ConsumedThisRound` per card. After the chain runs, the tracker formula's `Track` operator reads that value and updates the mask in three steps:

1. **Locate starting bit**: first 1-bit of `~ConsumedMask` = this round's starting card position
2. **Sequential bit-set**: sets `consumed` consecutive bits from that position
3. **Reset + update**: `ConsumedThisRound = 0`, `StartIndex = pos + consumed`, ready for the next round

The mask fills monotonically. Reaching `RequiredMask` terminates the loop.

If draws are insufficient to complete the chain in one round (e.g. 5 cards, 1 draw), only card 0 executes before `EvaluateAdd` detects `DrawsProvide <= 0` on card 1 and writes R0, causing the evaluator to immediately interrupt the chain. The tracker reads `ConsumedThisRound = 1`, updates the mask, and sets `StartIndex = 0 + 1 = 1`. When the wand recharges with fresh draws next round, the chain resumes from card 1: card 0 hits `b.StartIndex < a.StartIndex` and passes through, card 1 executes normally.

### No Compilation Inside the Wrap Loop

Both `Run()` calls inside the wrap loop use precompiled delegates. `Instantiate` is a lightweight `ref struct` stack allocation plus a cache-hit delegate lookup. The chain formula and tracker formula each compile once; the loop contains no compilation.

```csharp
// Compilation: once each for chain and tracker
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// Wrapping: all delegate calls, zero compilation
do
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
    var tracked = tracker.Instantiate(trackFormula)
        .Set("prev", new SpellTracker(state, mask))
        .Run();
    state = tracked.Context;
    mask  = tracked.ConsumedMask;
} while ((mask & requiredMask) != requiredMask);
```

### R1 Bus Carries the Context

Each card's `[prev] + card_face_value` reads the previous card's output through the R1 register. `Set("prev", state)` injects the current context into the chain head's first Immediate slot; after evaluation, R1 carries the final `SpellContext` back. The entire chain's context flow is R1 read-write chaining with zero heap allocation.

### Implicit Casting Cost

Each card automatically costs 1 draw. `10.5|idx:0` provides 0 draws; the net −1 is implicit. `0|draw 2|idx:1`: 1 remaining + 2 provision − 1 cost = 2 draws. The 1-draw deduction is handled uniformly by the `Add` operator; card data only declares the damage modifier and draw provision.

## Key Points

- `damage|draw N|idx:N` named-field format: `draw` is optional (defaults to 0), `idx:` is required
- `Add` acts on both the damage modifier and draw provision; `a.DrawsProvide <= 0` writes R0 to interrupt the chain, `b.StartIndex < a.StartIndex` passes through to skip consumed cards
- `ConsumedThisRound` increments inside the chain, consumed in batch by the tracker formula then reset
- `StartIndex` is updated by the tracker to `pos + consumed`, so the next round resumes from the unconsumed position
- Double cast (`0|draw 2`) and tradeoff (`-5|draw 2`) are natural consequences of the same format
- Spell wrapping: chain → tracker alternation; mask fills monotonically until reaching RequiredMask
- `SpellTracker` is separated from `SpellContext` — hot path never touches the `ulong` mask
- Connect keeps each card independent; per-link delegates are cached in FormulaCache and reused across chains
- `RequiredMask = (1 << (maxIndex + 1)) - 1`: computed from the chain's max card index, carried across rounds in `SpellTracker` as the termination condition
- Wrapping 100 times = 200 delegate calls (chain + tracker), not 100 compilations
