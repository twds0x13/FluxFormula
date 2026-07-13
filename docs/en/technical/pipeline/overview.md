# Pipeline Overview

FluxFormula's compilation and execution pipeline is divided into four stages: **Lex → Compile → Instantiate → Run**. Adjacent stages transfer state through explicit type boundaries. Each layer has independent responsibilities; upper layers are unaware of lower-layer implementation details.

## Stage Overview

```
String / Token[]
  │
  ├─ 1. Lex ────────────────────────────────────────────
  │   FluxLexer.Lex(string) → LexResult<TData>
  │   Output: FluxToken[] + variable name list
  │   Allocation: Token array + variable name strings (one-time)
  │
  ├─ 2. Compile ────────────────────────────────────────
  │   FluxAssembler.Compile(LexResult) → FluxFormula<TData, TDef>
  │   Internal: FluxCompiler (shunting-yard) → Instruction[] bytecode
  │   Output: FluxFormula (immutable container holding Instruction[] buffer)
  │   Allocation: Instruction[] buffer (one-time)
  │
  ├─ 2b. Connect / VFF Encode ──────────────────────────
  │   FluxFormula.Connect() → FluxChain (ChainLink[])
  │   FluxChain.GetLinks() → VffFormat.ToBytes() → .vff bytecode
  │   Output: VFF byte array (saved as standalone file or embedded in blob)
  │
  ├─ 3. Instantiate ────────────────────────────────────
  │   FluxAssembler.Instantiate(FluxFormula) → FluxInstance<TData, TDef>
  │   Internal: build FluxJITInjector (JIT path) or FluxInjector (interpreter path) + JIT delegate compilation (IL first → Expression fallback → interpreter)
  │   Output: FluxInstance (ref struct, stack-allocated)
  │   Allocation: JIT delegate compilation (cacheable), Injector metadata (stack)
  │
  └─ 4. Run ────────────────────────────────────────────
      FluxInstance.Set(...).Run() → TData
      Internal: interpreter or JIT delegate executes bytecode
      Output: computation result
      Allocation: 0 B (zero GC on hot path)
```

## Type Boundaries Between Stages

| Boundary | Input Type | Output Type | Immutability |
|----------|-----------|-------------|--------------|
| Lex → Compile | `string` | `LexResult<TData>` | LexResult is immutable |
| Compile → Instantiate | `LexResult` / `FluxToken[]` | `FluxFormula<TData, TDef>` | FluxFormula is immutable |
| Instantiate → Run | `FluxFormula` | `FluxInstance<TData, TDef>` | Instance is mutable (Set) |
| Run → Result | — | `TData` | Value type result |

Key design: **Compilation products (FluxFormula) are immutable**, cacheable in FormulaCache and retrievable by DualHash64. Instantiation products (FluxInstance) are lightweight ref structs, stack-allocated, recreated for each evaluation.

## Design Decisions per Stage

### 1. Lex — Hand-Written Span Scanner

**Why not regex?**
- Regex in .NET produces internal state machine and capture group allocations, violating the zero-GC goal
- A hand-written `ReadOnlySpan<char>` scanner is zero-allocation (except output arrays), with controllable performance
- Operator matching requires longest-symbol-first (`**` before `*`); regex alternation does not guarantee match order

**Why two-pass scanning?**
- First pass: split by symbols into Tokens, record positions
- Second pass: detect juxtaposition (e.g., `2(atk)`), insert implicit operators
- Two separated passes keep each pass's logic simple; O(2n) is still linear complexity

### 2. Compile — Shunting-Yard Algorithm

**Why shunting-yard over recursive descent?**
- Shunting-yard naturally handles precedence and associativity without manual recursion levels
- Operator precedence and associativity are injected via `IFluxDefinition`; the algorithm is operator-agnostic
- The generated RPN (Reverse Polish Notation) directly maps to the register VM's execution order

**Why conservative Instruction[] buffer sizing?**
- `tokens.Length * (1 + dataSlots) + 1`: each token produces at most one instruction header + TData slots
- Pure operator tokens waste data slots, but dynamic resizing with its GC cost is avoided
- Trimmed at the end via `buffer.AsSpan(0, actualCount).ToArray()`

### 3. Instantiate — Compile-Execute Separation

**Why separate Compile and Instantiate?**
- Compile once, evaluate with different parameters repeatedly: cache FluxFormula, Instantiate repeatedly
- JIT delegate compilation is expensive; separation allows delegate caching in JitCache
- Instantiate returns a ref struct with stack-frame-limited lifetime — separation prevents the long-lived Formula from being stack-constrained

**JIT auto-degradation mechanism**
- JIT delegate compilation has two paths: IL emission (`FluxILCompiler`, preferred on Mono/CoreCLR) and Expression Tree (`FluxExprCompiler`, universal fallback)
- IL2CPP / AOT platforms support neither `Expression.Compile()` nor `DynamicMethod`
- `CompileDelegate` degrades in three tiers: IL → Expression → interpreter
- After first failure, `FluxPlatform.DisableJit()` is set; subsequent calls in the same process skip JIT entirely

### 4. Run — Dual-Backend Execution

**Interpreter path**:
- `stackalloc` dynamically allocates `regCount` TData registers (64-byte aligned); `regCount` is determined by scanning the bytecode on demand
- `fixed` pointer pins the bytecode buffer
- Instruction-by-instruction loop; short-circuit on R0 non-default

**JIT path**:
- Delegate is pre-compiled (via IL emission or Expression Tree, retrieved from cache)
- Injected payload array is passed in
- No loop, no branch misprediction — a single delegate invocation

**Why does the interpreter still need to exist?**
- AOT platforms (IL2CPP, iOS, WebGL) do not support JIT compilation
- The interpreter is the universal fallback, zero platform dependencies
- Cold-start scenarios: first JIT compilation has latency; the interpreter executes immediately

## Cache Interception Points in the Pipeline

```
Compile ──→ DualHash64.Compute(bytecode) ──→ FormulaCache.Put(hash, ptr, len)
                                                │
Instantiate ──→ FormulaCache.Instance.TryGetDelegate(hash) ──→ hit → reuse delegate
                                                │
                                                └──→ miss → JIT Compile → PutDelegate
```

The cache layer forms a transparent acceleration layer between the Compile and Instantiate stages. Users do not need to be aware of the cache's existence — `Instantiate(jit: true)` directly returns a pre-compiled delegate on cache hit, or performs full JIT compilation and writes to cache on miss.

## Other Subsystems

Beyond the main four-stage pipeline, the following subsystems provide compile-time code generation, runtime gradual evaluation, and persistence:

- **[LiteralScanner Source Generator](./literal-scanner-sg.md)**: generates dedicated Span literal scanners from attribute declarations at compile time. `FluxLexer` prioritizes generated scanners at construction time.
- **[Blob Registry](../blob-registry.md)**: binary distribution pipeline for pre-compiled formulas. `IFluxBlobRegistry` + `BlobRegistryGenerator` + `FluxBlob.Load/Unload` additive model supports multiple mods.
- **[VFF Persistence Format](../vff-format.md)**: persistent form of `ChainLink[]`. References formulas in blobs by `DualHash64`, supports parameter overrides and recursive assembly.
- **[Curry Evaluator](./curry-evaluator.md)**: functional State→State gradual binding. Together with `FluxEvaluator` (hot path) and `FluxStepEvaluator` (step debug), forms the three-evaluator architecture.
- **[JIT Injector](./jit-injector.md)**: `FluxJITInjector` (2 fields, zero branches) as a separate type, split from `FluxInjector` in v5.7.1.

## Next Steps

- [Data Injector](./injector.md) — internal mechanism of Set/SetIndex
- [JIT Injector](./jit-injector.md) — FluxJITInjector hot-path injection
