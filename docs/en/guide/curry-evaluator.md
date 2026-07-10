# Progressive Binding Evaluator (Currying)

Suppose you have a skill damage formula: `[baseAtk] * [skillMult] * (1 + [critDmg])`. baseAtk and critDmg are character stats that rarely change; skillMult is a per-skill parameter. You can bind the character stats once when the character panel opens, save the intermediate state, then bind only skillMult for each skill — instead of chaining `.Set().Set().Set().Run()` every time.

`FluxCurryEvaluator` provides functional-style progressive variable binding. Bind parameters in declaration order, each `Bind` returning a new State. Supports forking — multiple callers can bind different values from the same intermediate state. Ideal for deferred parameter injection and template formula reuse.

## Relationship to the Hot Path

Three evaluation modes in FluxFormula:

| | Hot Path | Curry | Step Debug |
|---|---|---|---|
| Type | `FluxInstance` (ref struct) | `FluxCurryEvaluator` (struct) | `FluxStepEvaluator` (struct) |
| Purpose | Production full-speed evaluation | Progressive parameter binding | Per-instruction debugging |
| Variable injection | `.Set(name, value)` | `.Bind(params values)` by order | None |
| State model | Mutable ref struct | Immutable State → State | Immutable State → State |
| Forking | Not supported | Supported | Manual via state saves |

## Creation

```csharp
var assembler = new FluxAssembler<float, FloatMathDef>(definition);
var formula = assembler.Compile(lexer.Lex("[a] * [b] + [c]"));

var curry = assembler.Curry(formula);
// Equivalent to: FluxCurryEvaluator<float, FloatMathDef>.Create(definition, formula)
```

## API

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsCompleted` | `bool` | `true` when all variables are bound and evaluation finished |
| `Result` | `TData` | Final result. Reading while incomplete fills remaining variables with `default` and completes evaluation |
| `BoundCount` | `int` | Number of variables bound so far |
| `VariableCount` | `int` | Total variable count in the formula |

### Methods

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
```

Binds the next N variables in bytecode order. Returns a new instance each call; the original
is unchanged. Excess values are silently clamped; empty array is a no-op.

## Usage

### Basic: Batch Binding

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[x] + [y] * [z]")));
curry = curry.Bind(10f, 2f, 3f);
// curry.IsCompleted == true, curry.Result == 16f
```

### Incremental Binding

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[a] * [b]")));
var step1 = curry.Bind(5f);
// step1.BoundCount == 1, step1.IsCompleted == false

var step2 = step1.Bind(3f);
// step2.Result == 15f
```

### Forking

```csharp
var step1 = curry.Bind(5f);

var branchA = step1.Bind(2f);     // 5 * 2 = 10
var branchB = step1.Bind(3f);     // 5 * 3 = 15
// step1 is still BoundCount=1
```

### Force Complete via Result

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[x] + 5")));
Console.WriteLine(curry.Result);  // 5 (0 + 5, x filled with default)
```

## Notes

- Non-`ref struct` — can be stored in fields, arrays, generic type parameters
- Each `Bind` allocates two arrays (`_boundValues` + `_regs`), sized to variable count
- `params` syntax enables batch binding, avoiding chained `Bind().Bind().Bind()`
- Curry always interprets; bypasses the JIT path
- Chain formulas (`FluxChain`) must call `.ToAtomic()` before passing to curry
