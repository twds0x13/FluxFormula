# FluxFormula Technical Analysis

> Generated: 2026-06-19 | Last updated: 2026-06-21 | Based on version: 1.5.0
>
> A file-by-file technical analysis of FluxFormula's source code, noting potential issues, implicit conventions, and optimization opportunities. Read-only analysis, no source modifications.
>
> Relationship with other technical documents:
> - [Internals](./internals.md) — high-level architectural overview
> - [Compile Cache Pipeline](./compile-cache.md) — cache architecture deep dive
> - [ChainLink Deep Dive](./chainlink-deep-dive.md) — chain evaluation deep dive
> - [Architecture Decisions](./architecture-decisions.md) — design decisions and context
>
> This document serves as a **source-level supplement** — when the abstract descriptions in the documents above are insufficient to answer implementation questions, consult this file-by-file analysis.

---

## 1. Architecture Overview

```
String expression / FluxToken[]
  → FluxLexer.Lex()
    → LexResult (Token[] + VarNames[])
  → FluxAssembler.Compile()
    → FluxCompiler (shunting-yard algorithm)
      → Instruction[] bytecode
        → FluxFormula (immutable, holds buffer)
  → FluxAssembler.Instantiate()
    → FluxInjector (inject data)
      → FluxInstance (fluent API)
        → .Run()
          ├─ Interpreter path: FluxEvaluator.Compute()  ← stackalloc + unsafe pointers
          └─ JIT path:       FluxJITCompiler.Compile() → Expression.Lambda.Compile() delegate
```

Compared to v1.0.0, v1.5.0 adds FluxLexer (lexical analysis), FluxPlatform (JIT degradation), compile-cache pipeline (DualHash64 + FormulaCache), blob build pipeline (FluxBlobBuilder + FluxBlob), VFF virtual formula format, global configuration (FluxConfig), and MaxRegister on-demand allocation.

### Three-Layer Generic Constraints (shared by all core types)

| Parameter | Constraint | Meaning |
|-----------|-----------|---------|
| `TData` | `unmanaged` | Data unit type (float, int, custom blittable struct) |
| `TOper` | `unmanaged, Enum` | Operator enum with byte-sized underlying representation |
| `TDef` | `unmanaged, IFluxDefinition<TData,TOper>` | Operator semantics definition, value type to eliminate virtual dispatch |

---

## 2. File-by-File Technical Notes

### 2.1 FluxToken.cs

**Role**: Lexical layer — the most basic unit users build.

**Key points**:
- `TOper` is cast to its underlying value as opcode via `*(byte*)&oper`. The operator enum's underlying type must be `byte` (validated in `FluxFormula`'s static constructor, see §2.5).
- The Token's `Data` field is only meaningful for Immediate-type tokens; for operator tokens it holds `default` — an implicit convention with no runtime validation.

**Potential issues**:
- Token.Data is meaningless for non-Immediate tokens, but there is no defensive check or documentation warning.

---

### 2.2 FluxEvaluator.cs (including all enums/structs/interfaces)

**Role**: Runtime core file — carries all public type definitions and the interpreter execution engine.

#### 2.2.1 Enums

| Enum | Underlying Type | Values | Description |
|------|----------------|--------|-------------|
| `FluxType` | `byte` | Modifier=0, Formula=1 | Distinguishes complete formulas from modifier fragments |
| `OpType` | `byte` | Immediate=0, Instruction=1, Return=2 | Bytecode instruction classification |
| `Associativity` | — | Left, Right | Operator associativity |
| `Pair` | `byte` | None=0, Left=1, Right=2 | Bracket pair role |

**Key points**:
- `OpType.Immediate` means "immediate load" — loading a TData value from the instruction stream into a register. This matches the traditional CPU "immediate" concept.
- `FluxType.Modifier` semantics: a formula fragment with no "source", cannot execute standalone; must be concatenated to a Formula via `Connect()`.
- `TokenContext` enum is used in the compiler/lexer to distinguish symbol context semantics.

#### 2.2.2 FluxPlatform (added in v1.3.0)

```csharp
internal static class FluxPlatform
{
    internal const int MaxRegisters = 255;
    private static volatile bool _jitDisabled;

    public static bool IsJitDisabled => _jitDisabled;
    public static void DisableJit() => _jitDisabled = true;
}
```

**Key points**:
- `volatile bool` ensures multi-threaded visibility — prevents JIT degradation state inconsistency in async/Job System scenarios.
- `DisableJit()` is only called by the framework in `FluxAssembler.Instantiate()` when `Expression.Compile()` fails; users do not call it directly.
- `MaxRegisters = 255` is the byte index limit; R0 (error) and R1 (bus) are reserved, leaving 253 general-purpose registers.

#### 2.2.3 OpPair\<TOper\>

```csharp
struct OpPair<TOper> {
    Pair PairRole;        // bracket role
    TOper TargetLeft;     // matching target left bracket
    bool EmitOnMatch;     // whether to emit instruction on match
    TOper EmitOpCode;     // what instruction to emit
}
```

**Key points**:
- A micro DSL engine: maps syntactic brackets to semantic instructions.
- Typical use case: function call `sin(x)` — `sin` is a Left-pair, right bracket match triggers `EmitOnMatch=true`, emitting `EmitOpCode=SinOp`.
- `TargetLeft` allows different left bracket types to pair — e.g. `[` and `]` can have different opcodes.

**Potential issues**:
- `TargetLeft` uses `Equals()` comparison, which boxes Enum values. Since TOper is likely backed by byte, the JIT will optimize it; negligible impact but worth noting.

#### 2.2.4 Instruction (8-Byte Explicit Layout)

```
Offset:  0        1        2     3     4     5     6     7
Field:   OpCode   Dest     Arg0  Arg1  Arg2  Arg3  Arg4  Arg5
         └────────────── Raw (long) ──────────────────────┘
```

**Key points**:
- `Size = 8` guaranteed; `Marshal.SizeOf<Instruction>()` is usable.
- Maximum operand count = 6 (Arg0–Arg5), limiting single operation arity.
- `Dest` and `Arg0` may be the same register (e.g., R1 = R1 + R2, Dest=1, Arg0=1, Arg1=2).
- `Raw` shares offset 0 with `OpCode` — reading `Raw` gives the full 8-byte long view. Used only for debug dumps and `ToBytes()` serialization.

**Design constraints**:
- Arity capped at 6, balanced against 8-byte struct size: 6 operands + OpCode + Dest = 8 bytes.
- Dest can be set to 0 (R0 error register) — this is intentional. If a user-defined operator writes to R0, it triggers short-circuit return for custom error handling.

#### 2.2.5 IFluxDefinition\<TData, TOper\>

```csharp
interface IFluxDefinition<TData, TOper> {
    TOper GetReturnOp();                          // which opcode is Return
    int GetArity(byte op);                       // operand count
    OpType GetKind(byte op);                     // Immediate/Instruction/Return
    int GetPrecedence(TOper op);                 // precedence (higher = tighter)
    OpPair<TOper> GetPair(TOper op);            // bracket pair info
    Associativity GetAssociativity(TOper op);    // Left/Right
    TOper ResolveToken(TOper op, TokenContext ctx); // token disambiguation (v1.2.0)
    TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers); // execution
}
```

**Key points**:
- Parameter `op` is `byte`, not `TOper` — callers have already converted via `*(byte*)&oper`.
- `Compute` receives the entire `Instruction`, not just the opcode, allowing access to Dest and Arg0–Arg5.
- `registers` is a `Span<TData>` of length 256 — the interpreter guarantees at least this size.
- `ResolveToken` added in v1.2.0, maps the same symbol to different operators based on context (e.g., `-` → unary negate vs binary subtract).
- Register conventions:
  - R0: Error register. Any non-default return triggers short-circuit.
  - R1: Bus / default result register.
  - R2–R254: General-purpose registers.

#### 2.2.6 IFluxJITDefinition\<TData, TOper\>

```csharp
interface IFluxJITDefinition<TData, TOper> : IFluxDefinition<TData, TOper> {
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers);
}
```

**Key points**:
- `registers` is `ParameterExpression[256]`, with indices corresponding to register numbers.
- Return value is a pure computation `Expression` — callers handle assignment wrapping and R0 error checking.
- Must be semantically consistent with `Compute()`: same input produces same output.

#### 2.2.7 FluxEvaluator (Interpreter Core)

```csharp
internal unsafe ref struct FluxEvaluator<TData, TOper, TDef> {
    TData Compute(ReadOnlySpan<Instruction> raw);
    static bool IsDefault(TData* ptr);
}
```

**Key points**:

**(a) Register memory alignment**
```csharp
byte* rawPtr = stackalloc byte[sizeof(TData) * byte.MaxValue + 63];
long addr = (long)rawPtr;
TData* regsPtr = (TData*)((addr + 63) & ~63);  // 64-byte aligned
```
- Allocates 256 TData registers + 63 bytes padding.
- Aligns to 64-byte boundary, suggesting SIMD optimization intent (AVX cache line), though no SIMD instructions are currently used.
- Allocated on the stack (`stackalloc`), zero GC.

**(b) Immediate reading**
```csharp
TData* pData = (TData*)(pBase + ip + 1);  // +1 skips Instruction header
regsPtr[inst->Dest] = *pData;
ip += (sizeof(TData) + 7) / 8;             // Instruction count to skip
```
- Data follows the Instruction header in the same `Instruction[]` buffer.
- `(sizeof(TData) + 7) / 8` calculates how many Instruction slots the data occupies. TData ≤ 8 bytes = 1 slot, ≤ 16 bytes = 2 slots.
- The `ip` loop variable increments in "instruction count" rather than "byte count" — mixed units increase cognitive load.

**(c) Execution and short-circuit**
```csharp
regsPtr[inst->Dest] = _definition.Compute(operByte, *inst, registers);
if (!IsDefault(&regsPtr[0]))           // R0 non-default → short-circuit return
    return regsPtr[0];
```
- After every instruction, R0 is checked; any non-default value triggers an immediate error return.
- `IsDefault()` uses `ReadOnlySpan<byte>.SequenceEqual()` for byte-by-byte comparison — may be slow for structs larger than 8 bytes, but ensures generality.

**(d) Instruction pointer without bounds checking**
- The loop `for (int ip = 0; ip < raw.Length; ip++)` with `ip += dataSlots` on the Immediate path may skip multiple instructions. No bounds checking — fully trusts the compiler correctly computed the buffer length.

**Potential issues**:
- `ip` mixed units (instruction count vs byte offset) — high cognitive load, but transparent to users as it's internal.
- No `raw.Length` bounds protection — trusts compiler output. A compiler bug producing an undersized buffer would cause a memory access violation rather than a friendly error.
- `IsDefault` byte-by-byte comparison is generic but suboptimal; could specialize for larger blittable struct types.

---

### 2.3 FluxLexer.cs (added in v1.1.0)

**Role**: String-to-token-stream lexical analyzer.

**Key points**:

**(a) Architecture**: Configuration-driven, hand-written `ReadOnlySpan<char>` scanner. Zero regex, zero third-party dependencies, zero allocation (except output arrays).

**(b) Configuration model**:
```csharp
public class LexerConfig<TData, TOper>
{
    public TOper LiteralOper;                          // operator for numeric literals
    public Func<string, TData> LiteralParser;          // literal → TData parse function
    public List<OperatorRule<TOper>> Operators;        // operator symbol list
    public List<BracketRule<TOper>> Brackets;          // bracket pair list
    public List<TOper> ImplicitOperators;              // implicit multiplication operators
    public List<VariablePatternRule> VariablePatterns; // variable patterns
}
```

**(c) Operator matching strategy**: Operators sorted by symbol length in descending order on construction; longer symbols matched first. E.g., `**` is tried before `*`, preventing truncation mismatches.

**(d) Implicit operator insertion**: The Lexer detects juxtaposition — e.g., `2(atk)` or `(a)(b)` — in a two-pass scan and inserts implicit operators between them. If multiple implicit operators are configured and juxtaposition is detected, throws `FormatException` reporting the ambiguity.

**(e) Variable patterns**: Variable syntax defined through `VariablePatternRule`'s Prefix/Suffix. E.g., `["[", "]"]` matches `[atk]`, `["{var:", "}"]` matches `{var:damage}`. Variable names are recorded alongside token positions in `LexResult.VarNames`.

**Potential issues**:
- If operators have prefix relationships (e.g., `-` and `--`), the sort logic guarantees longer-first matching, but users must avoid defining ambiguous rules.
- Implicit operator insertion performs an O(n) scan in the second pass where n = token count; linear overhead for very long formulas.
- The literal parse delegate `LiteralParser` may throw exceptions (e.g., `float.Parse` failure); Lexer does not catch, propagating exceptions directly to the caller.

---

### 2.4 FluxCompiler.cs

**Role**: Shunting-yard algorithm compiler, converting infix token streams to postfix bytecode.

**Key points**:

**(a) Register allocation**
```csharp
byte nextReg = 2;  // R0=Error, R1=Bus, start from 2
```
- Simple incrementing allocation, no register reuse. Longest formula limited to 253 variables (255 - R0 - R1).
- When registers run out, throws `"Out of registers."`. The error message lacks context (current formula length / register usage).

**(b) EmitOp — implicit R1 injection**
```csharp
while (regTop + 1 < arity)
{
    for (int i = regTop; i >= 0; i--)
        regStack[i + 1] = regStack[i];
    regStack[0] = 1;  // R1
    regTop++;
}
```
- When operand arity > available register stack depth, automatically injects R1 to fill the gap.
- O(n²) shift operation: the entire stack shifts left by one for each missing operand. In practice n ≤ 64, arity ≤ 6, negligible impact.

**(c) Bracket matching and EmitOnMatch**
- Supports "function call" semantics: matching in `sin(x)` emits the `SinOp` instruction.
- The `OpPair.EmitOnMatch` + `EmitOpCode` mechanism is flexible but requires correct `GetPair()` configuration to be effective.

**(d) Precedence and associativity**
```csharp
bool shouldPop = (topPrec > currPrec)
              || (topPrec == currPrec && assoc == Associativity.Left);
```
- Standard shunting-yard rule: higher precedence pops first; equal precedence with left associativity pops first.

**(e) Termination instruction**
```csharp
ret->Dest = regTop >= 0 ? regStack[0] : (byte)1;  // default return R1
```
- When the register stack has values, returns the bottom (first result); otherwise returns R1.
- For multi-value expressions, only the stack bottom is returned; remaining values are discarded.

**Potential issues**:
- Registers are never reused — long formulas may exhaust 253 registers prematurely.
- `"Out of registers."` error lacks context, making debugging difficult.

---

### 2.5 FluxFormula.cs

**Role**: Immutable bytecode container after compilation.

**Key points**:

**(a) TOper type validation (static constructor)**
```csharp
static FluxFormula()
{
    if (sizeof(TOper) != 1)
        throw new TypeInitializationException(
            typeof(FluxFormula<TData, TOper>).FullName,
            new NotSupportedException(
                $"FluxFormula requires TOper underlying type to be byte. Current: {typeof(TOper).Name} (sizeof={sizeof(TOper)}). Use `enum {typeof(TOper).Name} : byte`."
            )
        );
}
```
- Automatically executes on type initialization; raises a clear exception immediately if TOper's underlying type is not byte.
- Exception message includes the specific type name and sizeof value for easy diagnosis.

**(b) Connect() concatenation — with empty formula protection**
```csharp
if (this.Count == 0) return next;
if (next.Count == 0) return this;
```
- Guard clauses added in v1.3.0, protecting empty formula concatenation edge cases, preventing negative array length crashes from `newCount = -1 + next.Count`.
- Note: `Connect()` still produces one `new Instruction[newCount]` heap allocation — the only GC allocation point at compile time.

**(c) Raw() / ToBytes() / FromBytes()**
- `Raw()`: Returns `ReadOnlySpan<Instruction>`, exposing only the valid region of `Count` length.
- `ToBytes()`: Serializes to a compact byte array, zero extra allocation (memcpy write).
- `FromBytes()`: Deserializes from byte array, no recompilation needed. Typical scenario: hot-reloading formulas.

**Potential issues**:
- `Connect()` does not remap register numbers; concatenated formulas may have register conflicts. Currently the caller is responsible for ensuring register consistency with no framework-level conflict detection.

---

### 2.6 FluxAssembler.cs

**Role**: Main entry point, orchestrating the full compile → instantiate pipeline.

**Key points**:

**(a) JIT auto-degradation (added in v1.3.0)**
```csharp
if (jit && !FluxPlatform.IsJitDisabled)
{
    try
    {
        var func = FluxJITCompiler<TData, TOper, TDef>.Compile(...);
        return new FluxInstance(..., func, true);
    }
    catch (Exception ex) when (
        ex is PlatformNotSupportedException
        || ex is NotSupportedException
        || ex is InvalidOperationException)
    {
        FluxPlatform.DisableJit();
    }
}
// fall through to interpreter
```
- Wraps try-catch in `Instantiate()`, catching three types of exceptions when `Expression.Compile()` fails on IL2CPP/AOT platforms.
- On failure, calls `FluxPlatform.DisableJit()`, causing all subsequent `Instantiate(jit: true)` calls in the same process to skip JIT directly.
- After first JIT failure, degradation has no performance impact on subsequent calls (skip try-catch and go directly to the interpreter branch).

**(b) Buffer size estimation**
```csharp
int dataSlots = (sizeof(TData) + 7) / 8;
var buffer = new Instruction[tokens.Length * (1 + dataSlots) + 1];
```
- Reserves 1 instruction header + dataSlots data slots per token, plus 1 final Return.
- Pure operator tokens waste data slots — this is a conservative estimate, safe but not compact.

**(c) FluxType determination**
```csharp
if (kind == OpType.Instruction) {
    if (pairInfo.PairRole != Pair.Left) {
        type = FluxType.Modifier;
    }
}
```
- First Token is Instruction and not a left bracket → Modifier (fragment lacking a source).
- First Token is Immediate → Formula.

**(d) CreateInjector — two-pass scan**
- First pass: count Immediate instructions (allocate offsets array).
- Second pass: record each Immediate's data offset.
- Avoids using `List<int>` (which would produce GC), using raw arrays instead. A time-for-space design choice.

**Potential issues**:
- `buffer.ToArray()` copies the entire formula buffer. For large formulas (thousands of instructions), this is a significant allocation. If a formula is cached and `Instantiate()` called repeatedly, each call copies. The JIT path could potentially avoid this copy.
- `CreateInjector()`'s two-pass scan could be optimized to a single pass (allocate an estimated-size array, expand if insufficient), but the current approach guarantees exact array sizing.

---

### 2.7 FluxInjector.cs

**Role**: Data injector, writing user parameters into the bytecode buffer.

**Key points**:

**(a) Two modes**
```csharp
// JIT mode: offsets = null, linear index
offset = paramIndex * _slotsPerData;

// Interpreter mode: offsets point to Immediate data positions in formula buffer
offset = _offsets[paramIndex];
```

**(b) Set() pointer write**
```csharp
fixed (Instruction* pBase = _buffer) {
    *(TData*)(pBase + offset) = value;  // pointer reinterpretation, zero-copy write
}
```
- In `pBase + offset`, the offset unit is "number of Instructions", not bytes. `Instruction*` arithmetic automatically handles the multiplication.

**(c) Set() named variable injection**
- Inline binary search locates the variable name in `VariableSlots`, O(log n) complexity.
- All occurrences of the same variable name are updated simultaneously.

**Confirmed fix (v1.3.0)**:
- `ToString()` now correctly returns `"FluxInjector<{typeof(TData).Name}>"`; the legacy `FluxBinder` naming has been corrected.

**Potential issues**:
- `Set()` in JIT mode checks `offset + _slotsPerData > _buffer.Length` for bounds, but `offset + _slotsPerData` could overflow int for very large index values — no protection. In practice indices are small, so risk is minimal.

---

### 2.8 FluxInstance.cs

**Role**: Fluent API — the ref struct users interact with directly.

**Key points**:

**(a) ref struct design**
```csharp
public ref struct FluxInstance<TData, TOper, TDef>
```
- Allows `Set()` to modify internal state (injector). Stack-allocated, cannot be boxed, cannot be a class field.
- Cost: cannot be used in lambda captures (`Assert.Throws(() => inst.Run())` won't work), requires try-catch instead.

**(b) Run() dispatch**
```csharp
if (_isJit)
    return _jitFunc(_injector.GetBuffer());    // JIT delegate
else
    return new FluxEvaluator<TData, TOper, TDef>(_provider).Compute(_formula.Raw());  // interpreter
```
- JIT: passes the injected data buffer (compact payload array).
- Interpreter: creates a new FluxEvaluator each time (ref struct, stack-allocated), passing the full formula buffer.

**(c) Modifier protection**
```csharp
if (_formula.Type != FluxType.Formula)
    throw new InvalidOperationException("Modifier cannot run standalone.");
```

---

### 2.9 FluxJITCompiler.cs

**Role**: JIT compiler, converting bytecode to LINQ Expression Trees and then compiling to delegates.

**Key points**:

**(a) Data payload extraction**
- The JIT path separates "instructions" from "data" — instructions become Expression Trees, data becomes an `Instruction[]` parameter.
- Data stored in payload's Instruction slots is actually the binary representation of TData.

**(b) Immediate loading (SafeCast)**
- Each Immediate generates a `GetData<TData>(buffer, index)` call, internally reading TData from `dataBuffer[index]` via `fixed` pointer.
- Each SafeCast requires one `fixed` statement — multiple pointer pins at runtime.

**(c) Error short-circuit**
```csharp
Expression.IfThen(
    Expression.NotEqual(regs[0], defaultTDataExpr),
    Expression.Return(returnTarget, regs[0])
);
```
- Isomorphic to the interpreter — R0 is checked after every Instruction.

**(d) Return logic**
```csharp
Expression.Condition(
    Expression.NotEqual(regs[0], defaultTDataExpr),
    regs[0],           // R0 non-default → error
    regs[inst.Dest]    // otherwise return Return instruction's destination register
);
```
- JIT Return handling is more precise than the interpreter — explicitly uses `inst.Dest` rather than falling back to `regs[1]`.

**(e) Expression.Compile()**
- `Expression.Lambda<CompiledFunc>(block, bufferParam).Compile()`.
- In .NET, `Compile()` also internally performs an interpret→JIT conversion, incurring some overhead.
- The compiled delegate is strongly typed `Func<Instruction[], TData>`.
- AOT platforms (IL2CPP, iOS, WebGL) do not support `Expression.Compile()`; the framework catches exceptions in `FluxAssembler.Instantiate()` and automatically degrades.

**Potential issues**:
- Generated Expression Trees are not cached per se — each `Compile()` rebuilds + recompiles. For frequently-compiled scenarios this has overhead, but the formula-caching pattern (compile once, instantiate many times) avoids it.
- `SafeCast` is called once per data slot — multiple `fixed` statements may impact performance, but for most formulas (dozens of variables) the impact is negligible.

---

### 2.10 Editor Extensions

**FluxFormulaExtensions.Dump()**:
- Outputs three formats per instruction: human-readable summary, labeled binary, 64-bit raw binary.
- Uses a shared `StringBuilder` to reduce allocations.

**InstructionExtensions.ToBinary()**:
- Format: `Op:XXXXXXXX | De:XXXXXXXX | R0:XXXXXXXX R1:XXXXXXXX ... R5:XXXXXXXX | Raw:XXXXXXXXXXXXXXXX`.
- Always displays all 8 fields — even meaningless Arg values when arity < 6.

**FluxAssetEditor.cs**:
- Generic EditorWindow, zero reflection. Maps type-to-editor via `FluxEditorRegistry`.
- Supports formula compilation, immediate evaluation, and variable debugging.

---

## 3. Current Status Assessment (v1.5.0)

### 3.1 Resolved v1.0.0 Issues

The following issues are confirmed fixed in the current version:

| # | Original Issue | Solution | Location |
|---|---------------|----------|----------|
| 1 | JIT crash on IL2CPP/AOT platforms | `FluxPlatform` + `Instantiate()` try-catch auto-degradation | `FluxEvaluator.cs` / `FluxAssembler.cs` |
| 2 | `Connect()` no Count=0 guard | Guard: `if (this.Count == 0) return next; if (next.Count == 0) return this;` | `FluxFormula.cs` |
| 3 | TOper underlying type unvalidated | `FluxFormula` static ctor `sizeof(TOper) != 1` check | `FluxFormula.cs` |
| 4 | No real tests | 152 unit tests covering compile/interpreter/JIT/Connect/Lexer/persistence/cache/blob | `tests/` + `Tests/` directories |
| 5 | No IFluxDefinition example | `FloatMathDef` complete example + `SmokeTest.cs` | `TestDefinition.cs` |
| 6 | Register model undocumented | VitePress bilingual docs covering core concepts/API/internals | `docs/` directory |
| 7 | FluxBinder residual naming | `ToString()` updated to `"FluxInjector<{TData}>"` | `FluxInjector.cs` |
| 8 | AOT compatibility undeclared | Docs/FAQ explicitly document platform support matrix | `docs/faq.md` |
| 9 | README not implemented | Bilingual README + badge system complete | `README.md` / `README.en.md` |

### 3.2 New Capabilities in v1.5.0

| Capability | Description | Location |
|-----------|-------------|----------|
| Compile cache | DualHash64 → FormulaCache → Delegate cache, significant cold/warm latency divergence | `DualHash64.cs`, `FormulaCache.cs` |
| Blob build pipeline | FluxBlobBuilder scans FluxAsset → concatenates blob → generates C# offset table | `FluxBlobBuilder.cs`, `FluxBlob.cs` |
| VFF virtual formula | "VFF\0" format persistent formula references + parameter overrides, DLL-style symbol resolution | `VffFormat.cs` |
| Format centralization | FormulaFormat/BinaryFormat eliminate scattered helpers; single source of truth for format definitions | `FormulaFormat.cs`, `BinaryFormat.cs` |
| MaxRegister on-demand | Formula header stores compile-time-analyzed max register number; on-demand stackalloc | `FormulaFormat.cs` |
| Global configuration | FluxConfig replaces hardcoded constants (cache capacity/merge threshold/buffer size) | `FluxConfig.cs` |
| Per-link JIT chain evaluation | Connect products JIT-compiled per link, SetIndex(0, prevResult) chaining | `FluxJITCompiler.cs`, `FluxInstance.cs` |

### 3.3 Current Improvement Items

| # | Category | Issue | Location |
|---|---------|-------|----------|
| 1 | Perf | `CreateInjector()` two-pass scan could be optimized to single pass | `FluxAssembler.cs` |
| 2 | Perf | Interpreter `IsDefault()` byte-by-byte comparison could be specialized for large structs | `FluxEvaluator.cs` |
| 3 | Robustness | Registers never reused; 253 registers may be exhausted for very long formulas | `FluxCompiler.cs` |
| 4 | Robustness | `"Out of registers."` error lacks context | `FluxCompiler.cs` |
| 5 | Robustness | `Set()` JIT mode `offset + _slotsPerData` could overflow int (low risk) | `FluxInjector.cs` |
| 6 | Docs | Some internal algorithms still lack source-level comments (shunting-yard, register allocation, R1 injection) | `FluxCompiler.cs` |

---

## 4. Architecture Assessment

### Strengths
- **Generic design**: TData/TOper/TDef three-layer parameter separation, compile-time type safety, zero virtual dispatch overhead.
- **Zero-GC completeness**: ref struct + stackalloc + unsafe + unmanaged constraints cover the complete hot path from Instantiate to Run.
- **Dual-backend strategy**: Interpreter with zero compile overhead, full platform compatibility; JIT compiles to near-native performance. Auto-degradation lets IL2CPP users be unaware of the switch.
- **Compact bytecode**: 8-byte fixed-size instructions, suitable for caching, serialization, and cross-platform transport.
- **OpPair system**: Flexible syntax-to-semantics mapping mechanism, can simulate complex syntax like function calls.
- **Lexer design**: Configuration-driven Span scanner, zero dependencies, zero allocation, correct longest-symbol-first matching strategy.

### Architectural Constraints
- No register reuse means formulas are capped at 253 variables. Formulas exceeding this scale must be split.
- `Connect()` does not remap register numbers; multi-formula concatenation requires the caller to guarantee register consistency.
- The JIT path depends on `Expression.Compile()`, fully relying on the interpreter on AOT platforms — but this is an objective limitation of the .NET ecosystem, and the framework handles it automatically to the maximum extent possible.
