# Glossary

Core terms used throughout the FluxFormula documentation, organized by category. Each entry is a 1–2 sentence factual statement.

## Pipeline Stages

| Term | Description |
|------|-------------|
| Lexer (词法分析) | `FluxLexer<TData>` parses a string expression into a token stream. Hand-written `ReadOnlySpan<char>` scanner — zero regex, zero allocation |
| Compiler (编译器) | `FluxAssembler.Compile()` converts the token stream into bytecode, producing a `FluxFormula<TData,TDef>` or `FluxModifier<TData,TDef>` |
| Interpreter (解释器) | `FluxEvaluator` executes bytecode instructions one by one. Fully cross-platform compatible (including IL2CPP/AOT). ref struct, stack-allocated |
| JIT | `FluxJITCompiler` compiles bytecode into a LINQ Expression Tree delegate. Single call takes 2–4ns. Platforms without JIT (WebGL, iOS) auto-degrade to the interpreter |
| IL Compiler (IL 编译器) | `FluxILCompiler` emits IL instructions directly into a `DynamicMethod`, producing a lightweight delegate |

## Data Structures

| Term | Description |
|------|-------------|
| Token (`FluxToken<TData>`) | The smallest atomic unit of an infix expression. Contains `Oper` (byte opcode) and `Data` (value). Immediate tokens carry a concrete value; operator tokens have `default` Data |
| Instruction (指令) | 8-byte fixed-size struct with `LayoutKind.Explicit` memory layout. Contains OpCode, Arg0/Arg1, and a 2-byte Raw field. TData may span multiple Instruction slots |
| Immediate (立即数) | An instruction of `OpType.Immediate`, carrying a compile-time constant. Stored inline in the bytecode buffer — no runtime lookup needed |
| Formula (`FluxFormula<TData,TDef>`) | Immutable bytecode container representing a complete, independently evaluable formula. Produced by `Compile()`, cacheable and reusable |
| Modifier (`FluxModifier<TData,TDef>`) | Immutable bytecode container representing a formula fragment missing its left operand. Appended after a preceding formula via `Connect()` |
| FluxChain (链) | Return type of `Connect()`. Holds `ChainLink[]` internally; physical merge is deferred to evaluation time. Automatically merges into an atomic formula when chain length exceeds `MergeThreshold` |
| FluxInstance (实例) | Runtime execution handle for a formula. `Instantiate()` returns a ref struct; inject variables via `Set()`, then get the result via `Run()` |

## Execution Model

| Term | Description |
|------|-------------|
| Virtual Machine / VM (虚拟机) | Register-based bytecode execution engine (not stack-based). Operand addresses are specified directly by register indices in each instruction |
| Register Machine (寄存器机) | A linear array of 256 virtual registers (`stackalloc TData[256]`). Binary operation results can reuse the left operand's register slot, reducing register pressure |
| Bytecode (字节码) | The Instruction sequence output by the compiler. Each instruction is 8 bytes fixed-size, cache-friendly. Serializable to `.ff` files or embeddable in a Blob |
| Early Exit / R0 (短路返回) | When `Compute()` writes a non-default value to R0 (error register), the evaluator immediately stops and returns that value, skipping all remaining instructions |
| Bus Register / R1 (总线寄存器) | Fixed register index 1. All formula results land in R1. `Connect()` passes the previous formula's R1 output as the next formula's input |
| Arity (元数) | The number of arguments an operator takes. Binary operators have arity=2 (e.g. `+`, `*`); unary have arity=1 (e.g. negation `-`). Maximum arity is 6 |
| stackalloc | C# keyword that allocates a contiguous block of memory on the stack. FluxFormula uses `stackalloc TData[256]` for the register array — zero GC |
| ref struct | C# value type constraint: instances can only live on the stack — no boxing, no heap storage. `FluxInstance` is a ref struct, guaranteeing zero heap allocation during execution |

## Operator Definition

| Term | Description |
|------|-------------|
| OpCode (操作码) | The `byte` identifier for an operator. The framework operates on raw bytes — the Definition's methods receive `byte` and cast to a private enum as needed |
| Precedence (优先级) | Operator binding priority. `*` has higher precedence than `+`, so in `a + b * c`, `b * c` is bound first. Implemented as `GetPrecedence(byte)` returning int |
| Associativity (结合性) | Evaluation direction for same-precedence operators. `Left` = left-to-right (`a - b - c` = `(a - b) - c`), `Right` = right-to-left |
| OperandPosition | The side from which an operator expects its operand. `Left` (e.g. `+` reads from the left operand), `Right` (e.g. `Neg` reads from the right operand). `GetFirstPosition()` declares the first operand location |
| TokenContext | A context marker used during compilation to disambiguate symbols. `OperandExpected` vs `OperatorExpected`. The same symbol (e.g. `-`) maps to a different opcode depending on context |
| OpType | Instruction type classification: `Instruction` (normal operator), `Immediate` (literal constant), `Return` (terminator), `Pair` (bracket) |

## Caching & Persistence

| Term | Description |
|------|-------------|
| FormulaCache (公式缓存) | Global formula cache keyed by `DualHash64`. Both JIT delegates and bytecode entries are stored here. Backed by weak references — collectable under GC pressure |
| DualHash64 (双哈希) | 128-bit dual hash (XxHash64 + FnvHash64) for content addressing of formula bytecode. The same formula appearing in different places shares the same cache entry |
| Delegate Cache (委托缓存) | Each compiled `FluxFormula`'s JIT delegate is cached in FormulaCache. First `Instantiate` compiles; subsequent calls hit the cache |
| Blob | A binary collection of all pre-compiled formulas (`.blob` file). Generated at build time by `FluxBlobBuilder`, loaded at runtime via `FluxBlob.Load()` into FormulaCache |
| VFF (Virtual Flux Formula) | `.vff` file persisting formula references (DualHash64 pointing into a Blob) plus parameter overrides. Analogy: Blob ≈ DLL, VFF ≈ import table |
| FluxAsset | Unity `ScriptableObject` asset file for a formula. Visually editable in the Editor; included in the Blob at build time |

## Platform Concepts

| Term | Description |
|------|-------------|
| IL2CPP | Unity's AOT compilation backend: converts C# to C++ then compiles to native code. `Expression.Compile()` is unsupported; FluxFormula auto-degrades to the interpreter |
| AOT (Ahead-of-Time) | Compilation approach where machine code is generated at build time — no new code can be emitted at runtime. iOS and WebGL are AOT platforms |
| Burst | Unity's high-performance compiler that compiles a C# subset to highly optimized native code. FluxFormula definitions satisfy the `unmanaged` constraint and are Burst-compatible |
| Addressables | Unity's asynchronous asset management system. The `FluxFormula.Addressables` package loads Blob and VFF files via `ValueTask<T>` |
| Expression Tree (表达式树) | .NET's code-as-data API. The JIT path converts bytecode into an Expression Tree, then compiles it into a delegate. Not available on AOT platforms |
