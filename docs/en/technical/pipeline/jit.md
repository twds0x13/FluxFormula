# JIT Compilation: From Bytecode to Delegate

`FluxJITCompiler<TData, TDef>` compiles `Instruction[]` bytecode to LINQ Expression Trees, then to executable delegates. Core design question: **how to compile dynamic opcodes (arbitrary `byte` values from Definition) into statically-typed delegates at 2ns execution latency?**

## Compilation Pipeline

```
Instruction[] → Expression Tree → Delegate → GCHandle → FormulaCache
```

### 1. Registers → ParameterExpression

Each instruction's destination register maps to a `ParameterExpression`:

```csharp
ParameterExpression[] registers = new ParameterExpression[regCount];
for (int i = 0; i < regCount; i++)
    registers[i] = Expression.Parameter(typeof(TData), $"r{i}");
```

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

Under `FLUX_FAST_EXPRESSION_COMPILER`, uses [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) instead of standard `Expression.Compile()`. Benefits: avoids internal `System.Reflection.Emit` overhead; faster compilation for complex expressions.

## Delegate Caching

Compiled delegates are stored in `FormulaCache` via `GCHandle`:

```csharp
var handle = GCHandle.Alloc(func);
cache.PutDelegate(hash, GCHandle.ToIntPtr(handle));
```

Same bytecode → same `DualHash64` → cache hit → zero compilation on subsequent `Instantiate` calls. Cache capacity is controlled by `FluxConfig.FormulaCacheCapacity` (default 2048). This is why JIT path achieves 2ns latency — compilation happens once.

## Per-Link Chained JIT

Chained formulas compile each link independently into a `CompiledFunc[]`:

```csharp
for (int i = 0; i < _chainFuncs.Length; i++)
{
    if (i > 0) injector = injector.SetIndex(0, prevResult);
    prevResult = _chainFuncs[i](injector.GetBuffer());
}
```

Each link's delegate is independently cached — `A.Connect(B).Connect(C)` shares `A`, `B`, `C` caches across all combinations.

## JIT Failure Fallback

When `Expression.Compile()` throws `PlatformNotSupportedException`:
1. `FluxPlatform.DisableJit()` — process-wide, one-time
2. Fallback to interpreter path — transparent to caller

The only difference is performance (2ns → 27ns), not correctness.
