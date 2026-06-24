# Shunting-Yard Compiler

`FluxCompiler<TData, TDef>` implements Dijkstra's classic shunting-yard algorithm, compiling infix token sequences to postfix bytecode (`Instruction[]`). Core design question: **how to handle operator precedence, bracket pairing, and multi-argument function syntax in a single linear scan?**

## Why Shunting-Yard?

Three advantages for FluxFormula's use case:

1. **Single pass**: O(n) over the input token sequence.
2. **Local decisions**: Precedence and associativity come from Definition — the algorithm doesn't interpret operator semantics.
3. **Natural postfix output**: Output order is directly executable by the stack interpreter, with no intermediate AST.

## Dual-Stack Model

```
Input:  [C(1), Add, C(2), Mul, C(3)]    ← infix tokens
                │
    ┌───────────┴───────────┐
    │   Shunting-Yard        │
    │   operatorStack        │   pending operators
    │   outputQueue (RPN)    │   output bytecode
    └───────────────────────┘
                │
Output: [C(1), C(2), C(3), Mul, Add]    ← postfix bytecode
```

The operator stack uses `Instruction*` + `stackalloc` on the stack; the output queue writes directly into the pre-allocated `Instruction[]` buffer. No `List<T>` or `Queue<T>` allocations.

## Core Loop

```
for each Token:
    Immediate  → emit to buffer
    LeftParen  → push
    RightParen → pop until LeftParen, emit all
    Operator   → while top.priority ≥ current.priority: pop & emit; then push
```

Associativity handling: left-associative (`+`, `-`, `*`, `/`) pops at equal precedence; right-associative (`^`) keeps the stack top.

## Operator Pair System

FluxFormula's key extension to standard shunting-yard. Operators can declare pairing relationships:

```
? pair: EmitOnMatch=true, EmitOpCode=Select
: pair: targets ? above
```

When the compiler encounters `:`, it finds the matching `?` on the operator stack, pops everything between them, and emits `Select` in place of `?`. This enables **ternary `a ? b : c`** → `Select(a, b, c)` naturally within shunting-yard. Comma separators (`select(a, b, c)`) use the same mechanism with `IsSeparator=true`.

## Register Allocation

Each emitted instruction gets a virtual register:

```csharp
byte destReg = AllocRegister();
inst.Dest = destReg;
```

The allocator tracks used/freed registers (0-255). Actual register count is stored in the formula header's `MaxRegister` field — the runtime stack-allocates only what's needed.

## Output Format

Compiled output is directly executable bytecode:

```
[Immediate(R2, v1)] [Immediate(R3, v2)] [Add(R1, R2, R3)] [Return(R1)]
```

No AST, no three-address code — straight to `Instruction[]`. The 8-byte layout allows `TData*` pointer reinterpretation for writing immediate values inline.
