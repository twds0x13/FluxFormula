# FluxCurryEvaluator

When you need progressive variable binding—rather than supplying all parameters at once—`FluxCurryEvaluator` suspends evaluation between variable injection points. Each `Bind` returns a new state; prior states are unaffected.

## Signature

```csharp
public readonly struct FluxCurryEvaluator<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsCompleted` | `bool` | All variables bound and evaluation complete |
| `Result` | `TData` | Evaluation result. Throws `InvalidOperationException` when the mask is incomplete; call `ForceComplete` or bind all variables first |
| `BoundCount` | `int` | Number of variables bound so far |
| `VariableCount` | `int` | Total number of variables in the formula |

## Methods

### Create (static factory)

```csharp
public static FluxCurryEvaluator<TData, TDef> Create(
    TDef definition, FluxFormula<TData, TDef> formula)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `definition` | `TDef` | Operator definition |
| `formula` | `FluxFormula<TData, TDef>` | Compiled formula |

After creation, automatically executes to the first suspension point (first unbound variable) and returns the initial state.

### Bind (by name)

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(string name, TData value)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Variable name (out-of-order binding) |
| `value` | `TData` | Value to bind |

Looks up an unbound variable by name, injects the value, then executes to the next suspension point or completion. Throws `ArgumentException` if the variable is already bound or does not exist. Returns a new instance.

### Bind (sequential)

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `values` | `params TData[]` | Bind the next N unbound variables in order |

Binds values sequentially, executing to a suspension point after each. Returns a new instance.

### ForceComplete

```csharp
public FluxCurryEvaluator<TData, TDef> ForceComplete()
```

Fills all remaining unbound variables with `default(TData)` and executes to completion. Once `IsCompleted` is `true`, `Result` can be read directly.

## Usage

#### Stepwise Binding

```csharp
var def = default(MathDef);
var assembler = new FluxAssembler<float, MathDef>(def);
var lexer = new FluxLexer<float>(config);
var formula = assembler.Compile(lexer.Lex("[atk] * [mult] + [bonus]"));

var curry = FluxCurryEvaluator<float, MathDef>.Create(def, formula);
curry = curry.Bind("atk", 100f);   // inject atk, execute until mult suspension
curry = curry.Bind("mult", 2f);    // inject mult, execute until bonus suspension
curry = curry.Bind("bonus", 50f);  // inject bonus, evaluation complete
float result = curry.Result;       // 250
```

#### Midstream Forking

```csharp
var baseState = FluxCurryEvaluator<float, MathDef>.Create(def, formula)
    .Bind("atk", 100f).Bind("mult", 2f);

var branchA = baseState.Bind("bonus", 50f);   // 250
var branchB = baseState.Bind("bonus", 0f);    // 200
// baseState is unaffected
```

#### Force Completion

```csharp
var partial = FluxCurryEvaluator<float, MathDef>.Create(def, formula)
    .Bind("atk", 100f);
// Remaining mult, bonus filled with 0f
float result = partial.ForceComplete().Result;  // 0
```

## See Also

- [FluxStepEvaluator](./flux-step-evaluator) — Per-instruction step debugger
- [FluxInstance](./flux-instance) — Hot-path evaluator with one-shot injection
- [FluxAssembler](./flux-assembler) — Compilation entry point
