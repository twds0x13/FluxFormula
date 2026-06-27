# Architecture Decision Records

Key architectural decisions made during FluxFormula's development, with context, options, and rationale.

## ADR-1: Dual Hash (DualHash64)

**Date**: 2026-06-21
**Status**: Accepted

### Context
The compile cache needs a content-addressed storage scheme. Single cryptographic hashes (SHA256) are safe but slow; single non-cryptographic hashes (xxHash64) are fast but vulnerable to structural collisions.

### Options
1. SHA256 — safe, but expensive per formula
2. xxHash64 only — fast, ~2³² birthday collision space
3. xxHash64 + FNV-1a 64 — two orthogonal non-cryptographic hashes

### Decision
**Option 3**. The two hashes have independent internal structures (xxHash: multiply-shuffle-rotate; FNV: XOR-prime-multiply). An attacker must solve simultaneous collision equations. Combined difficulty exceeds the individual ceiling of non-cryptographic hashes.

### Consequences
- 128-bit key adds 16-byte per-entry storage overhead (acceptable)
- `Combine()` provides O(1) chain key computation without re-scanning
- Not cryptographically secure — relies on offset table being compiled into the assembly

---

## ADR-2: Hashes in Offset Table, Not Blob

**Date**: 2026-06-21
**Status**: Accepted

### Context
Compiled formula bytecode is cached as `.ff` files. Hashes must be stored to verify integrity at runtime.

### Options
1. Embed hashes in blob (16-byte header per entry)
2. Store hashes in Source Generator offset table (compiled into assembly)

### Decision
**Option 2**. Hashes inside the blob are self-verifying — an attacker modifying the blob can also modify the hash. The offset table is compiled IL; tampering requires decompilation and recompilation.

### Consequences
- Offset table is compact (24 bytes per entry: offset + length + dualHash)
- Runtime verification: get expected hash from offset table → read bytes from blob → compute → compare
- Source Generator must run before Player Build's C# compilation

---

## ADR-3: Deferred Materialization (ChainLink)

**Date**: 2026-06-21
**Status**: Accepted

### Context
`Connect` originally performed full bytecode concatenation (Array.Copy + new Instruction[]). Each call allocated a merged buffer.

### Options
1. Always merge bytecode (original behavior)
2. Append metadata references only (ChainLink), defer materialization to evaluation

### Decision
**Option 2**. Analogous to LINQ deferred execution. `Connect` appends reference slices to `ChainLink[]`. Physical merge is deferred to `Run()` (short chains, per-link) or `ToAtomic()` (long chains / JIT). The threshold (8 links) moved from Connect to `Instantiate`.

### Consequences
- Connect is zero-allocation (only appends to ChainLink array)
- Dual representation (chain vs atomic) adds code paths, but `Instantiate` centralizes the decision
- Per-link interpreter evaluation and ToAtomic merge produce identical results via mid-program Return semantics

---

## ADR-4: Delegate Caching (GCHandle)

**Date**: 2026-06-21
**Status**: Accepted

### Context
JIT compilation (`Expression.Compile()`) is expensive. The same formula should not be recompiled on every instantiation.

### Options
1. No caching — recompile every time
2. WeakReference on each formula to its delegate
3. Global FormulaCache using GCHandle for delegate storage

### Decision
**Option 3**. `GCHandle.Alloc(func)` → `GCHandle.ToIntPtr()` stored in FormulaCache's `IntPtr` slot. Shares storage with bytecode pointers (distinguished by `DelegateSlot = -2` marker).

### Consequences
- A formula compiles at most once, regardless of instantiation count
- GCHandle lifecycle managed by FormulaCache (freed on eviction/Compact)
- On IL2CPP, `Expression.Compile()` is unsupported; automatic interpreter fallback

---

## ADR-5: Formula ↔ Modifier Conversion

**Date**: 2026-06-21
**Status**: Accepted

### Context
Formula and Modifier are two views of the same bytecode. Connect should support both "B consumes A's output" and "B runs independently".

### Options
1. Implicit conversion through Connect only
2. Explicit `ToMultiplier()` / `ToFormula()` API

### Decision
**Option 2**. Semantics left to the caller: `Connect(A, B.ToMultiplier())` for B consuming A's output; `Connect(A, B)` for B running independently.

### Consequences
- Users have explicit control over chain semantics
- `ToMultiplier`/`ToFormula` are bytecode-transforming operations (allocate new `Instruction[]`)
- `CHAIN_LINK_INTERNAL_` prefix reserved for internal variables

---

## ADR-6: Mid-Program Return Semantics

**Date**: 2026-06-21
**Status**: Accepted

### Context
When ToAtomic merges chain bytecodes, intermediate Return instructions cause the interpreter to exit prematurely. Dropping them loses the R1 bus connection between links.

### Options
1. Drop intermediate Returns + insert explicit register-copy instructions
2. Accept semantic divergence between ToAtomic and per-link paths
3. Modify interpreter: intermediate Returns copy Dest → R1 and continue

### Decision
**Option 3**. When a Return is followed by more instructions (`ip + 1 < raw.Length`), the interpreter copies `regsPtr[Dest]` to `regsPtr[1]` (R1 bus) and continues. In normal bytecode, Return is always the last instruction — existing behavior unchanged.

### Consequences
- ToAtomic preserves all intermediate Returns (no instruction dropping)
- Per-link and ToAtomic evaluation produce identical results for all chain types
- Single branch added to interpreter hot loop (negligible overhead)

---

## ADR-7: Threshold Merge Centralized in Instantiate

**Date**: 2026-06-21
**Status**: Accepted

### Context
Connect originally contained the threshold check (>8 links → merge), coupling "when to merge" with "how to connect".

### Options
1. Threshold in Connect (original behavior)
2. Connect always produces chains, threshold in Instantiate

### Decision
**Option 2**. Connect only describes chain relationships. Instantiate decides whether to merge based on path (JIT vs interpreter) and chain length.

### Consequences
- Simplified Connect code
- Single decision point for merge strategy
- Interpreter: short chains per-link, long chains merged. JIT: per-link delegates chained via SetIndex(0, prevResult)

---

## Adopted (v1.7.0)

| Topic | Notes |
|-------|-------|
| Per-link JIT evaluation | `FluxAssembler.InstantiateJitChain()` compiles each link into an independent delegate; `FluxInstance.RunJitChain()` chains them via `SetIndex(0, prevResult)`. Eliminates forced ToAtomic merging on the JIT path. |
| MaxRegister on-demand allocation | Formula header stores compile-time maximum register number. `FluxEvaluator` and `FluxExprCompiler` allocate registers on demand instead of full 255. |
| FormulaFormat / BinaryFormat centralization | Format definition and byte-level I/O each centralized into a single source file, eliminating 9+ scattered helpers. |
| FluxConfig global configuration | Replaces hardcoded constants (cache capacity, merge threshold, buffer size). Unity integration via `FluxConfigAsset` ScriptableObject auto-injection. |

## Pending

| Topic | Notes |
|-------|-------|
| ChainLink storage format | Currently `Instruction[]` reference. Could use `byte[]` copy to eliminate GC edge cases (low priority). |
| Connect auto-ToMultiplier | Currently manual. Should Connect default to `ToMultiplier` on non-first links? |
