# FluxStepEvaluator

When you need to inspect register state instruction-by-instruction and understand bytecode execution flow, `FluxStepEvaluator` provides instruction-level single-step debugging. Each `Step()` executes one instruction and returns a new state; prior states are unaffected.

## Signature

```csharp
public readonly struct FluxStepEvaluator<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsCompleted` | `bool` | Whether execution has finished |
| `Result` | `TData` | Final result (meaningful only when `IsCompleted`) |
| `CurrentIP` | `int` | Current instruction pointer (bytecode index) |
| `CurrentOpCode` | `byte` | Opcode of the current instruction (0 if not started or completed) |
| `CurrentInstruction` | `Instruction` | Full struct of the current instruction (default if not started or completed) |
| `Regs` | `ReadOnlySpan<TData>` | Read-only snapshot of the register file |
| `InstructionCount` | `int` | Total number of instructions |

## Methods

### Create (static factory)

```csharp
public static FluxStepEvaluator<TData, TDef> Create(
    TDef definition, FluxFormula<TData, TDef> formula)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `definition` | `TDef` | Operator definition |
| `formula` | `FluxFormula<TData, TDef>` | Compiled formula |

Returns the initial state (IP = 0, not yet started).

### Step

```csharp
public FluxStepEvaluator<TData, TDef> Step()
```

Executes exactly one instruction. Returns itself if already completed. Returns a new instance (full register array copy).

### RunToEnd

```csharp
public FluxStepEvaluator<TData, TDef> RunToEnd()
```

Executes all remaining instructions to completion. Equivalent to calling `Step()` in a loop, but without intermediate copies.

## Usage

#### Step Debugging Loop

```csharp
var def = default(MathDef);
var formula = new FluxAssembler<float, MathDef>(def)
    .Compile(new FluxLexer<float>(config).Lex("(3 + 4) * 2"));

var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);

while (!state.IsCompleted)
{
    Console.WriteLine(
        $"IP={state.CurrentIP}, Op={(MathOp)state.CurrentOpCode}, " +
        $"R1={state.Regs[1]}, R2={state.Regs[2]}");
    state = state.Step();
}
Console.WriteLine($"Result = {state.Result}");  // 14
```

#### Run to Completion

```csharp
var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);
var final = state.RunToEnd();
float result = final.Result;
```

#### Inspecting the Current Instruction

```csharp
var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);
var inst = state.CurrentInstruction;
// inst.OpCode, inst.Arg0, inst.Arg1, inst.Dest
```

## See Also

- [FluxCurryEvaluator](./flux-curry-evaluator) — Variable-level progressive evaluation
- [FluxInstance](./flux-instance) — Hot-path full-speed evaluation
- [Instruction](./instruction) — 8-byte instruction struct
