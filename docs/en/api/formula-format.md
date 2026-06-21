# FormulaFormat

Atomic formula (`.ff`) bytecode format definition and read/write helpers.

## Byte Layout

```
Header (14B): Count(4 LE) + Type(1) + ImmediateCount(4 LE) + VarSlotCount(4 LE) + MaxRegister(1)
Body:         Instruction[Count] (Count × 8B)
Tail:         VariableSlot[] — each: NameLen(4 LE) + UTF8 + SlotIndex(4 LE)
```

Relationship with `VffFormat`: VFF entries are distinguished from formula entries by the `"VFF\0"` magic. The reader calls `VffFormat.IsVff()` first to determine the type.

## Constants

| Constant | Value | Description |
|------|------|------|
| `HeaderSize` | `14` | Header size in bytes |
| `InstructionOffset` | `14` | Instruction region start offset (= HeaderSize) |

## Static Properties

| Property | Type | Description |
|------|------|------|
| `InstructionSize` | `int` | Bytes per Instruction = `sizeof(Instruction)`. Auto-tracks struct size changes |

## Static Methods

### DataSlots\<TData\>

```csharp
public static int DataSlots<TData>() where TData : unmanaged
```

Number of Instruction slots occupied by a TData value = `ceil(sizeof(TData) / sizeof(Instruction))`. Single source of truth for all dataSlots computations in the project.

```csharp
// float(4B) / Instruction(8B) = 1 slot
int slots = FormulaFormat.DataSlots<float>(); // 1

// Vector3(12B) / Instruction(8B) = 2 slots
int vecSlots = FormulaFormat.DataSlots<Vector3>(); // 2
```

### ReadHeader

```csharp
public static FormulaHeader ReadHeader(ReadOnlySpan<byte> data)
```

Reads the 14-byte header from bytecode, returning a `FormulaHeader` struct.

### ReadVariableSlots

```csharp
public static VariableSlot[] ReadVariableSlots(
    ReadOnlySpan<byte> data, int baseSlotOffset = 0)
```

Reads the variable slot list from the bytecode tail. `baseSlotOffset` offsets SlotIndex for chain formulas.

### WriteHeader

```csharp
public static void WriteHeader(byte[] data, ref int offset, FormulaHeader header)
```

Writes a `FormulaHeader` into a byte array, advancing `offset` by 14 bytes.

### GetInstructionSpan

```csharp
public static ReadOnlySpan<Instruction> GetInstructionSpan(ReadOnlySpan<byte> data)
```

Extracts the Instruction region from full bytecode (skipping the 14-byte header), returning `ReadOnlySpan<Instruction>`.

### IsFormula

```csharp
public static bool IsFormula(ReadOnlySpan<byte> bytes)
```

Checks whether a byte span is a formula entry (not VFF, no `"VFF\0"` magic). Complementary to `VffFormat.IsVff()`.

## FormulaHeader Struct

| Field | Type | Description |
|------|------|------|
| `Count` | `int` | Number of Instructions |
| `Type` | `FluxType` | Formula or Modifier |
| `ImmediateCount` | `int` | Number of Immediate instructions |
| `VarSlotCount` | `int` | Number of variable slots |
| `MaxRegister` | `byte` | Compile-time max register index |

## Steps When Changing Instruction Struct Size

1. Adjust `FieldOffset` and `Raw` field in `Instruction.cs`
2. Update `FluxFormula.ToBytes()` / `FromBytes()` serialization
3. Regenerate all blobs (`FluxBlobBuilder.Build()`)
4. Run the full test suite

`InstructionSize` and `DataSlots<TData>` auto-track via `sizeof` — no manual updates needed.

## See Also

- [VffFormat](./vff-format) — VFF entry format
- [DualHash64](./dualhash64) — Bytecode integrity verification
- [BinaryFormat](./overview) — Little-endian binary I/O primitives (API overview)
