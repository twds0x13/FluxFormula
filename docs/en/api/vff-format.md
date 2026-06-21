# VffFormat

VFF (Virtual FluxFormula) byte format definition and resolver. VFF is not a standalone asset type — it is an entry type within a blob, distinguished from formula entries by the `"VFF\0"` magic bytes. VFF and formula entries coexist in the same blob, sharing the `FluxBlob.Entry` offset table.

## Purpose

The core purpose of VFF is **persistent formula composition references**: during offline build, references to multiple formulas and their parameter override metadata are packed into a VFF entry in the blob. At runtime, `Resolve()` expands the VFF into an executable chain formula without recompilation.

## Byte Layout

```
Header (8B):     Magic(4 "VFF\0") + Version(1) + LinkCount(1) + OverrideCount(1) + Flags(1)
LinkTable:       LinkCount × 22B — Hash(16) + ImmCount(1) + InstCount(2) + Type(1) + VarSlotCount(2)
OverrideTable:   OverrideCount × variable — GlobalSlot(2) + Kind(1) + [DataLen(1) + Data(var)]
```

Variable names are not stored in the VFF — they are read directly from the referenced formulas' bytecode via `FormulaFormat.ReadVariableSlots()` at resolve time.

## Constants

| Constant | Type | Value | Description |
|------|------|------|------|
| `Magic` | `byte[]` | `"VFF\0"` | VFF entry identification magic bytes |
| `HeaderSize` | `int` | `8` | Header size in bytes |
| `LinkEntrySize` | `int` | `22` | Single link entry size in bytes |
| `FlagHasConstants` | `byte` | `1 << 0` | bit0: contains hard-coded constant data |

## Structs

### VffHeader

8-byte header (`[StructLayout(Size = 8)]`).

| Field | Type | Offset | Description |
|------|------|------|------|
| `Version` | `byte` | +4 | Format version (currently 1) |
| `LinkCount` | `byte` | +5 | Number of referenced formulas (max 255) |
| `OverrideCount` | `byte` | +6 | Number of parameter overrides (max 255) |
| `Flags` | `byte` | +7 | Flag bits |
| `HasConstants` | `bool` | — | `(Flags & FlagHasConstants) != 0` |

Constructor: `new VffHeader(version, linkCount, overrideCount, flags)`

### VffLinkEntry

22-byte formula reference (`[StructLayout(Size = 22)]`).

| Field | Type | Offset | Description |
|------|------|------|------|
| `Hash` | `DualHash64` | +0 (16B) | DualHash64 of the referenced formula |
| `ImmCount` | `byte` | +16 (1B) | Immediate count of this link |
| `InstCount` | `ushort` | +17 (2B) | Instruction count of this link |
| `Type` | `FluxType` | +19 (1B) | Formula or Modifier |
| `VarSlotCount` | `ushort` | +20 (2B) | Variable slot count of this link |

Constructor: `new VffLinkEntry(hash, immCount, instCount, type, varSlotCount)`

### VffOverride\<TData\>

Resolved parameter override metadata.

| Field | Type | Description |
|------|------|------|
| `GlobalSlot` | `int` | Immediate global index in the merged pipeline |
| `Kind` | `VffOverrideKind` | Override type |
| `ConstantValue` | `TData` | Hard-coded value when Kind=Constant (default otherwise) |

Constructor: `new VffOverride<TData>(globalSlot, kind, constantValue)`

### VffResolveResult\<TData, TOper\>

VFF resolution result.

| Field | Type | Description |
|------|------|------|
| `Formula` | `FluxFormula<TData, TOper>` | Resolved chain formula (can be passed to `Instantiate()`) |
| `Overrides` | `VffOverride<TData>[]` | Parameter override list (empty array = pure reference, no overrides) |

## Enum

### VffOverrideKind

```csharp
public enum VffOverrideKind : byte
{
    Inject  = 0,   // Injected by caller via Injector at evaluation time
    Constant = 1,  // Hard-coded to a fixed value at VFF definition time
}
```

## Methods

### IsVff

```csharp
public static bool IsVff(ReadOnlySpan<byte> bytes)
```

Checks whether a byte span is a VFF entry. Compares the first 4 bytes against `"VFF\0"`. Inlined via `AggressiveInlining`.

### Resolve

```csharp
public static VffResolveResult<TData, TOper> Resolve<TData, TOper>(
    DualHash64 vffHash)
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

Reads a VFF entry from `FormulaCache` and recursively resolves it into a chain formula.

**Resolution flow:**

1. Look up VFF bytecode from cache by `vffHash`
2. Validate magic + version
3. Iterate over the LinkTable, for each link:
   - If the target is a regular formula → build a `ChainLink` with SlotIndex offset by `cumImm`
   - If the target is another VFF → **recurse**, with SlotIndex and GlobalSlot auto-offset
4. Parse the OverrideTable, merging current-level and recursively-flattened overrides
5. Merge variable slots from all links
6. Return `VffResolveResult`

**Exceptions:**

| Condition | Exception Message |
|------|------|
| Cache miss | `"VFF entry not found in cache for hash: …"` |
| Magic mismatch | `"Blob entry is not a VFF (magic mismatch). Hash: …"` |
| Unsupported version | `"Unsupported VFF version: … Expected: 1."` |
| Referenced formula not in cache | `"VFF link [i] references entry not in cache. Hash: …"` |
| Circular reference | `"Circular VFF reference detected: link [i] references VFF …"` |
| Override dataLen mismatch | `"VFF override [i] constant data length mismatch: expected …, got …"` |

**Cycle detection:** Uses a `HashSet<DualHash64>` to maintain the recursion stack. Throws `InvalidOperationException` when an already-visited hash is encountered. Removes from the stack on return, allowing different branches in a DAG to share the same sub-VFF.

## Usage Example

```csharp
// Assume the blob contains these entries:
//   hash_a: formula "[atk] * 2"     (variable atk, slot 0)
//   hash_b: formula "[def] + 10"    (variable def, slot 0)
//   hash_v: VFF referencing hash_a + hash_b, overriding slot 0 to fixed value 100

var result = VffFormat.Resolve<float, FloatOp>(hash_v);

// result.Formula is a chain formula with 2 links
// ChainLink is an internal implementation detail — users don't access it directly
// result.Formula.ImmediateCount == 2  (1 imm per link)

// result.Overrides contains parameter overrides defined in the VFF
// Pass overrides to FluxInstance during Instantiate

var instance = assembler.Instantiate(result.Formula, jit: true);
// Apply overrides...
float value = instance.Run();
```

## Internals

`ResolveLinks<TData, TOper>(vffBytes, visited)` is the core recursive method, returning `(ChainLink[], VffOverride<TData>[], totalImm)`.

- **Nested VFF flattening**: Recursively expands child VFF links; SlotIndex and GlobalSlot are offset by the current `cumImm` to ensure consecutive indices in the merged pipeline
- **Variable slot merging**: All links' `VariableSlot[]` arrays are concatenated in order, with SlotIndex already corrected by offset
- **Override merging**: The current VFF's own overrides come first, followed by recursively-flattened ones; all `GlobalSlot` values are already offset

## See Also

- [FluxFormula](./flux-formula) — Chain formulas and the Connect mechanism
- [FormulaCache](./formula-cache) — Bytecode cache (data source for VFF resolution)
- [FormulaFormat](./formula-format) — Formula bytecode format (`ReadVariableSlots`, `ReadHeader`)
