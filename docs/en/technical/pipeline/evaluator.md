# Interpreter Execution Loop

`FluxEvaluator<TData, TDef>` is a stack-based bytecode interpreter. Core design question: **how to execute bytecode entirely on the stack (outside the managed heap), achieving 27ns single-evaluation latency?**

## Zero-Allocation Execution Stack

```csharp
internal unsafe ref struct FluxEvaluator<TData, TDef>
{
    public TData Compute(ReadOnlySpan<Instruction> program, byte maxRegister = 0)
    {
        int regCount = maxRegister > 1 ? maxRegister + 1 : FluxPlatform.MaxRegisters;
        TData* regs = stackalloc TData[regCount];
        // ... execution loop ...
    }
}
```

`ref struct` guarantees `FluxEvaluator` never escapes to the heap. `stackalloc TData[regCount]` allocates the register file on the stack — for `float` with max 8 registers, only 32 bytes. Register count is determined by the formula header's `MaxRegister`, not always 255.

## R0/R1 Bus Convention

| Register | Index | Semantic |
|----------|-------|----------|
| R0 (Error) | 0 | Error flag (reserved, unused) |
| R1 (Bus) | 1 | Chain serial bus: previous link output → next link input |

R1 initializes to `default(TData)`. The `Return` instruction writes its Dest register value to R1 for consumption by the next chain link.

## Core Execution Loop

```csharp
for (int ip = 0; ip < program.Length; )
{
    var inst = program[ip];
    byte opCode = inst.OpCode;

    if (opCode == ReturnOp)
    {
        regs[Registers.Bus] = regs[inst.Dest];
        ip++;
    }
    else if (kind == OpType.Immediate)
    {
        regs[inst.Dest] = *(TData*)(pBase + ip + 1);
        ip += 1 + dataSlots;
    }
    else  // Instruction
    {
        regs[inst.Dest] = _definition.Compute(opCode, inst, new ReadOnlySpan<TData>(regs, regCount));
        ip++;
    }
}
```

- **Immediate**: pointer reinterpretation reads `TData` directly from the instruction stream, writing to the destination register. PC skips the instruction header + data slots.
- **Instruction**: delegates to `_definition.Compute()`, where the Definition implements the actual arithmetic logic.
- **Return**: writes the Dest register value to R1 (Bus), awaiting consumption by the next link.

## Interpreter vs. JIT Trap: Return Semantics

The `Return` instruction behaves differently in the interpreter and JIT:

- **Interpreter**: `Return` writes Dest to R1, then continues to the next instruction. During chained evaluation, the next link's code follows immediately after `Return`, reading from R1. This is "fall-through" semantics.
- **JIT**: each instruction compiles to an independent Expression Tree. The Expression corresponding to `Return` returns its Dest value; the JIT delegate caller (`RunJitChain`) handles injecting it into the next link's R1 position.

This difference means **the bytecode is semantically equivalent across interpreter and JIT paths — only the execution mechanism differs**. This is the core of JIT consistency testing.

## Why Not Switch Dispatch?

Traditional bytecode interpreters use `switch(opCode)` dispatch. FluxFormula uses a three-way branch (Immediate / Instruction / Return) because:

1. **Unknown opcode count**: opcodes are defined by the Definition. The framework does not know how many operators exist. A `switch` cannot exhaustively enumerate them at the framework level.
2. **Delegated semantics**: the `Compute()` delegate hands operator semantics entirely to the Definition. The framework does not interpret opcode meanings.
3. **Branch-predictor-friendly**: the three-category pattern (Immediate / Instruction / Return) is highly predictable. Immediate and Instruction alternate in bytecode, making the branch pattern regular.

## Two `Compute` Overloads

```csharp
// Standard: R1 initialized to default(TData)
public TData Compute(ReadOnlySpan<Instruction> raw, byte maxRegister = 0)

// Chained: R1 initialized from prevResult (previous link output)
public TData Compute(ReadOnlySpan<Instruction> raw, TData prevResult, byte maxRegister = 0)
```

The second overload enables per-link chain interpretation without link awareness.
