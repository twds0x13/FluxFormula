# Example: Card Draw Stack

A dual-field immediate syntax and chain pass-through model — each spell card carries both a damage value and a draw cost, chained via Connect into a draw queue.

## Scenario

Noita-style spell drawing: each card has a damage value and a draw cost. `5.5|-1` means 5.5 damage and consume 1 draw. Multiple cards are chained into a queue; the chain naturally terminates when draws run out. An external loop reads the remaining draws from the return value to decide whether to keep drawing.

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

`Add` operates on both Damage and DrawsLeft simultaneously. Negative literals (e.g., `-5|2`) encode all card face semantics.

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

    // Scan integer (DrawsLeft)
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
- `10.5|-1` — full format: 10.5 damage, consume 1 draw
- `10.5` — omit `|draws`, DrawsLeft defaults to 0
- `-5|2` — negative damage, gain 2 draws
- Missing digits after `|` → DrawsLeft = 0

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
            DrawOp.Add => new DrawState(a.Damage + b.Damage, a.DrawsLeft + b.DrawsLeft),
            _ => default,
        };
    }
}
```

`Add` accumulates both damage and draws. Negative literals encode all decrement semantics: `-5|2` means 5 penalty and 2 draw bonus, `10.5|-1` means 10.5 damage consuming 1 draw.

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

// Build deck: each card is a Modifier of "[prev] + face_value - face_cost"
// Card 1: +10 dmg, -1 draw
// Card 2: +7 dmg,  -1 draw
// Card 3: +5 dmg,  -1 draw
var mod1 = runner.Compile(lexer.Lex("[prev] + 10|-1"));
var mod2 = runner.Compile(lexer.Lex("[prev] + 7|-1"));
var mod3 = runner.Compile(lexer.Lex("[prev] + 5|-1"));

// Initial state: 0 damage, 3 draws
var deck = runner.Compile(lexer.Lex("0|3"));
var chain = deck.ToModifier().Connect(mod1.ToModifier())
               .Connect(mod2.ToModifier()).Connect(mod3.ToModifier());

// Draw all cards at once
DrawState result = runner.Instantiate(chain).Run();
// → (22.0 dmg, 0 draws) = 10 + 7 + 5 = 22 damage, 3 - 1 - 1 - 1 = 0 draws

// External loop for one-by-one drawing
DrawState state = new(0, 3);
var cards = new[] { mod1, mod2, mod3 };
foreach (var card in cards)
{
    if (state.DrawsLeft <= 0) break;
    state = runner.Instantiate(card).Set("prev", state).Run();
}
// → Each card runs independently; stops when draws are exhausted
```

## Key Points

- `float|int` dual-field immediates are another application of LiteralScanner: `|` as a field separator replaces the `:tag` label pattern
- `Add` acts on both Damage and DrawsLeft simultaneously; negative literals replace all other operations
- When DrawsLeft reaches 0, subsequent cards in the chain still evaluate, but the external loop stops by checking `state.DrawsLeft <= 0`
- `DrawState` carries two dimensions of information; Connect passes the full state through the R1 bus
