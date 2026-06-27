# JIT Compilation: From Bytecode to Delegate

`FluxJITCompiler<TData, TDef>` compiles `Instruction[]` bytecode to LINQ Expression Trees, then to executable delegates. Core design question: **how to compile dynamic opcodes (arbitrary `byte` values from Definition) into statically-typed delegates at 2ns execution latency?**

## Compilation Pipeline

```
Instruction[] → Expression Tree → Delegate → GCHandle → FormulaCache
```

### 1. Registers → ParameterExpression

Each instruction's destination register (`inst.Dest`) maps to a `ParameterExpression`:

```csharp
ParameterExpression[] registers = new ParameterExpression[regCount];
for (int i = 0; i < regCount; i++)
    registers[i] = Expression.Parameter(typeof(TData), $"r{i}");
```

The register file is represented as a parameter array in the Expression Tree. At runtime, registers are stack-local variables. Once the JIT delegate is further compiled to machine code by the CLR JIT, register parameters map to CPU registers or stack slots.

### 2. Instructions → Expressions

```
Immediate:    reg[dest] = constant      → Expression.Assign(reg[dest], Expression.Constant(value))
Instruction:  reg[dest] = Compute(...)   → Definition.GetExpression(opCode, inst, registers)
Return:       return reg[dest]           → registers[inst.Dest]
```

`GetExpression` is the core method provided by Definition implementors:

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers)
{
    return ((MathOp)op) switch
    {
        MathOp.Add => Expression.Add(registers[inst.Arg0], registers[inst.Arg1]),
        MathOp.Sub => Expression.Subtract(...),
        MathOp.Mul => Expression.Multiply(...),
        // ...
    };
}
```

### 3. BlockExpression → Lambda → Delegate

```csharp
var body = Expression.Block(registers, expressions);
var lambda = Expression.Lambda<CompiledFunc>(body, injectorParam);
var compiled = lambda.Compile();  // or CompileFast()
```

`CompiledFunc` signature: `delegate TData CompiledFunc(TData[] injector)`. The `injector` array holds immediate values in JIT mode — accessed by index rather than embedded in the instruction stream.

## FastExpressionCompiler

Under `FLUX_FAST_EXPRESSION_COMPILER`, uses [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) instead of standard `Expression.Compile()`:

```csharp
#if FLUX_FAST_EXPRESSION_COMPILER
    var compiled = Expression.Lambda<CompiledFunc>(body, injectorParam).CompileFast();
#else
    var compiled = Expression.Lambda<CompiledFunc>(body, injectorParam).Compile();
#endif
```

FastExpressionCompiler's advantage: avoids certain internal `System.Reflection.Emit` overhead in `Expression.Compile()`. For simple expressions the difference is small; for complex chained formulas it is significant.

## Delegate Caching

Compiled delegates are stored in `FormulaCache` via `GCHandle`:

```csharp
var handle = GCHandle.Alloc(func);
cache.PutDelegate(hash, GCHandle.ToIntPtr(handle));
```

Same bytecode → same `DualHash64` → cache hit → zero compilation on subsequent `Instantiate` calls. Cache capacity is controlled by `FluxConfig.FormulaCacheCapacity` (default 2048). This is why JIT path achieves 2ns latency — compilation happens once.

## Per-Link Chained JIT

Chained formulas **always** compile per-link, regardless of chain length. `MergeThreshold` is not checked on the JIT path. Each link compiles independently into a `CompiledFunc[]`:

```csharp
for (int i = 0; i < _chainFuncs.Length; i++)
{
    if (i > 0) injector = injector.SetIndex(0, prevResult);
    prevResult = _chainFuncs[i](injector.GetBuffer());
}
```

### Why JIT never merges long chains

The interpreter path merges chains longer than `MergeThreshold` (default 8) into a single atomic formula, because per-link `BuildLinkBuffer` allocates an `Instruction[]` per link. The JIT path keeps per-link execution for three reasons:

1. **Zero-allocation hot path**: Each link's delegate is pre-compiled. The runtime cost is a `SetIndex` write and a function pointer call — no heap allocation.
2. **LEGO model**: Each link's delegate is independently cached in `FormulaCache`. `A.Connect(B).Connect(C)` shares `A`, `B`, `C` cached delegates across different chains. Merging to atomic loses this link-level reuse.
3. **Compile cost amortization**: A merged formula is a unique bytecode combination, requiring a fresh Expression Tree → delegate compilation. Per-link keeps compile costs amortized per link.

This asymmetry is intentional: the interpreter decides by allocation cost, JIT decides by cache reuse.

## JIT Failure Fallback

When the platform does not support `Expression.Compile()` (IL2CPP/AOT), compilation throws `PlatformNotSupportedException`. `FluxAssembler.Instantiate` catches this exception and:

1. Calls `FluxPlatform.DisableJit()`, preventing further JIT attempts within the same process
2. Falls back to the interpreter path (`jit: false`)

Fallback is automatic and transparent to the caller. The only difference is performance (2ns → 27ns), not correctness.
