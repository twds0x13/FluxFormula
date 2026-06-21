# Data Injector

`FluxInjector<TData>` is responsible for writing user parameters into the formula bytecode buffer. Its core design question: **how to efficiently locate and overwrite specific variable values in a compact `Instruction[]` while maintaining zero GC?**

## Two Injection Modes

FluxInjector has two working modes, depending on the execution backend:

| Mode | Trigger | Data Layout | Location Method |
|------|---------|-------------|-----------------|
| **JIT mode** | `jit: true` | payload array (compact TData sequence) | Linear index: `paramIndex * slotsPerData` |
| **Interpreter mode** | `jit: false` | full formula buffer (Instruction[] + inlined TData) | Offset array: `_offsets[paramIndex]` |

The fundamental difference between the two modes comes from Instruction's different roles in JIT and interpreter:
- **JIT mode**: Instructions and data are separated. Instructions become Expression Trees (consumed at compile time); data stays in the payload array for runtime reading. The payload is a compact TData sequence with no Instruction headers.
- **Interpreter mode**: Instructions and data coexist in the same Instruction[] buffer. TData follows immediately after the Immediate instruction header, written via pointer offset.

## Core Data Structure

```csharp
internal unsafe struct FluxInjector<TData> where TData : unmanaged
{
    private readonly Instruction[] _buffer;        // bytecode buffer (shared reference)
    private readonly int[] _offsets;               // interpreter mode: Immediate offsets in buffer
    private readonly FluxFormula.VariableSlot[] _variableSlots; // variable name → position mapping
    private readonly int _slotsPerData;            // Instruction slots occupied per TData
}
```

`_buffer` is a shared reference — FluxInjector does not own the buffer; it only holds a reference and writes to it. This avoids copying, but also means multiple FluxInstances cannot concurrently operate on the same buffer (not a concern in Unity's main thread context).

## SetByIndex — Index-Based Injection

```csharp
public FluxInjector<TData> SetIndex(int paramIndex, TData value)
{
    int offset;
    if (_offsets == null)  // JIT mode
    {
        offset = paramIndex * _slotsPerData;
    }
    else                   // Interpreter mode
    {
        offset = _offsets[paramIndex];
    }

    fixed (Instruction* pBase = _buffer)
    {
        *(TData*)(pBase + offset) = value;  // pointer reinterpretation write
    }
    return this;
}
```

Key details:
- In `pBase + offset`, the offset unit is "number of Instructions", not bytes. `Instruction*` arithmetic automatically multiplies by `sizeof(Instruction)` = 8.
- `*(TData*)(pBase + offset)` reinterprets the address of the Instruction slot as a TData pointer and writes directly. No memcpy, no boxing.
- Returns `this` to enable fluent chaining.

## SetByName — Named Variable Injection

`Set(name, value)` needs to map a variable name to paramIndex. This is the most technically interesting part of the injector.

### Why Not a Dictionary?

The standard C# approach would be `Dictionary<string, int>` for name → index mapping. FluxFormula does not use this because:

1. **Dictionary is a heap-allocated type**. Even with a cached Dictionary instance, each lookup incurs virtual dispatch overhead.
2. **Variable names are fixed after compilation**. VariableSlots are sorted in lexicographic order at compile time, naturally supporting binary search.
3. **Variable counts are typically small**. Game formulas usually have 2–20 variables. Inline binary search has a better constant factor than Dictionary at these scales.

### Implementation: Inline Binary Search

```csharp
public FluxInjector<TData> Set(string name, TData value)
{
    // Binary search for variable name in VariableSlots
    int lo = 0, hi = _variableSlots.Length - 1;
    while (lo <= hi)
    {
        int mid = (lo + hi) / 2;
        int cmp = string.CompareOrdinal(name, _variableSlots[mid].Name);
        if (cmp == 0)
        {
            // Found: batch-update all occurrences of this variable name
            int slotIndex = _variableSlots[mid].SlotIndex;
            // ... update all positions corresponding to slotIndex
            return this;
        }
        if (cmp < 0) hi = mid - 1;
        else lo = mid + 1;
    }
    throw new ArgumentException($"Variable '{name}' not found.");
}
```

Notable design choices:

- **`string.CompareOrdinal`** rather than `CompareTo`. Ordinal comparison avoids culture-sensitive sorting, is faster, and has consistent behavior.
- **All occurrences of the same variable name are updated simultaneously**. A variable name can appear multiple times in a formula (e.g., `[atk] * 2 + [atk]`); all occurrences share a single SlotIndex. One Set overwrites all positions.
- **Sorting happens at compile time**. FluxCompiler sorts VariableSlots by Name during compilation; zero sorting cost at runtime.

### Sorting Strategy: Parallel Arrays

FluxCompiler maintains two parallel arrays:
- `List<string> varNames`: variable names
- `List<int> varPositions`: corresponding positions in Instruction[]

After compilation, both are jointly sorted by variable name (any stable sort algorithm works). Entries with the same variable name cluster together after sorting. They are then compressed into VariableSlot[], with adjacent same-name entries merged into a single record.

This "parallel arrays + sort + compress" strategy is more GC-friendly than Dictionary: sorting happens on the compiler's mutable Lists; the final product VariableSlot[] is a compact read-only array.

## JIT Mode Special Handling

In JIT mode, `_offsets` is null and offsets are computed as `paramIndex * _slotsPerData`. This means the JIT payload must arrange TData strictly in paramIndex order with no gaps.

The JIT compiler determines the paramIndex-to-payload-position mapping at `Compile()` time. It generates an Expression Tree that is independent of variable count — variable values are read from the payload at runtime via `GetData<TData>(buffer, index)`.

## Pointer Write Safety

```csharp
fixed (Instruction* pBase = _buffer)
{
    *(TData*)(pBase + offset) = value;
}
```

The safety of this write is guaranteed by:
- The `TData : unmanaged` constraint ensures the type can be safely accessed via pointer
- `sizeof(TData)` varies, but `_slotsPerData = (sizeof(TData) + 7) / 8` guarantees sufficient Instruction slots
- Bounds checking is performed at the `SetIndex` entry point (though extreme values lack overflow protection; see [Technical Analysis](../technical-analysis.md#27-fluxinjectorcs))

## Next Steps

- [Pipeline Overview](./overview.md) — back to pipeline overview
