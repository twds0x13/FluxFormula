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

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxFormula<TData, TOper>` | Empty formula (Count=0), for Connect edge cases |

## Methods

### Connect

```csharp
public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
```

Concatenates two formulas. Removes the trailing Return instruction from the current formula and appends all of `next`'s content.

- Guard clause: if either side is empty, returns the other directly
- Does not remap register numbers; ensure `next` does not overwrite the current formula's register allocation

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

Returns a read-only view of the `Instruction[]` buffer, exposing only the valid region of `Count` instructions.

### ToBytes

```csharp
public byte[] ToBytes()
```

Serializes the formula to a byte array. Format: 13-byte header (Count + Type + ImmediateCount + VariableSlot count) + instruction region (Count × 8 bytes, each writing the Raw long value) + variable slot region (each: nameLen + UTF8 name + slotIndex). Serialization is zero-overhead memcpy with no reflection.

### FromBytes

```csharp
public static FluxFormula<TData, TOper> FromBytes(byte[] data)
```

Deserializes from the byte array produced by `ToBytes()`. No recompilation needed; the bytecode is ready to use.

```csharp
// Persist
byte[] raw = formula.ToBytes();
File.WriteAllBytes("damage.ff", raw);

// Load (zero compilation)
var loaded = FluxFormula<float, FloatOp>.FromBytes(raw);
float r = runner.Instantiate(loaded).Set("atk", 100f).Run();
```

`FromBytes` validates `sizeof(TOper) == 1` during type initialization, throwing `TypeInitializationException` on failure.

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single> [Type: Formula, Instructions: 4]"
```

## Construction

The constructor is `internal`. Users generate instances via `FluxAssembler.Compile()` or use `FluxFormula<TData, TOper>.Empty` for an empty instance.
