# FluxFormula

Immutable bytecode container.

## Signature

```csharp
public readonly struct FluxFormula<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

## Fields

| Field | Type | Description |
|------|------|------|
| `Count` | `int` | Number of instructions (including trailing Return) |
| `Type` | `FluxType` | `Formula` (executable standalone) or `Modifier` (requires Connect) |
| `ImmediateCount` | `int` | Number of Immediate instructions; upper bound for `SetIndex()` |
| `VariableSlots` | `VariableSlot[]` | Variable name to slot index mapping table, populated by the Lexer path |
| `MaxRegister` | `byte` | Compile-time max register index (0 = unanalyzed, fallback to full 255) |

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxFormula<TData, TOper>` | Empty formula (Count=0), for Connect edge cases |

## Structs

### ChainLink

A single link in a chain formula. Stores bytecode reference and metadata for the formula fragment, with the `DualHash64.Key` used to look up its JIT delegate from cache. Public since 2.0.

| Field | Type | Description |
|------|------|------|
| `Key` | `DualHash64` | Bytecode hash — cache key for delegate lookup |
| `Bytecode` | `Instruction[]` | Bytecode reference (points to original formula's Instruction[], non-copying) |
| `InstructionCount` | `int` | Number of Instructions |
| `Type` | `FluxType` | `Formula` or `Modifier` |
| `ImmediateCount` | `int` | Immediate count for this fragment (used for SetIndex offset calculation) |
| `VarSlots` | `VariableSlot[]` | Variable slots for this fragment |
| `MaxRegister` | `byte` | Max register index for this fragment (0 = unanalyzed) |

Advanced users can access the chain structure via `GetChainLinks()` and persist it as a VFF file using `VffFormat.ToBytes()`.

## Construction

The constructor is `internal`. Users generate instances via `FluxAssembler.Compile()` or use `FluxFormula<TData, TOper>.Empty` for an empty instance.

## Methods

### Connect

```csharp
public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
```

Chains the current formula with a Modifier. Does not merge bytecode — appends `ChainLink` reference slices. Physical concatenation is deferred to evaluation time.

- Guard clause: if either side is empty, returns the other directly
- **`next` must be a Modifier** (`next.Type == FluxType.Modifier`); throws `ArgumentException` otherwise. Call `.ToMultiplier()` on the formula to strip its first operand before connecting
- See [ChainLink Deep Dive](../technical/chainlink-deep-dive)

### ToMultiplier

```csharp
public FluxFormula<TData, TOper> ToMultiplier()
```

Converts Formula to Modifier. Removes the first Immediate instruction and its data slots, renames its destination register to 1 (R1). Returns self if already a Modifier. Chain formulas are converted to atomic first.

### ToFormula

```csharp
public FluxFormula<TData, TOper> ToFormula(string varName)
```

Converts Modifier to Formula. Inserts an Immediate instruction named `varName` in place of R1 input, renames R1 references to the new register. Returns self if already a Formula.

### ToAtomic

```csharp
internal FluxFormula<TData, TOper> ToAtomic()
```

Merges a chain formula into an atomic formula. All links' `Instruction[]` arrays are concatenated in full (including intermediate Returns). Called automatically for JIT paths and long chains (>8).

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

Returns the `DualHash64` of the formula's bytecode. For atomic formulas, equivalent to hashing `ToBytes()`. For chain formulas, the sequential `Combine` of all link keys. Used as cache lookup key.

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

Returns a read-only view of the formula's underlying instructions. Chain formulas are automatically merged via `ToAtomic()` before returning, presenting a unified atomic representation externally.

### ToBytes

```csharp
public byte[] ToBytes()
```

Serializes the formula to a byte array. Chain formulas are automatically merged to atomic before serialization. Format: 14-byte header (Count(4) + Type(1) + ImmediateCount(4) + VarSlotCount(4) + MaxRegister(1)) + instruction region (Count × InstructionSize bytes, each writing the Raw field) + variable slot region (each: nameLen + UTF8 name + slotIndex). Format definition is centralized in `FormulaFormat`; byte-level I/O is unified via `BinaryFormat`.

### FromBytes

```csharp
public static FluxFormula<TData, TOper> FromBytes(byte[] data)
public static FluxFormula<TData, TOper> FromBytes(ReadOnlySpan<byte> data)
```

Deserializes from the byte array produced by `ToBytes()`. No recompilation needed; the bytecode is ready to use. The `ReadOnlySpan<byte>` overload enables zero-copy deserialization from pinned memory pointers.

```csharp
// Persist
byte[] raw = formula.ToBytes();
File.WriteAllBytes("damage.ff", raw);

// Load (zero compilation)
var loaded = FluxFormula<float, FloatOp>.FromBytes(raw);
float r = runner.Instantiate(loaded).Set("atk", 100f).Run();

// Zero-copy load from blob pointer
var fromBlob = FluxFormula<float, FloatOp>.FromBytes(blobSpan.Slice(offset, length));
```

`FromBytes` validates `sizeof(TOper) == 1` during type initialization, throwing `TypeInitializationException` on failure.

### IsChained

```csharp
public bool IsChained { get; }
```

Whether the formula is a chain formula (produced by `Connect()`). Chain formulas internally store multiple `ChainLink` entries; atomic formulas return `false`.

### ChainLength

```csharp
public int ChainLength { get; }
```

Number of links in a chain formula. Returns `0` for atomic formulas.

### GetChainLinks

```csharp
public ReadOnlySpan<ChainLink> GetChainLinks()
```

Returns a read-only view of chain links. Returns an empty span for atomic formulas. Advanced users can read the chain structure and persist it as a `.vff` file via `VffFormat.ToBytes()`.

```csharp
var chain = formulaA.Connect(formulaB);
if (chain.IsChained)
{
    var links = chain.GetChainLinks();
    byte[] vffData = VffFormat.ToBytes<float>(links.ToArray(), Array.Empty<VffOverride<float>>());
    builder.Save(vffData, FluxArtifactKind.Virtual, "ComboChain.vff");
}
```

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single> [Type: Formula, Instructions: 4]"
```

## See Also

- [FluxAssembler](./flux-assembler) — compilation entry point that produces FluxFormula
- [FluxInstance](./flux-instance) — streaming executor after instantiation
- [Instruction](./instruction) — 8-byte instruction struct
- [FormulaFormat](./formula-format) — bytecode serialization format
