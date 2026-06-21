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
| `IsChained` | `bool` | Whether this instance is a chain formula (contains multiple ChainLinks) |
| `ChainLength` | `int` | Number of chain links (1 for non-chained formulas) |

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxFormula<TData, TOper>` | Empty formula (Count=0), for Connect edge cases |

## Methods

### Connect

```csharp
public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
```

Chains two formulas. Does not merge bytecode — appends `ChainLink` reference slices. Physical concatenation is deferred to evaluation time.

- Guard clause: if either side is empty, returns the other directly
- If `next` is not a Modifier, it is kept as-is (first operand does not read from R1). To let `next` consume the current formula's output, explicitly call `next.ToMultiplier()`
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

### IsChained / ChainLength

```csharp
public bool IsChained { get; }
public int ChainLength { get; }
```

`IsChained`: whether the formula is chained (holds a `ChainLink[]` internally). `ChainLength`: number of links in the chain; 0 for atomic formulas.

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

Returns a read-only view of the `Instruction[]` buffer, exposing only the valid region of `Count` instructions.

### ToBytes

```csharp
public byte[] ToBytes()
```

Serializes the formula to a byte array. Format: 14-byte header (Count(4) + Type(1) + ImmediateCount(4) + VarSlotCount(4) + MaxRegister(1)) + instruction region (Count × InstructionSize bytes, each writing the Raw field) + variable slot region (each: nameLen + UTF8 name + slotIndex). Format definition is centralized in `FormulaFormat`; byte-level I/O is unified via `BinaryFormat`.

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

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single> [Type: Formula, Instructions: 4]"
```

## Construction

The constructor is `internal`. Users generate instances via `FluxAssembler.Compile()` or use `FluxFormula<TData, TOper>.Empty` for an empty instance.
