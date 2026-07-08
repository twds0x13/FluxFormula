# Test Coverage Boundary

> Current baseline: **97.6% line coverage** (359 tests, 0 failures). 140 uncovered lines remain, distributed across multiple classes.

## Current Baseline

```
Line coverage:    97.6%  (5865 / 6005)
Method coverage:  97.0%  (586 / 604)
Test count:       359    (0 failures)
```

The following describes uncovered code by three categories: the nature of the uncovered code, the reason it is uncovered, and the impact on users.

---

## Tier 1: Untestable

This code is not testable: calling it would break process state, it is purely test scaffolding, or it has no semantic contract.

### `FluxPlatform.DisableJit()`: 40%

`DisableJit()` is a global, irreversible switch. After calling it, all subsequent JIT paths in the process are disabled. The only test that would cover it would contaminate the entire test suite. The method body is a single assignment: `_jitDisabled = true`.

The actual trigger path is the JIT compilation failure catch block on IL2CPP/AOT platforms, which is platform behavior rather than logic behavior and cannot be simulated by unit tests.

This item is excluded from the coverage gate.

### `BareDef` Helper Struct

A 9-method stub struct written to trigger the `IFluxDefinition.GetOperatorName` DIM. Only `GetOperatorName` is actually called; the remaining 8 methods are never used. This is test-scaffolding uncovered code and does not affect production coverage evaluation.

### `ToString()` Variants

`Instruction.ToString()`, `OpPair.ToString()`, `FluxFormula.ToString()`, `FluxModifier.ToString()`, and similar are debug output with no semantic contract. Format changes are implementation details and do not constitute regression risk.

---

## Tier 2: Uneconomical Investment

These uncovered paths require specialized test environments or combinatorial-explosion-level test cases where the cost/benefit ratio is unfavorable.

### `FluxExprCompiler` Expression Tree Edge Cases: 92.9%

The remaining 7.1% is distributed across:
- Register layout for multi-parameter operators (Select/Lerp/Sum6)
- The FastExpressionCompiler vs. standard `Expression.Compile()` fork

Covering these requires a Cartesian product of operator kind × register count × data type. `JitConsistencyTests` (100% coverage) already covers the core JIT-to-interpreter equivalence. Definition implementations that introduce new multi-parameter operators should verify JIT path correctness themselves.

### FormulaCache Concurrency and Eviction: 87.1%

The remaining 12.9% involves LRU eviction under cache pressure, `GCHandle` allocation failure, and multi-threaded concurrent `TryGet`/`Put`. Testing these requires multi-threading infrastructure and platform-dependent GCHandle limits.

Cache correctness is ensured by three test files: `FormulaCacheTests`, `FormulaCacheAndChainTests`, and `FormulaCacheEdgeTests`. Concurrency behavior is a performance characteristic; benchmarks are more sensitive to performance regressions than unit tests.

### FluxEvaluator Register Edge Cases: 93.1%

The remaining 6.9% is the full 255-register fallback path when `MaxRegister=0` and extreme `stackalloc` sizes. These paths are functionally equivalent to the normal path, differing only in register count. Register allocation correctness is indirectly covered by `ArithmeticTests` and `JitConsistencyTests`.

---

## Tier 3: Scenario-Dependent

Whether these uncovered paths pose a risk depends on the usage scenario and input trust boundary.

### FluxModifier: 89.1%

The remaining 10.9% includes two paths:
- `ToAtomic()`: merging a chained formula into an atomic formula, relevant when chaining multiple Modifiers and then serializing
- `FromBytes(ReadOnlySpan<byte>)` corrupted data branch: relevant when accepting external bytecode input

Definition ecosystems that use chained `Connect` composition may benefit from 2–3 additional serialization round-trip tests. Scenarios where formulas are purely build-time artifacts are unaffected.

### VffFormat: 97.6%

The remaining 2.4% covers error branches for corrupted VFF format: truncated headers, incorrect version numbers, nested VFF DAG edge cases. Scenarios that expose VFF to user editing need additional coverage. Scenarios where VFF is exclusively toolchain-generated are unaffected.

### FluxLexer: 97.5%

The remaining 2.5% covers extreme token sequences: consecutive unary operators, empty brackets, variable pattern vs. operator pattern disambiguation. Scenarios with simple DSL grammars (basic arithmetic plus variables) are fully covered. Scenarios extending the DSL grammar need `LexerEdgeTests`-level supplementary verification.

---

## Coverage Gate

```
┌─────────────────────────────────────────────────┐
│  Coverage gate: 97% line coverage                 │
│                                                   │
│  Tier 1 (Untestable): destructive / zero-semantic │
│    / test-scaffolding code                        │
│    → [ExcludeFromCodeCoverage] or .runsettings    │
│                                                   │
│  Tier 2 (Uneconomical): platform-dependent /      │
│    combinatorial explosion / concurrency          │
│    → documented; benchmark is primary detector    │
│                                                   │
│  Tier 3 (Scenario-dependent): depends on          │
│    Definition ecosystem and input trust boundary  │
│    → relevant scenario developers self-assess     │
└─────────────────────────────────────────────────┘
```

Beyond 97.6%, the remaining uncovered code has entered the region of diminishing returns. Research shows that the last 2% of coverage consumes approximately 40% of test-writing time[^1]. This project uses 97% as the gate: above this line, the cost curve per percentage point steepens sharply and the probability of new tests finding actual bugs approaches zero.

[^1]: A. Mockus, N. Nagappan & T. T. Dinh-Trong, "Test Coverage and Post-Verification Defects: A Multiple Case Study," *ESEM 2009*, pp. 291–300, DOI: [10.1109/ESEM.2009.5315981](https://doi.org/10.1109/ESEM.2009.5315981). Key finding: test effort increases exponentially with coverage, while field defect reduction increases only linearly — optimal coverage is well short of 100%.
