---
layout: home

hero:
  name: "FluxFormula"
  text: Unity Formula Compilation & Execution Framework
  tagline: Custom operator sets, infix-to-bytecode compilation, interpreter or JIT dual-backend execution, zero heap allocation
  actions:
    - theme: brand
      text: Get Started
      link: /en/guide/getting-started
    - theme: alt
      text: API Reference
      link: /en/api/overview

features:
  - title: Zero-GC Hot Path
    details: ref struct, stackalloc, and unsafe pointer operations. Zero heap allocations at execution time. Only one Instruction[] allocation at compile time; subsequent execution runs purely on the stack.
  - title: Dual-Backend Execution
    details: Interpreter for full platform compatibility. JIT compiles to delegates via LINQ Expression Trees. AOT platforms auto-degrade without manual switching.
  - title: Custom Instruction Sets
    details: Implement the IFluxJITDefinition interface to define domain operators. A single implementation yields both interpreter and JIT execution paths.
  - title: Compact Bytecode
    details: Instruction is an 8-byte fixed-size struct with explicit LayoutKind.Explicit memory layout. 256 virtual registers, max arity of 6.
---

## Performance at a Glance

BenchmarkDotNet on Intel Core Ultra 9 275HX, .NET 9, ShortRun:

| Stage | Simple | Complex | Allocation |
|------|--------|---------|------------|
| Lexer | ~140 ns | ~410 ns | 392–1080 B |
| Compile | ~32 ns | ~110 ns | 112–496 B |
| Interpreter Eval | ~20 ns | ~30 ns | **0 B** |
| JIT Eval | ~1.7 ns | ~4 ns | **0 B** |

One-time compilation cost. Execution: zero heap allocation. JIT is 5–11× faster than the interpreter.
