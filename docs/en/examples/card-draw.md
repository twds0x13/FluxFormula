# Example: Spell Context

A dual-field immediate syntax and chain modification model. The `float` field is a damage modifier (positive = increase, negative = decrease), the `int` field is draw provision. Each card automatically costs 1 draw to cast; cards are chained via Connect into a cast queue.

## Scenario

Noita-style spell correction system. Each spell card has two attributes:
- **Damage modifier**: float, positive = increase final damage, negative = decrease final damage
- **Draw provision**: int, how many draws this card provides. Casting automatically consumes 1 draw; net change = provision − 1

`10.5|0` is a pure damage correction spell: provides 0 draws, net −1 draw. `0|2` is a double cast: `1 + 2 - 1 = 2`, the next two casts are free. `-5|2` is a tradeoff draw spell: `1 + 2 - 1 = 2`, paying 5 damage for 2 free casts. `20|1` is a self-paying spell: `1 + 1 - 1 = 1`, providing exactly enough draws to offset its own cost.

**Spell wrapping** (Noita machine-gun wand): when the chain ends with DrawsLeft > 0, a while loop re-injects the output back to the chain head. The chain pass-through kicks in automatically when DrawsLeft reaches zero mid-execution.

## TData Struct

```csharp
public struct DrawState : IEquatable<DrawState>
{
    public float Damage;      // Accumulated damage
    public int DrawsLeft;     // Remaining draws

    public DrawState(float damage, int draws)
        => (Damage, DrawsLeft) = (damage, draws);

    public readonly bool Equals(DrawState other)
        => Damage == other.Damage && DrawsLeft == other.DrawsLeft;

    public override readonly string ToString()
        => $"({Damage:F1} dmg, {DrawsLeft} draws)";
}
```

`sizeof(DrawState) = 8` bytes (float + int), consuming 1 Instruction slot per Immediate.

## Operator Enum

```csharp
public enum DrawOp : byte
{
    Const, Add,
    LParen, RParen, Return,
}
```

`Add` operates on both Damage and DrawsLeft simultaneously. DrawsLeft automatically deducts the 1-draw casting cost.

## LiteralScanner: `float|int` Dual-Field Format

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out DrawState value) =>
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
    if (pos >= src.Length || src[pos] != '|') { value = new DrawState(damage, 0); return pos; }
    pos++;

    // Scan integer (draw provision)
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

Format rules:
- `10.5|0` — 10.5 damage, 0 draw provision (implicit −1 cost → net −1)
- `0|2` — no damage change, 2 draw provision: `1 + 2 - 1 = 2`
- `-5|2` — decrease 5 damage, 2 draw provision: `1 + 2 - 1 = 2`
- Omitting `|draws` defaults to 0

## Definition

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

The `- 1` in `Add` is the implicit casting cost. `b.DrawsLeft` is the card's draw provision; net change = provision − 1. Damage modifiers accumulate directly, unaffected by the cost.

## Lexer Configuration

```csharp
var config = new LexerConfig<DrawState>
{
    LiteralOper   = (byte)DrawOp.Const,
    LiteralParser = _ => default,
    LiteralScanner = /* see above */,
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

## Usage

```csharp
var def    = new DrawDef();
var runner = new FluxAssembler<DrawState, DrawDef>(def);
var lexer  = new FluxLexer<DrawState>(config);

// Build spell cards: Modifier form is "[prev] + card_face_value"
// Card 1: +10 damage modifier, 0 draws (only the casting cost)
// Card 2: +7 damage modifier,  0 draws
// Card 3: +5 damage modifier,  0 draws
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|0"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|0"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|0"));

// Chain all cards together
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// 7-draw initial state: triggers spell wrapping (Noita machine-gun wand)
DrawState state = runner.Instantiate(chain)
    .Set("prev", new DrawState(0, 7))
    .Run();
// → (22.5 dmg, 4 draws)  4 draws remain after chain

// Spell wrapping: re-inject output back to chain head while draws > 0
while (state.DrawsLeft > 0)
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
}
// Wrap 1: (22.5, 4) → (45.0, 1)  second pass draws all 3 cards
// Wrap 2: (45.0, 1) → (55.5, 0)  mid-chain draw exhaustion, card 1 only
// → (55.5 dmg, 0 draws)
```

## How It Works

### Connect Preserves Per-Card Independence

`Connect()` stores each card as an independent `Modifier` with its own `Instruction[]` in a `ChainLink[]` array. Physical bytecode merging is deferred to evaluation time and only triggered when the chain exceeds `MergeThreshold` (default: 8 links).

### Per-Link JIT Caching

Each `ChainLink`'s delegate is independently cached in `FormulaCache` by `DualHash64`. The first `Instantiate(chain)` compiles all links; subsequent instantiations hit the cache. When the same card appears in multiple decks, its delegate is reused across chains.

### Zero Recompilation on Spell Wrapping

`runner.Instantiate(chain).Set("prev", state).Run()` inside the wrap loop performs zero compilation. `Instantiate` is a `ref struct` stack allocation plus a cache-hit delegate lookup. Each wrap costs N function pointer calls (N = cards in chain), fully decoupled from the one-time compilation.

```csharp
// Compilation: exactly once, during the first Instantiate(chain)
var chain = mod1.ToModifier().Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// Wrapping: all delegate calls, zero compilation
while (state.DrawsLeft > 0)
{
    state = runner.Instantiate(chain).Set("prev", state).Run();
}
```

### R1 Bus Carries the Context

Each card's `[prev] + card_face_value` reads the previous card's output through the R1 register. `Set("prev", state)` injects the current context into the chain head's first Immediate slot; after evaluation, R1 carries the final `DrawState` back. The entire chain's context flow is R1 read-write chaining with zero heap allocation.

### Implicit Casting Cost

Each card automatically costs 1 draw. `10.5|0` reads as "provides 0 draws"; the net −1 is implicit. `0|2`: 1 remaining + 2 provision − 1 cost = 2 draws. Card face values stay as card face values — the casting cost is a constant, not part of the card data.

## Key Points

- `float|int` dual-field immediates are another application of LiteralScanner: `|` as field separator
- `Add` acts on both the damage modifier and draw provision simultaneously, with an implicit −1 casting cost
- Double cast (`0|2`) and tradeoff (`-5|2`) are natural consequences of the same `float|int` format
- Spell wrapping: a while loop re-injects chain output back to the chain head; pass-through engages when DrawsLeft reaches zero mid-execution
- Connect keeps each card independent; per-link delegates are cached in FormulaCache and reused across chains
- Wrapping 100 times = 100 delegate calls, not 100 compilations
