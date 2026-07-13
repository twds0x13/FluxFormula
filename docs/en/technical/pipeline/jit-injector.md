# JIT Injector: FluxJITInjector

Its core design question: a JIT-compiled delegate only needs to write directly to the bytecode buffer by SlotIndex during execution -- it does not need variable name mapping, modifier detection, or value readback. Why pay the branch penalty for features it does not use?

The answer is to split the JIT hot-path injection logic into a dedicated type `FluxJITInjector<TData>`: 2 fields, zero branches, stack-allocated. The full `FluxInjector<TData>` (11 fields, multiple branches) is preserved for the interpreter path and chain evaluation.

## Why the Split (v5.7.1)

Before v5.7.0, JIT-compiled delegates used the same injector as the interpreter -- `FluxInjector<TData>`:

- 11 fields: `_buffer`, `_offsets`, `_slotsPerData`, `_varNames`, `_varSlotIndexes`, `_varCount`, `_values`, and more
- `SetIndex(int index, TData value)` contained multiple branches: `_offsets == null` check (JIT mode vs interpreter mode), offset table lookup, bounds check
- Every variable injection on the JIT hot path passed through these branches, even though the JIT scenario only needs the simplest `buffer[offset] = value`

v5.7.1 split the JIT hot-path injection logic into a dedicated type:

## Implementation

```csharp
internal readonly struct FluxJITInjector<TData>
    where TData : unmanaged
{
    private readonly Instruction[] _buffer;   // payload buffer (shared reference)
    private readonly int _slotsPerData;       // DataSlots<TData>()

    internal FluxJITInjector(Instruction[] buffer)
    {
        _buffer = buffer;
        _slotsPerData = FormulaFormat.DataSlots<TData>();
    }

    internal readonly FluxJITInjector<TData> SetIndex(int paramIndex, TData value)
    {
        int offset = paramIndex * _slotsPerData;
        unsafe
        {
            fixed (Instruction* pBase = _buffer)
                *(TData*)(pBase + offset) = value;
        }
        return this;
    }
}
```

Key characteristics:
- **2 fields**: `_buffer` (payload buffer) + `_slotsPerData` (instruction slots per TData)
- **Zero branches**: `SetIndex` directly computes the offset, pointer write, zero conditionals
- **Zero dictionary lookups**: no variable name to SlotIndex mapping needed (the JIT compiler determines SlotIndex at compile time)
- **Zero value readback**: no `_values` array needed (chained JIT's `BuildLinkBuffer` does not read back from this injector)

## Performance Comparison

| | FluxInjector (full) | FluxJITInjector |
|---|---|---|
| Fields | 11 | 2 |
| SetIndex branches | 2 (`_offsets == null` + offset lookup) | 0 |
| Name lookup | Binary search O(log n) | Not needed |
| Value readback | `_values[]` array | Not needed |
| Allocation | Heap (contains arrays) | Stack (`Instruction[]` is shared reference) |
| Purpose | Interpreter + chain JIT merge readback | JIT hot path |

## Dual-Path Design

Injector type is chosen at compile time, with no runtime polymorphism:

```mermaid
flowchart TD
    A[FluxAssembler.Instantiate] --> B{JIT enabled?}
    B -->|Yes| C{Chained?}
    B -->|No| D[FluxInjector + interpreter]
    C -->|Single formula| E[FluxJITInjector + CompiledFunc]
    C -->|Chain| F[FluxInjector + per-link CompiledFunc[]]
```

- **JIT hot path** (single formula): uses `FluxJITInjector`. JIT delegates have the signature `CompiledFunc<TData>(Instruction[] dataBuffer)`, and the injector is passed as a value type on the call stack -- zero GC.
- **Chain JIT**: still uses `FluxInjector`. `BuildLinkBuffer` needs to read back upstream link output values (via `GetValue`), which `FluxJITInjector` does not support.
- **Interpreter path**: always uses `FluxInjector`. Needs full variable name mapping and modifier detection.

## Design Decisions

1. **Compile-time selection, not runtime branching**: `FluxJITCompiler` decides which injector type to use during compilation, eliminating the `_offsets == null` runtime check.

2. **Shared reference, not copy**: `_buffer` is an `Instruction[]` reference; `FluxJITInjector` shares the same array with its caller. The injector does not own the buffer.

3. **Original FluxInjector retains all capabilities**: the interpreter needs complete variable slot mapping, modifier support, and chain JIT merge readback. These are not removed by the split.

4. **Not a ref struct**: `FluxJITInjector` must be returnable from methods, so it is a plain `readonly struct`. Its sole reference-type field (`Instruction[]`) is shared, avoiding heap allocation copies.

## References

- [Data Injector](./injector.md) -- FluxInjector full injector (interpreter path)
- [Interpreter Execution Loop](./evaluator.md) -- hot-path evaluator
- [Expression Tree Compilation](./jit.md) -- JIT delegate compilation
- [Architecture Decisions](../architecture-decisions.md) -- ADR v5.7.0 FluxJITInjector split
