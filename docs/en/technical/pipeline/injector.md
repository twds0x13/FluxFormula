# Data Injector

`FluxInjector<TData>` is responsible for writing user parameters into the formula bytecode buffer. Its core design question: how to efficiently locate and overwrite specific variable values in a compact `Instruction[]` while maintaining zero GC.

JIT hot-path injection is handled by the separate type `FluxJITInjector<TData>`; see [JIT Injector](./jit-injector.md).

## Core Data Structure

```csharp
internal readonly struct FluxInjector<TData> where TData : unmanaged
{
    private readonly Instruction[] _buffer;         // bytecode buffer (shared reference)
    private readonly int[] _offsets;                // Immediate offsets in the buffer
    private readonly int _slotsPerData;             // Instruction slots occupied per TData

    // Variable lookup: parallel arrays, binary search, zero GC
    private readonly string[] _varNames;            // unique variable names (dictionary order)
    private readonly int[][] _varSlotIndexes;       // per-name SlotIndex groups
    private readonly int _varCount;                 // unique variable count

    // Value readback: indexed by SlotIndex, written by Set/SetIndex, read by GetValue
    private readonly TData[] _values;
}
```

Variable name lookup uses **parallel arrays `_varNames[]` + `_varSlotIndexes[][]`**. Each group of identically-named variables is stored as an `int[]` array in `_varSlotIndexes`; a single `Set` call overwrites all occurrences. `_values[]` stores values by SlotIndex; `GetValue()` provides O(1) readback, used by chain evaluation's `BuildLinkBuffer`.

## SetIndex: Index-Based Injection

```csharp
internal readonly FluxInjector<TData> SetIndex(int paramIndex, TData value)
{
    // Value readback (BuildLinkBuffer depends on this array during chain evaluation)
    if (_values != null && paramIndex < _values.Length)
        _values[paramIndex] = value;

    if (paramIndex < 0 || paramIndex >= _offsets.Length)
        throw new IndexOutOfRangeException(
            $"Parameter index {paramIndex} is out of bounds.");

    int offset = _offsets[paramIndex];
    unsafe
    {
        fixed (Instruction* pBase = _buffer)
            *(TData*)(pBase + offset) = value;
    }
    return this;
}
```

Key details:
- In `pBase + offset`, the offset unit is "number of Instructions", not bytes. `Instruction*` arithmetic automatically multiplies by `sizeof(Instruction)` = 8.
- `*(TData*)(pBase + offset)` reinterprets the address of the Instruction slot as a TData pointer and writes directly. No memcpy, no boxing.
- Returns `this` to enable fluent chaining.

The JIT hot path uses [FluxJITInjector](./jit-injector.md) (2 fields, zero branches) instead of this method.

## Set: Named Variable Injection

`Set(name, value)` maps a variable name to SlotIndex. This is the most technically interesting part of the injector.

### Why Not a Dictionary

The standard C# approach would use `Dictionary<string, int>` for name-to-index mapping. FluxFormula does not use this because:

1. **Dictionary is a heap-allocated type**. Even with a cached Dictionary instance, each lookup incurs virtual dispatch overhead.
2. **Variable names are fixed after compilation**. VariableSlots are sorted in dictionary order at compile time, naturally supporting binary search.
3. **Variable counts are typically small**. Game formulas usually have 2-20 variables. Inline binary search has a better constant factor than Dictionary at these scales.

### Implementation: Inline Binary Search

```csharp
internal readonly FluxInjector<TData> Set(string name, TData value)
{
    int lo = 0, hi = _varCount - 1;
    while (lo <= hi)
    {
        int mid = lo + (hi - lo) / 2;
        int cmp = string.CompareOrdinal(_varNames[mid], name);
        if (cmp == 0)
        {
            int[] slotIndexes = _varSlotIndexes[mid];
            for (int i = 0; i < slotIndexes.Length; i++)
            {
                int si = slotIndexes[i];
                if (_values != null && si < _values.Length)
                    _values[si] = value;

                int offset = _offsets[si];
                unsafe
                {
                    fixed (Instruction* pBase = _buffer)
                        *(TData*)(pBase + offset) = value;
                }
            }
            return this;
        }
        if (cmp < 0) lo = mid + 1;
        else         hi = mid - 1;
    }
    throw new ArgumentException($"Variable '{name}' is not defined in this formula.");
}
```

- **`string.CompareOrdinal`**: ordinal comparison avoids culture-sensitive sorting.
- **All occurrences of the same variable name are updated simultaneously**: via `_varSlotIndexes[mid]` (`int[]` array), iterating all positions in one pass.
- **Safe midpoint calculation**: `lo + (hi - lo) / 2` avoids integer overflow.
- **`_values` write-back**: every overwritten position also updates `_values[si]` for O(1) `GetValue()` readback.

### Sorting Strategy: Dedup + Sort at Construction

FluxInjector builds the lookup table from `VariableSlot[]` at runtime:

1. Iterate `varSlots` to deduplicate and count unique variable names
2. Create an `int[]` array per unique name collecting all `SlotIndex` values
3. Jointly sort by name in dictionary order via `Array.Sort(_varNames, _varSlotIndexes, ...)`
4. At lookup time, binary-search `_varNames[]`; on hit, iterate `_varSlotIndexes[mid]` to update all slots

Sorting happens once at Injector construction; every subsequent `Set` call is allocation-free.

## TrySet: Silent Injection

```csharp
public FluxInjector<TData> TrySet(string name, TData value)
```

Same injection logic as `Set`, but silently skips when the variable name does not exist instead of throwing. Suitable for VFF override application: a mod may reference variable names from the base game that do not exist in the mod's own link. Callers need not know each link's variable signature in advance.

## GetValue: Value Readback

```csharp
public TData GetValue(int slotIndex)
```

Reads the value at the given SlotIndex from the `_values` array in O(1) time. The chain interpreter's `BuildLinkBuffer` relies on this method to obtain upstream link output values (propagated via the R1 Bus register).

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
- Bounds checking is performed at the `SetIndex` entry point (though extreme values lack overflow protection; see [Technical Analysis](../technical-analysis.md))

## References

- [JIT Injector](./jit-injector.md) -- FluxJITInjector hot-path injector (v5.7.1 split)
- [Curry Evaluator](./curry-evaluator.md) -- gradual injection evaluation
- [Pipeline Overview](./overview.md) -- back to pipeline overview
