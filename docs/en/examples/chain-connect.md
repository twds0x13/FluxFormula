# Example: ChainLink

ChainLink is an `internal` struct. Users interact with it indirectly through `Connect()`, `ToAtomic()`, and `ToMultiplier()`.

## Basic Chain Connect

`Connect()` requires the second argument to be a Modifier. Passing a Formula directly throws `ArgumentException`. Call `.ToMultiplier()` first to strip the first operand:

```csharp
using FluxFormula.Core;

var lexer = CreateMathLexer();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);

var fA = runner.Compile(lexer.Lex("10 + 5"));                // = 15
var fB = runner.Compile(lexer.Lex("2 * 3"));                 // Formula (has first operand 2)

// ❌ Error: fB is a Formula — its first operand would be silently overwritten
// var chain = fA.Connect(fB);  // throws ArgumentException

// ✅ Correct: convert fB to Modifier first (strip operand 2, read from R1 instead)
var chain = fA.Connect(fB.ToMultiplier());  // (10+5) * 3 = 45

var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // 45
```

## Modifier Chain: Passing Output Forward

`ToMultiplier()` converts a Formula to a Modifier — removes the first operand, replacing it with R1 bus input:

```csharp
var fBase = runner.Compile(lexer.Lex("1 + 2"));     // = 3
var fMod  = runner.Compile(lexer.Lex("2 * 3"));     // = 6

// fMod.ToMultiplier() turns "2*3" into "R1*3" (a multiply-by-3 modifier)
var chainMod = fBase.Connect(fMod.ToMultiplier());

var inst2 = runner.Instantiate(chainMod);
Console.WriteLine(inst2.Run()); // 9 = (1+2) * 3
```

Multiple modifiers in sequence:

```csharp
var current = runner.Compile(lexer.Lex("1 + 2")); // = 3

// Chain 3 multiply-by-2 modifiers
for (int i = 0; i < 3; i++)
    current = current.Connect(
        runner.Compile(lexer.Lex("3 * 2")).ToMultiplier());
// Semantics: ((3 * 2) * 2) * 2 = 24

var inst3 = runner.Instantiate(current);
Console.WriteLine(inst3.Run()); // 24
```

## Chain vs Atomic: ToAtomic

A chain formula can be merged to a regular atomic formula via `ToAtomic()`:

```csharp
var chain = fA.Connect(fB.ToMultiplier());

// Both paths produce the same result
float perLinkResult = runner.Instantiate(chain).Run();
float atomicResult  = runner.Instantiate(chain.ToAtomic()).Run();

Console.WriteLine(perLinkResult == atomicResult); // True
```

`ToAtomic()` concatenates all link bytecodes into a single `Instruction[]`. It is called automatically for JIT compilation and for chains exceeding the length threshold (>8).

## Auto-Merge on Long Chains

When chain length exceeds `ChainReserved.MergeThreshold` (8), `Instantiate` automatically calls `ToAtomic`:

```csharp
var current = runner.Compile(lexer.Lex("1 + 1"));
for (int i = 0; i < 10; i++)
    current = current.Connect(
        runner.Compile(lexer.Lex("2 * 1")).ToMultiplier());

// chain.ChainLength = 11 (exceeds 8)
// Instantiate auto-merges rather than doing 11 per-link calls
var inst = runner.Instantiate(current);
Console.WriteLine(inst.Run()); // evaluates correctly
```

Users don't need to worry about the threshold — `Instantiate` picks the optimal path automatically.

## ToMultiplier / ToFormula Round-Trip

Formula ↔ Modifier conversion preserves evaluation equivalence:

```csharp
var f = runner.Compile(lexer.Lex("7 + 3")); // = 10

// Formula → Modifier → Formula (round-trip)
var mod      = f.ToMultiplier();
var restored = mod.ToFormula("input");

// Inject original value via the new variable name
var inst = runner.Instantiate(restored).Set("input", 7f);
Console.WriteLine(inst.Run()); // 10 (equivalent to f)
```

## Notes

- `ChainReserved.InternalPrefix` (`"CHAIN_LINK_INTERNAL_"`) is reserved for chain evaluation internal variables. User-declared variables must not use this prefix
- Modifier formulas (`FluxType.Modifier`) cannot `Run()` standalone — they must appear as non-first links in a chain
- `Connect` requires the second argument to be a Modifier. Passing a Formula throws `ArgumentException`, instructing the user to call `.ToMultiplier()` first
- `IsChained`, `ChainLength`, and `ChainLink` are all `internal` — users never need to know whether a formula is internally chain-represented
