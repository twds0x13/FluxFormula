using System;
using System.Globalization;
using FluxFormula.Core;

// ── 1. Configure Lexer ──
var config = new LexerConfig<float>
{
    LiteralOper   = (byte)DamageOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)DamageOp.Add, slots: new sbyte[] { -1, +1 }),
        new("-", (byte)DamageOp.Sub, slots: new sbyte[] { -1, +1 }),
        new("*", (byte)DamageOp.Mul, slots: new sbyte[] { -1, +1 }),
        new("/", (byte)DamageOp.Div, slots: new sbyte[] { -1, +1 }),
        new("?", (byte)DamageOp.Question),
        new(":", (byte)DamageOp.Colon),
        new(",", (byte)DamageOp.Comma),
        new("select", (byte)DamageOp.Select,
            slots: new sbyte[] { +2, +4, +6 }, bracketOpen: "(", bracketClose: ")",
            aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ","), new(+7, ")") }),
    },
    Brackets =
    {
        new("(", ")", (byte)DamageOp.LParen, (byte)DamageOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var def    = default(DamageDef);
var runner = new FluxAssembler<float, DamageDef>(def);
var lexer  = new FluxLexer<float>(config);

// ── 2. Compile damage formula ──
// [atk] * ([isCrit] ? 1 + [critDmg] : 1)
// Crit → atk * (1 + critDmg).  No crit → atk * 1 = atk.
var formula = runner.Compile(lexer.Lex(
    "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));

// ── 3. Curry: bind known stats, leave isCrit for multiverse forking ──
var baseState = FluxCurryEvaluator<float, DamageDef>.Create(def, formula)
    .Bind("atk", 100f)
    .Bind("critDmg", 0.5f);
// baseState: atk=100, critDmg=0.5, isCrit unbound
Console.WriteLine($"Bound: {baseState.BoundCount}/{baseState.VariableCount}");

// ── 4. Multiverse simulations ──
var rng = new Pcg64(42);

// 4a. Simple threshold: 30% crit rate, 10000 runs
float avgA = baseState.Multiverse("isCrit", count: 10000, critRate: 0.3f, rng);
Console.WriteLine($"Threshold (30% crit, n=10000): {avgA:F2}  "
    + $"(expected ~115: 100*(1+0.5)*0.3 + 100*0.7)");

// 4b. Delegate predicate: every 3rd hit crits
var rng2 = new Pcg64(99);
int counter = 0;
float avgB = baseState.Multiverse("isCrit", count: 10000, rng =>
{
    counter++;
    return counter % 3 == 0;
}, rng2);
Console.WriteLine($"Delegate (every 3rd, n=10000): {avgB:F2}  "
    + $"(expected ~116.67: 100*(1+0.5)/3 + 100*2/3)");

// ── 5. Traditional full-Set comparison ──
var rng3 = new Pcg64(42);
float tradSum = 0f;
for (int i = 0; i < 10000; i++)
{
    bool crit = rng3.NextFloat() < 0.3f;
    float dmg = runner.Instantiate(formula, jit: true)
        .Set("atk", 100f).Set("critDmg", 0.5f)
        .Set("isCrit", crit ? 1f : 0f)
        .Run();
    tradSum += dmg;
}
Console.WriteLine($"Traditional full-Set (n=10000): {tradSum / 10000:F2}  "
    + $"(same seed, should match threshold)");

// ── 6. PCG64 reproducibility ──
var rngA = new Pcg64(123);
var rngB = new Pcg64(123);
bool match = true;
for (int i = 0; i < 100; i++)
    if (rngA.NextFloat() != rngB.NextFloat()) { match = false; break; }
Console.WriteLine($"PCG64 reproducibility: {(match ? "PASS" : "FAIL")}");
