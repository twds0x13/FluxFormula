# Example: Game Damage Formula

A typical RPG damage calculation — akin to the spell modifier stacking in Noita — demonstrating multi-variable expressions, nested operators, and implicit multiplication.

## Scenario

```
final damage = (base attack × skill multiplier + bonus) × (1 + crit damage) × defense factor
```

Expression: `([atk] * [skill] + [bonus]) * (1 + [crit]) * [def_factor]`

## Operator Enum

```csharp
public enum DamageOp : byte
{
    Const, Add, Sub, Mul, Div,
    LParen, RParen, Return,
}
```

## Definition

```csharp
public readonly struct DamageDef : IFluxJITDefinition<float, DamageOp>
{
    public DamageOp GetReturnOp() => DamageOp.Return;

    public int GetArity(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Add => 2, DamageOp.Sub => 2,
        DamageOp.Mul => 2, DamageOp.Div => 2,
        _ => 0,
    };

    public OpType GetKind(byte op) => ((DamageOp)op) switch
    {
        DamageOp.Const  => OpType.Immediate,
        DamageOp.Return => OpType.Return,
        _               => OpType.Instruction,
    };

    public int GetPrecedence(DamageOp op) => op switch
    {
        DamageOp.Add => 1, DamageOp.Sub => 1,
        DamageOp.Mul => 2, DamageOp.Div => 2,
        _            => 0,
    };

    public OpPair<DamageOp> GetPair(DamageOp op) => op switch
    {
        DamageOp.LParen => new OpPair<DamageOp> { PairRole = Pair.Left },
        DamageOp.RParen => new OpPair<DamageOp>
        {
            PairRole   = Pair.Right,
            TargetLeft = DamageOp.LParen,
        },
        _ => new OpPair<DamageOp> { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(DamageOp op) => Associativity.Left;

    public DamageOp ResolveToken(DamageOp op, TokenContext ctx) => op;

    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return ((DamageOp)op) switch
        {
            DamageOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            DamageOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            DamageOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            DamageOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            _ => 0f,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((DamageOp)op) switch
        {
            DamageOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            DamageOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            _ => Expression.Constant(0f),
        };
    }
}
```

## Usage

```csharp
var config = new LexerConfig<float, DamageOp>
{
    LiteralOper    = DamageOp.Const,
    LiteralParser  = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators      = { new("+", DamageOp.Add), new("-", DamageOp.Sub),
                       new("*", DamageOp.Mul), new("/", DamageOp.Div) },
    Brackets       = { new("(", ")", DamageOp.LParen, DamageOp.RParen) },
    VariablePatterns = { new("[", "]") },
    ImplicitOperators = { DamageOp.Mul },
};

var def    = new DamageDef();
var runner = new FluxAssembler<float, DamageOp, DamageDef>(def);
var lexer  = new FluxLexer<float, DamageOp>(config);

// Compile once, evaluate repeatedly
var formula = runner.Compile(lexer.Lex(
    "([atk] * [skill] + [bonus]) * (1 + [crit]) * [def_factor]"));

// Critical hit
float critDmg = runner.Instantiate(formula)
    .Set("atk", 250f)
    .Set("skill", 1.8f)
    .Set("bonus", 50f)
    .Set("crit", 1.5f)       // +150% crit damage
    .Set("def_factor", 0.7f) // 70% after enemy mitigation
    .Run();
// (250 * 1.8 + 50) * 2.5 * 0.7 = 875

// Normal hit (crit = 0)
float normalDmg = runner.Instantiate(formula)
    .Set("atk", 250f)
    .Set("skill", 1.8f)
    .Set("bonus", 50f)
    .Set("crit", 0f)
    .Set("def_factor", 0.7f)
    .Run();
// (250 * 1.8 + 50) * 1.0 * 0.7 = 350
```

## Key Points

- **Implicit multiplication**: `2[atk]` can be enabled via `ImplicitOperators` for shorthand notation
- **Variable reuse**: Instantiate the same Formula multiple times with different values — ideal for batch damage calculation
- **Performance**: Compile once (~100 ns + ~500 B), then ~20 ns per evaluation (interpreter) or ~3 ns (JIT)
