# FluxChain

A chain formula that cannot be evaluated directly. Produced by `FluxFormula.Connect()` or `FluxModifier.Connect()`, it stores multiple bytecode segments linked together. Convert to `FluxFormula` via `ToAtomic()` before evaluation, or pass directly to `FluxAssembler.Instantiate(FluxChain)` for per-link execution.

## Signature

```csharp
public readonly struct FluxChain<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## Acquisition

`FluxChain` is not constructed directly by users. Obtain it via:

```csharp
// FluxFormula.Connect(FluxModifier) → FluxChain
FluxChain<float, MathDef> chain = formula.Connect(modifier);

// FluxModifier.Connect(FluxModifier) → FluxChain
FluxChain<float, MathDef> modChain = modifier1.Connect(modifier2);

// VffFormat resolution result
var result = VffFormat.Resolve<float, MathDef>(hash);
FluxChain<float, MathDef> vffChain = result.Chain;
```

## Properties

| Property | Type | Description |
|------|------|------|
| `Length` | `int` | Number of links in the chain. 0 for empty |
| `Empty` | `FluxChain<TData, TDef>` (static) | Empty chain (Length=0), identity element for Connect |

## Methods

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

Appends a Modifier to the end of the chain. Returns a new `FluxChain` (original unchanged).

- Returns self when `next` is empty (Count=0)
- Returns single-link `FluxChain` when current chain is empty
- Chainable: `chain.Connect(m1).Connect(m2).Connect(m3)`

### ToAtomic

```csharp
public FluxFormula<TData, TDef> ToAtomic()
```

Concatenates all link bytecodes into a single `Instruction[]`, returning an atomic `FluxFormula`. The merged formula can then be `Instantiate`d and `Run`.

- Single heap allocation (`new Instruction[totalCount]`)
- `FluxAssembler.Instantiate(FluxChain)` auto-invokes this for long chains (>8)

### GetLinks

```csharp
public ReadOnlySpan<ChainLink> GetLinks()
```

Returns a read-only view of the chain links as a `ChainLink` span. Zero copy.

```csharp
var links = chain.GetLinks();
foreach (var link in links)
    Console.WriteLine($"Key={link.Key}, Instructions={link.InstructionCount}");
```

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

Computes the combined hash for the chain. Used for cache lookup — differs from `ToAtomic().GetByteHash()` (chain hash reflects link composition; atomic hash reflects merged bytecode).

## Evaluation Paths

| Path | Behavior |
|------|------|
| `Instantiate(chain, jit: false)` | Short chains (≤8): per-link interpreter evaluation via R1 bus. Long chains: auto-calls `ToAtomic` then single evaluation |
| `Instantiate(chain, jit: true)` | Per-link JIT delegate chaining — each link independently hits the delegate cache |
| `Instantiate(chain.ToAtomic(), ...)` | Merged first, then evaluated as a normal atomic formula |

## Distinction from FluxFormula

| Trait | `FluxFormula` | `FluxChain` |
|------|:---:|:---:|
| Directly evaluable | Yes | No (requires `Instantiate(FluxChain)` or `ToAtomic()`) |
| `Raw()` | O(1), zero alloc | Not available |
| `ToBytes()` | O(1) | Not available |
| Internal representation | `Instruction[]` (atomic bytecode) | `ChainLink[]` (reference slices) |
| Produced by | `Compile()`, `FromBytes()`, `ToAtomic()` | `Connect()`, VFF resolution |
| Chaining | `Connect()` returns `FluxChain` | `Connect()` returns `FluxChain` |

## See Also

- [FluxFormula / FluxModifier](./flux-formula) — atomic formulas and modifiers
- [FluxAssembler](./flux-assembler) — `Instantiate(FluxChain)` overload
- [VffFormat](./vff-format) — VFF resolution produces `FluxChain`
- [ChainLink Deep Dive](../technical/chainlink-deep-dive) — per-link JIT caching internals
