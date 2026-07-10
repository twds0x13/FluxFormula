# FluxFormula / FluxModifier / FluxChain

Immutable bytecode containers. `FluxFormula<TData, TDef>` is a complete formula (evaluable standalone); `FluxModifier<TData, TDef>` is a fragment missing its first operand (can only be chained or converted to Formula); `FluxChain<TData, TDef>` is a multi-segment bytecode sequence built by repeated `Connect` calls.

## Signatures

```csharp
// Complete formula — evaluable standalone
public readonly struct FluxFormula<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>

// Modifier — missing first operand, not evaluable standalone
public readonly struct FluxModifier<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>

// Chain formula — not directly evaluable
public readonly struct FluxChain<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## FluxFormula Properties

| Property | Type | Description |
|------|------|------|
| `Count` | `int` | Number of instructions (including trailing Return) |
| `ImmediateCount` | `int` | Number of Immediate instructions |
| `VariableSlots` | `VariableSlot[]` | Variable name to slot index mapping, populated by the Lexer path |
| `MaxRegister` | `byte` | Compile-time max register index (0 = unanalyzed, fallback to full 255) |

> `Type` field is `internal`. Type identity is guaranteed by the struct type itself — `FluxFormula` is always atomic.

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxFormula<TData, TDef>` | Empty formula (Count=0), identity element for Connect |
| `FromBytes(byte[])` | `FluxFormula<TData, TDef>` | Deserialize from bytecode |
| `FromBytes(ReadOnlySpan<byte>)` | `FluxFormula<TData, TDef>` | Deserialize from bytecode span (zero-allocation) |

## FluxModifier Properties

| Property | Type | Description |
|------|------|------|
| `Count` | `int` | Number of instructions |
| `ImmediateCount` | `int` | Number of Immediate instructions |
| `VariableSlots` | `VariableSlot[]` | Variable slot mapping |
| `MaxRegister` | `byte` | Max register index |

> `FluxModifier` has no `Instantiate()` method — any code attempting to independently evaluate a Modifier won't compile. Chain structure is accessed via `FluxChain.Length` and `FluxChain.GetLinks()`.

## Static Members

| Member | Type | Description |
|------|------|------|
| `Empty` | `FluxModifier<TData, TDef>` | Empty Modifier, identity element for Connect |

## Construction

Constructors are `internal`. Users generate instances via `FluxAssembler.Compile()`, `Connect()` returns `FluxChain`, or use `Empty` for an empty instance.

## FluxFormula Methods

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

Chains the current formula with a Modifier. Does not merge bytecode — returns a `FluxChain`. Physical concatenation is deferred to evaluation time.

- Guard clause: returns single-link `FluxChain` when `next` is empty
- **`next` is guaranteed to be a Modifier by the type system.** Call `.ToModifier()` on a Formula before connecting
- See [ChainLink Deep Dive](../technical/chainlink-deep-dive)

### ToModifier

```csharp
public FluxModifier<TData, TDef> ToModifier()
```

Converts Formula to Modifier. Removes the first Immediate instruction and its data slots, renames its destination register to R1 (Bus).

> **v3.0 breaking change:** Renamed from `ToMultiplier()` to `ToModifier()`. Old name retained as `[Obsolete]`.

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Converts Modifier to Formula. Inserts a named Immediate instruction in place of R1 input.

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

Computes the `DualHash64` hash of the bytecode. Atomic formulas always serialize before hashing.

### Raw / ToBytes / ToString

```csharp
public ReadOnlySpan<Instruction> Raw()              // O(1), zero allocation
public byte[] ToBytes()                             // Serialize to byte array
public override string ToString()
```

## FluxModifier Methods

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

Chains two Modifiers together, returns `FluxChain`. Result is still missing its first operand — call `.ToAtomic()` or connect to a `FluxFormula` before evaluation.

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Modifier→Formula: inserts a named variable to replace the R1 input. This is the only way to convert a `FluxModifier` into an evaluable `FluxFormula`.

### Raw / ToBytes / GetByteHash / ToString

```csharp
public ReadOnlySpan<Instruction> Raw()              // O(1), never allocates
public byte[] ToBytes()                             // Serialize to byte array
public DualHash64 GetByteHash()                     // Compute bytecode hash
public override string ToString()                   // Debug string representation
```

### FromBytes

```csharp
public static FluxModifier<TData, TDef> FromBytes(byte[] data)
public static FluxModifier<TData, TDef> FromBytes(ReadOnlySpan<byte> data)
```

Deserialize a Modifier from bytecode.

## FluxChain Methods

See [FluxChain API documentation](./flux-chain).

| Method | Return Type | Description |
|------|------|------|
| `Connect(FluxModifier)` | `FluxChain` | Append a Modifier to the end of the chain |
| `ToAtomic()` | `FluxFormula` | Explicitly merge all links into an atomic formula |
| `GetLinks()` | `ReadOnlySpan<ChainLink>` | Read-only view of chain links |
| `GetByteHash()` | `DualHash64` | Combined chain bytecode hash |

| Property | Type | Description |
|------|------|------|
| `Length` | `int` | Number of links in the chain |
| `Empty` | `FluxChain` (static) | Empty chain (Length=0), identity element for Connect |

## Structs

### ChainLink

A single link in a chain formula. Stores bytecode reference and metadata, with the `DualHash64.Key` used to look up its JIT delegate from cache. Accessed via `FluxChain.GetLinks()`.

| Field | Type | Description |
|------|------|------|
| `Key` | `DualHash64` | Bytecode hash — cache key for delegate lookup |
| `Bytecode` | `Instruction[]` | Bytecode reference (points to original formula's Instruction[], non-copying) |
| `InstructionCount` | `int` | Number of Instructions |
| `ImmediateCount` | `int` | Immediate count for this fragment |
| `VarSlots` | `VariableSlot[]` | Variable slots for this fragment |
| `MaxRegister` | `byte` | Max register index for this fragment (0 = unanalyzed) |

> `Type` field is `internal`.

## See Also

- [FluxChain API](./flux-chain) — chain formula dedicated API
- [FluxAssembler](./flux-assembler) — compilation entry point
- [FluxInstance](./flux-instance) — streaming executor
- [Instruction](./instruction) — 8-byte instruction struct
- [ChainLink Deep Dive](../technical/chainlink-deep-dive) — chain evaluation internals
