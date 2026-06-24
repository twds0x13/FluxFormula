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
    if (opCode == ReturnOp) { regs[Bus] = regs[inst.Dest]; ip++; }
    else if (kind == Immediate) { regs[inst.Dest] = *(TData*)(pBase + ip + 1); ip += 1 + dataSlots; }
    else { regs[inst.Dest] = _definition.Compute(opCode, inst, registers); ip++; }
}
```

- **Immediate**: pointer-reinterpretation read from instruction stream; PC skips header + data slots.
- **Instruction**: delegates to `_definition.Compute()` for operator semantics.
- **Return**: writes Dest to R1 (Bus) for chain consumption.

## Return Semantics: Interpreter vs JIT

- **Interpreter**: `Return` writes Dest to R1, then falls through to the next instruction. Chained links follow directly in the instruction stream, reading from R1.
- **JIT**: Each instruction compiles to an independent Expression Tree. `Return` produces an Expression returning the Dest value; the caller (`RunJitChain`) injects it into the next link's injector.

The bytecode is semantically identical across both paths — only execution differs. This is what `JitConsistencyTests` verifies.

## Two `Compute` Overloads

```csharp
// Standard: R1 initialized to default(TData)
public TData Compute(ReadOnlySpan<Instruction> raw, byte maxRegister = 0)

// Chained: R1 initialized from prevResult (previous link output)
public TData Compute(ReadOnlySpan<Instruction> raw, TData prevResult, byte maxRegister = 0)
```

The second overload enables per-link chain interpretation without link awareness.
