# Step Debugger

`FluxStepEvaluator` provides per-instruction execution, exposing instruction pointer and register snapshots. Useful for debugging compiler output, visualizing formula execution, and educational demonstrations.

## Creation

```csharp
var assembler = new FluxAssembler<float, FloatMathDef>(definition);
var formula = assembler.Compile(new[] { C(3f), Op(FloatOp.Mul), C(4f) });

var step = assembler.StepDebug(formula);
// Equivalent to: FluxStepEvaluator<float, FloatMathDef>.Create(definition, formula)
```

`C()` and `Op()` are test helpers generating Const immediate and Instruction operator tokens.

## API

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsCompleted` | `bool` | `true` when all instructions have been executed |
| `Result` | `TData` | Final result (meaningful only when `IsCompleted`) |
| `CurrentIP` | `int` | Current instruction pointer (index into bytecode) |
| `CurrentOpCode` | `byte` | Opcode byte of the current instruction |
| `CurrentInstruction` | `Instruction` | Full Instruction struct at current IP |
| `Regs` | `ReadOnlySpan<TData>` | Read-only snapshot of the register file |
| `InstructionCount` | `int` | Total instruction count in the bytecode |

### Methods

```csharp
public FluxStepEvaluator<TData, TDef> Step()
```

Executes one instruction. Returns a new instance. No-op when already completed.

```csharp
public FluxStepEvaluator<TData, TDef> RunToEnd()
```

Loops `Step()` until completion. Returns the final state.

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
