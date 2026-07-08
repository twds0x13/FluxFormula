# Example: Chain Connect

The following examples demonstrate the chaining behavior of `Connect()`. ChainLink is a public struct — regular users interact with it indirectly via `FluxFormula` / `FluxModifier` methods; advanced users can read the chain structure via `FluxChain.GetLinks()` and persist it using `VffFormat.ToBytes()`.

> **v3.0.0**: `Connect()`'s type signature upgraded from runtime checking to compile-time guarantee — the parameter is `FluxModifier<TData, TDef>`; passing a `FluxFormula` won't compile. `ToMultiplier()` renamed to `ToModifier()` (old name retained as `[Obsolete]`).

## Basic Chain Connect

`Connect()`'s type signature only accepts `FluxModifier`. Call `.ToModifier()` first to strip the first operand:

```csharp
using FluxFormula.Core;

var lexer = CreateMathLexer();
var runner = new FluxAssembler<float, MathDef>(Def);

var fA = runner.Compile(lexer.Lex("10 + 5"));                // Formula (has first operand 10)
var fB = runner.Compile(lexer.Lex("2 * 3"));                 // Formula (has first operand 2)

// ❌ Compile error: Connect only accepts FluxModifier, FluxFormula won't type-check
// var chain = fA.Connect(fB);  // CS1503: cannot convert FluxFormula to FluxModifier

// ✅ Correct: convert fB to Modifier first (strip first operand 2, read from R1 bus)
var chain = fA.Connect(fB.ToModifier());  // (10+5) * 3 = 45

var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // 45
```

## Modifier Chains: previous link output feeds the next

`ToModifier()` converts a Formula to a Modifier — removes the first operand, replacing it with R1 bus input:

```csharp
var fBase = runner.Compile(lexer.Lex("1 + 2"));     // = 3
var fMod  = runner.Compile(lexer.Lex("2 * 3"));     // = 6

// fMod.ToModifier() turns "2*3" into "R1*3" (a multiply-by-3 modifier)
var chainMod = fBase.Connect(fMod.ToModifier());

var inst2 = runner.Instantiate(chainMod);
Console.WriteLine(inst2.Run()); // 9 = (1+2) * 3
```

Chaining multiple modifiers:

```csharp
var base = runner.Compile(lexer.Lex("1 + 2")); // FluxFormula
var chain = base.Connect(
    runner.Compile(lexer.Lex("3 * 2")).ToModifier()); // FluxChain

// Chain 2 more multiply-by-2 modifiers
for (int i = 0; i < 2; i++)
    chain = chain.Connect(
        runner.Compile(lexer.Lex("3 * 2")).ToModifier());
// Semantics: ((3 * 2) * 2) * 2 = 24

var inst3 = runner.Instantiate(chain);
Console.WriteLine(inst3.Run()); // 24
```

## Chain vs Atomic: ToAtomic

A chain formula can be merged into a plain atomic formula via `ToAtomic()`:

```csharp
var chain = fA.Connect(fB.ToModifier());

// Both paths produce the same result
float perLinkResult = runner.Instantiate(chain).Run();
float atomicResult  = runner.Instantiate(chain.ToAtomic()).Run();

Console.WriteLine(perLinkResult == atomicResult); // True
```

`ToAtomic()` concatenates all link bytecodes into a single `Instruction[]`. Automatically called during JIT compilation or when evaluating long chains (>8 links).

## Automatic Merge on Threshold Exceeded

When chain length exceeds `FluxConfig.MergeThreshold` (default 8), `Instantiate` automatically calls `ToAtomic` before evaluation:

```csharp
FluxChain<float, MathDef> chain = runner.Compile(lexer.Lex("1 + 1"))
    .Connect(runner.Compile(lexer.Lex("2 * 1")).ToModifier());
for (int i = 0; i < 9; i++)
    chain = chain.Connect(
        runner.Compile(lexer.Lex("2 * 1")).ToModifier());

// chain.Length = 11 (exceeds MergeThreshold 8)
// Instantiate auto-merges, avoiding 11 per-link calls
var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // evaluates correctly
```

Users don't need to worry about the threshold — `Instantiate` automatically chooses the optimal path.

## ToModifier / ToFormula Round-Trip

Formula ↔ Modifier conversion preserves evaluation equivalence:

```csharp
var f = runner.Compile(lexer.Lex("7 + 3")); // = 10

// Formula → Modifier → Formula (round-trip)
var mod      = f.ToModifier();
var restored = mod.ToFormula("input");

// Inject the original value via the new variable name
var inst = runner.Instantiate(restored).Set("input", 7f);
Console.WriteLine(inst.Run()); // 10 (equivalent to f's evaluation)
```

## Modifier Cannot Run Standalone

In v3.0.0, `FluxModifier<TData, TDef>` has no `Instantiate()` method — any code that attempts to independently evaluate a Modifier **won't compile**. A Modifier can only be attached behind a Formula via `Connect()`, or converted to a complete Formula via `ToFormula(varName)`.

## Cross-Definition Type Safety

`FluxFormula<TData, TDef>` binds to a specific definition via the `TDef` generic parameter. `FluxFormula<float, MathDef>` and `FluxFormula<float, GameDef>` are different compile-time types — any accidental cross-connection won't compile.

## Notes

- `ChainReserved.InternalPrefix` (`"CHAIN_LINK_INTERNAL_"`) is a variable name prefix used internally for chain evaluation. Users must not declare variables with this prefix in `LexerConfig.VariablePatterns`
- `FluxChain.Length`, `FluxChain.GetLinks()`, and `ChainLink` are all public API — advanced users can read chain structure via `GetLinks()` and persist it as VFF using `VffFormat.ToBytes()`
- `Connect()` always returns `FluxChain<TData, TDef>` — `FluxFormula` and `FluxModifier` are no longer chain containers
- `ToMultiplier()` is retained as a `[Obsolete]` alias pointing to `ToModifier()`
