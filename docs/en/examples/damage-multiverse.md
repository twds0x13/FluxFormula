# Example: Crit Multiverse Simulation (Curry Forking)

The traditional approach to crit simulation re-injects all variables on every run: 1000 simulations = 3000 `Set` calls. Curry's fork capability changes this: bind the shared variables once, then fork a new instance for the one remaining variable each time.

This example defines a damage formula with a `select` ternary operator, uses `FluxCurryEvaluator` to bind attack and crit damage, then runs a multiverse simulation over the "is this a crit?" variable via custom `.Multiverse()` / `.MultiverseStats()` extension methods. `.Multiverse()` returns the arithmetic mean; `.MultiverseStats()` returns full aggregated statistics (Avg/Max/Min/Mid).

## Damage Formula

```csharp
[atk] * ([isCrit] ? 1 + [critDmg] : 1)
```

- On crit: `atk * (1 + critDmg)`
- No crit: `atk * 1`

`select` supports both function syntax `select(a, b, c)` and ternary syntax `a ? b : c`.

## Definition

`DamageDef` implements `IFluxExprDefinition<float>`, providing the four basic arithmetic operators plus the ternary `Select`. `Select` has `GetArity` returning 3 (condition + true-branch + false-branch), and `Compute` uses `regs[Arg0] != 0f ? regs[Arg1] : regs[Arg2]`.

Full source: `examples/DamageMultiverse/DamageDef.cs`.

## PCG64 Reproducible RNG

`Pcg64(ulong seed)` provides a deterministic random sequence — the same seed always produces the same sequence. `NextFloat()` returns values in `[0, 1)`.

## Curry Binding

```csharp
var formula = runner.Compile(lexer.Lex(
    "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));

var baseState = FluxCurryEvaluator<float, DamageDef>.Create(def, formula)
    .Bind("atk", 100f)
    .Bind("critDmg", 0.5f);
// baseState: atk=100, critDmg=0.5, isCrit unbound (2/3)
```

## Multiverse Extension Methods

`.Multiverse()` is an extension method on `FluxCurryEvaluator<float, TDef>` that returns `float`. Three overloads:

### Simple Threshold

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000, critRate: 0.3f, rng);
// Each iteration: rng.NextFloat() < 0.3 → crit
```

### Delegate Predicate

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000, rng =>
{
    counter++;
    return counter % 3 == 0; // every third hit crits
}, rng);
```

### External FluxFormula Judge

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000,
    judgeAssembler, judgeFormula, rng);
// Each iteration injects the random float into the judge formula; result > 0.5 means crit
```

## MultiverseStats Extension Methods

`.MultiverseStats()` returns a `MultiverseStats` struct (`Avg` / `Max` / `Min` / `Mid`). The same three overloads mirror `.Multiverse()`:

```csharp
// Threshold
MultiverseStats stats = baseState.MultiverseStats("isCrit", 10000, 0.3f, rng);

// Delegate
MultiverseStats stats = baseState.MultiverseStats("isCrit", 10000, rng => counter++ % 3 == 0, rng);

// External judge
MultiverseStats stats = baseState.MultiverseStats("isCrit", 10000,
    judgeAssembler, judgeFormula, rng);
```

All overloads share the same implementation pattern: cache the curry → loop fork → bind isCrit → `ForceComplete()` → read Result → collect statistics.

## Post-Processing with TrySet

Simulation results can be further refined through a post-processing formula. The formula is configuration-driven (`[Max] - [Avg]` for volatility, `[Avg] * (1 + [Max] / [Min])` for extreme ratio), and the injection code does not need to know the formula's signature in advance:

```csharp
// Simulation phase
var stats = baseState.MultiverseStats("isCrit", 10000, 0.3f, rng);

// Post-processing: TrySet injects all statistics, the formula decides which to use
float final = postProcessRunner.Instantiate(postProcessFormula)
    .TrySet("Avg", stats.Avg)
    .TrySet("Max", stats.Max)
    .TrySet("Min", stats.Min)
    .TrySet("Mid", stats.Mid)
    .Run();
```

`TrySet` silently skips variables that do not exist, so the post-processing formula can declare only `[Avg]` or `[Max] - [Min]`, and the injection code stays the same.

## Comparison with the Traditional Approach

**Traditional** (full re-injection every run):
```csharp
for (int i = 0; i < 10000; i++)
{
    bool crit = rng.NextFloat() < 0.3f;
    float dmg = runner.Instantiate(formula, jit: true)
        .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", crit ? 1f : 0f)
        .Run();
    sum += dmg;
}
```

**Curry + Multiverse**:
```csharp
var baseState = FluxCurryEvaluator<float, DamageDef>.Create(def, formula)
    .Bind("atk", 100f).Bind("critDmg", 0.5f);
float avg = baseState.Multiverse("isCrit", 10000, 0.3f, rng);
```

Each iteration binds 1 variable instead of 3. `baseState` remains unchanged across all simulations and can be reused.

## Notes

- `.Multiverse()` returns `float` (mean only); `.MultiverseStats()` returns `MultiverseStats` (Avg/Max/Min/Mid)
- Results do **not** go through `.Result` — methods return aggregated values directly
- The original curry instance is **unaffected** after Multiverse runs (`readonly struct` + per-fork new instances)
- PCG64 reproducibility means "30% crit rate" produces identical results across two runs
