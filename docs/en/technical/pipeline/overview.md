# Pipeline Overview

FluxFormula's compilation and execution pipeline is divided into four stages: **Lex → Compile → Instantiate → Run**. Adjacent stages transfer state through explicit type boundaries. Each layer has independent responsibilities; upper layers are unaware of lower-layer implementation details.

## Stage Overview

```
String / Token[]
  │
  ├─ 1. Lex ────────────────────────────────────────────
  │   FluxLexer.Lex(string) → LexResult<TData, TDef>
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
  │   FluxFormula.Connect() → chain formula (ChainLink[])
  │   GetChainLinks() → VffFormat.ToBytes() → .vff bytecode
  │   Output: VFF byte array (saved as standalone file or embedded in blob)
  │
  ├─ 3. Instantiate ────────────────────────────────────
  │   FluxAssembler.Instantiate(FluxFormula) → FluxInstance<TData, TDef, TDef>
  │   Internal: build FluxInjector + optional JIT delegate compilation
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
| Lex → Compile | `string` | `LexResult<TData, TDef>` | LexResult is immutable |
| Compile → Instantiate | `LexResult` / `FluxToken[]` | `FluxFormula<TData, TDef>` | FluxFormula is immutable |
| Instantiate → Run | `FluxFormula` | `FluxInstance<TData, TDef, TDef>` | Instance is mutable (Set) |
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
- IL2CPP / AOT platforms do not support `Expression.Compile()`
- `Instantiate(jit: true)` calls JIT compilation inside try-catch
- On catching `PlatformNotSupportedException`, calls `FluxPlatform.DisableJit()`
- Subsequent calls in the same process go directly to the interpreter, skipping try-catch

### 4. Run — Dual-Backend Execution

**Interpreter path**:
- `stackalloc` allocates 256 TData registers (64-byte aligned)
- `fixed` pointer pins the bytecode buffer
- Instruction-by-instruction loop; short-circuit on R0 non-default

**JIT path**:
- Delegate is pre-compiled (or retrieved from cache)
- Injected payload array is passed in
- No loop, no branch misprediction — a single delegate invocation

**Why does the interpreter still need to exist?**
- AOT platforms (IL2CPP, iOS, WebGL) do not support `Expression.Compile()`
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

## Next Steps

- [Data Injector](./injector.md) — internal mechanism of Set/SetIndex
