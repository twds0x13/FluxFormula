# FluxFormula / FluxModifier

Immutable bytecode containers. `FluxFormula<TData, TDef>` is a complete formula (evaluable standalone); `FluxModifier<TData, TDef>` is a fragment missing its first operand (can only be chained or converted to Formula).

## Signatures

```csharp
// Complete formula — evaluable
public readonly struct FluxFormula<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>

// Modifier — missing first operand, not evaluable standalone
public readonly struct FluxModifier<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
```

## FluxFormula Fields

| Field | Type | Description |
|------|------|------|
| `Count` | `int` | Number of instructions (including trailing Return) |
| `ImmediateCount` | `int` | Number of Immediate instructions; upper bound for `SetIndex()` |
| `VariableSlots` | `VariableSlot[]` | Variable name to slot index mapping table, populated by the Lexer path |
| `MaxRegister` | `byte` | Compile-time max register index (0 = unanalyzed, fallback to full 255) |

> `Type` field is `internal`. The type identity is guaranteed by the struct type itself — `FluxFormula` is always a Formula, `FluxModifier` always a Modifier.

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxFormula<TData, TDef>` | Empty formula (Count=0), for Connect edge cases |

## FluxModifier Properties

| Property | Type | Description |
|------|------|------|
| `Count` | `int` | Number of instructions |
| `ImmediateCount` | `int` | Number of Immediate instructions |
| `VariableSlots` | `VariableSlot[]` | Variable slot mapping |
| `MaxRegister` | `byte` | Max register index |
| `IsChained` | `bool` | Whether chained |
| `ChainLength` | `int` | Number of chain links |

| Static Member | Type | Description |
|------|------|------|
| `Empty` | `FluxModifier<TData, TDef>` | Empty Modifier, identity element for Connect |

## Structs

### ChainLink

A single link in a chain formula. Stores bytecode reference and metadata for the formula fragment, with the `DualHash64.Key` used to look up its JIT delegate from cache. Public since 2.0.

| Field | Type | Description |
|------|------|------|
| `Key` | `DualHash64` | Bytecode hash — cache key for delegate lookup |
| `Bytecode` | `Instruction[]` | Bytecode reference (points to original formula's Instruction[], non-copying) |
| `InstructionCount` | `int` | Number of Instructions |
| `ImmediateCount` | `int` | Immediate count for this fragment (used for SetIndex offset calculation) |
| `VarSlots` | `VariableSlot[]` | Variable slots for this fragment |
| `MaxRegister` | `byte` | Max register index for this fragment (0 = unanalyzed) |

> `Type` field is `internal`.

## Construction

Constructors are `internal`. Users generate instances via `FluxAssembler.Compile()` or use `Empty` for an empty instance.

## FluxFormula Methods

### Connect

```csharp
public FluxFormula<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

Chains the current formula with a Modifier. Does not merge bytecode — appends `ChainLink` reference slices.

- Guard clause: if either side is empty, returns the other directly
- **`next` is guaranteed to be a Modifier by the type system.** Call `.ToModifier()` on a Formula to strip its first operand before connecting
- See [ChainLink Deep Dive](../technical/chainlink-deep-dive)

### ToModifier

```csharp
public FluxModifier<TData, TDef> ToModifier()
```

Converts Formula to Modifier. Removes the first Immediate instruction and its data slots, renames its destination register to R1 (Bus). Returns self wrapped if already a Modifier internally. Chain formulas are converted to atomic first.

> **v3.0 breaking change:** Renamed from `ToMultiplier()` to `ToModifier()`. Old name retained as `[Obsolete]`.

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Converts Modifier to Formula. Inserts an Immediate instruction named `varName` in place of R1 input.

### Other Methods

`ToAtomic`, `GetByteHash`, `Raw`, `ToBytes`, `FromBytes`, `IsChained`, `ChainLength`, `GetChainLinks` — same as v2.x. See XML doc comments for details.

## FluxModifier Methods

### Connect

```csharp
public FluxModifier<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

Chains two Modifiers together. Result is still a Modifier (still missing first operand).

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Modifier→Formula: inserts a named variable to replace the R1 input. This is the only way to convert a `FluxModifier` into an evaluable `FluxFormula`.

### Other Methods

`Raw`, `ToBytes`, `GetByteHash`, `GetChainLinks`, `FromBytes` — same as `FluxFormula` equivalents.

## See Also

- [FluxAssembler](./flux-assembler) — compilation entry point that produces FluxFormula
- [FluxInstance](./flux-instance) — streaming executor after instantiation
- [Instruction](./instruction) — 8-byte instruction struct
- [FormulaFormat](./formula-format) — bytecode serialization format
