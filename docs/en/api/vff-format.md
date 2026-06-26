# VffFormat

VFF (Virtual FluxFormula) byte format definition, encoder, and parser. VFF entries coexist with formula entries in blobs, distinguished by the `"VFF\0"` magic.

## Purpose

VFF provides two operational directions:

- **Encoding**: `ToBytes()` serializes chain formula references (`ChainLink[]`) and parameter overrides into a VFF byte array. This is the **creation side** — compose long pipelines in the editor, persist them as standalone `.vff` files, and let `FluxBlobBuilder` embed them into the blob.
- **Decoding**: `Resolve()` reads VFF entries from `FormulaCache` within a blob; `FromBytes()` parses from raw byte arrays. Both recursively expand into executable chain formulas without recompilation.

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

### VffResolveResult\<TData, TDef\>

VFF resolution result.

| Field | Type | Description |
|------|------|------|
| `Formula` | `FluxFormula<TData, TDef>` | Resolved chain formula (can be passed to `Instantiate()`) |
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
public static VffResolveResult<TData, TDef> Resolve<TData, TDef>(
    DualHash64 vffHash)
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
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

### ToBytes

```csharp
public static byte[] ToBytes<TData>(
    ChainLink[] links,
    VffOverride<TData>[] overrides)
    where TData : unmanaged
```

Serializes chain formula references into a VFF byte array. Pairs with `FromBytes` — roundtrip guarantees link equivalence.

| Parameter | Type | Description |
|------|------|------|
| `links` | `ChainLink[]` | Chain link array (e.g. from `FluxChain.GetLinks()`) |
| `overrides` | `VffOverride<TData>[]` | Parameter override list (pass empty array for no overrides) |

The output byte layout matches the "Byte Layout" section above: Header ("VFF\0" + Version + LinkCount + OverrideCount + Flags) + LinkTable + OverrideTable. The `HasConstants` flag bit is computed automatically based on whether any override has `Constant` kind.

### FromBytes

```csharp
public static VffResolveResult<TData, TDef> FromBytes<TData, TDef>(
    byte[] data)
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
```

Parses a VFF from a raw byte array, producing a chain formula. Functionally equivalent to `Resolve()`, but takes VFF bytes directly as a parameter rather than looking them up from `FormulaCache`.

Referenced formulas are still resolved through `FormulaCache` — ensure dependent formula bytecode is injected into the cache before calling.

| Parameter | Type | Description |
|------|------|------|
| `data` | `byte[]` | VFF-format byte array (must start with `"VFF\0"` magic) |

**Exceptions:**

| Condition | Exception Message |
|------|------|
| Magic mismatch | `"Data is not a VFF entry (magic mismatch)."` |
| Unsupported version | `"Unsupported VFF version: … Expected: 1."` |
| Referenced formula not in cache | `"VFF link [i] references entry not in cache. Hash: …"` |

## Usage Example

### Creating and Persisting a VFF

```csharp
// Compile two formulas and inject into cache
var fA = assembler.Compile(lexer.Lex("[atk] * 2"));
var fB = assembler.Compile(lexer.Lex("[def] + 10"));
byte[] bytesA = fA.ToBytes(), bytesB = fB.ToBytes();
var hashA = FormulaCache.Instance.Put(bytesA);
var hashB = FormulaCache.Instance.Put(bytesB);

// Build ChainLink references
var links = new[]
{
    new ChainLink { Key = hashA, Bytecode = FormulaFormat.GetInstructionSpan(bytesA).ToArray(),
        InstructionCount = fA.Count, Type = (byte)FluxType.Formula,  // internal enum; 0=Modifier, 1=Formula
        ImmediateCount = fA.ImmediateCount, VarSlots = fA.VariableSlots,
        MaxRegister = fA.MaxRegister },
    new ChainLink { Key = hashB, Bytecode = FormulaFormat.GetInstructionSpan(bytesB).ToArray(),
        InstructionCount = fB.Count, Type = FluxType.Formula,
        ImmediateCount = fB.ImmediateCount, VarSlots = fB.VariableSlots,
        MaxRegister = fB.MaxRegister },
};

// Serialize to VFF byte array
byte[] vffData = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());

// Save as .vff file (via IFluxFileFormatter)
builder.Save(vffData, FluxArtifactKind.Virtual, "AttackDefenseChain.vff");
```

### Parsing a VFF from Bytes

```csharp
// Load bytes from .vff file → resolve
byte[] loaded = File.ReadAllBytes("AttackDefenseChain.vff");
var result = VffFormat.FromBytes<float, MathDef>(loaded);

// result.Formula is a chain formula, ready to execute
var instance = assembler.Instantiate(result.Formula, jit: true);
instance.Set("atk", 100f).Set("def", 50f);
float value = instance.Run();
```

## Internals

`ResolveLinks<TData, TDef>(vffBytes, visited)` is the core recursive method shared by both `Resolve()` and `FromBytes()`, returning `(ChainLink[], VffOverride<TData>[], totalImm)`.

`ToBytes<TData>()` produces exactly the byte layout defined above, using `BinaryFormat` for all multi-byte writes.

- **Nested VFF flattening**: Recursively expands child VFF links; SlotIndex and GlobalSlot are offset by the current `cumImm` to ensure consecutive indices in the merged pipeline
- **Variable slot merging**: All links' `VariableSlot[]` arrays are concatenated in order, with SlotIndex already corrected by offset
- **Override merging**: The current VFF's own overrides come first, followed by recursively-flattened ones; all `GlobalSlot` values are already offset

## See Also

- [FluxFormula](./flux-formula) — Chain formulas and the Connect mechanism
- [FormulaCache](./formula-cache) — Bytecode cache (data source for VFF resolution)
- [FormulaFormat](./formula-format) — Formula bytecode format (`ReadVariableSlots`, `ReadHeader`)
