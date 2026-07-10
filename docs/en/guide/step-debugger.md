# Step Debugger

Formula giving wrong results? Want to see exactly where the bytecode goes off track? The step debugger lets you inspect register state instruction by instruction — debug your formula execution like you would step through C# code.

`FluxStepEvaluator` provides per-instruction execution, exposing instruction pointer and register snapshots. Useful for debugging compiler output, visualizing formula execution, and educational demonstrations.

## Creation

```csharp
var assembler = new FluxAssembler<float, FloatMathDef>(definition);
var formula = assembler.Compile(new[] { C(3f), Op(FloatOp.Mul), C(4f) });

var step = assembler.StepDebug(formula);
// Equivalent to: FluxStepEvaluator<float, FloatMathDef>.Create(definition, formula)
```

`C()` and `Op()` are test helpers generating Const immediate and Instruction operator tokens.

For the complete property list, method signatures, and parameter descriptions, see [FluxStepEvaluator API Reference](/en/api/flux-step-evaluator).

## Usage

### Manual Stepping

```csharp
var step = assembler.StepDebug(formula);
Console.WriteLine(step.CurrentIP);     // 0
Console.WriteLine(step.CurrentOpCode); // Const

step = step.Step();  // load immediate into register
step = step.Step();  // execute multiplication
step = step.Step();  // Return: complete

Assert.That(step.IsCompleted, Is.True);
Assert.That(step.Result, Is.EqualTo(12f));
```

### Count Instructions

```csharp
var step = assembler.StepDebug(formula);
int steps = 0;
while (!step.IsCompleted) { step = step.Step(); steps++; }
// steps == 3 (Const + Mul + Return)
```

### Inspect Registers

```csharp
var step = assembler.StepDebug(formula);
step = step.Step();
var snapshot = step.Regs;
Console.WriteLine(snapshot.Length);
```

## Notes

- `ReadOnlySpan<TData>` register snapshot is valid only per step — each `Step()` returns a new view
- Non-`ref struct` — can be stored in fields or arrays
- Step debug always interprets; bypasses the JIT path
- Like `FluxCurryEvaluator`, each `Step()` allocates a new array
