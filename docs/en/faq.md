# Frequently Asked Questions

## "Modifier cannot run standalone" Error

This occurs when compiling a token sequence that starts with a binary operator (e.g., `+ 5`), which is classified as `FluxType.Modifier`. A Modifier lacks a left operand and must be attached to a complete Formula via `Connect()`.

```csharp
// Error: starts with Add → Modifier
var tokens = new[] { Op(FloatOp.Add), C(5f) };
runner.Build(tokens).Run(); // InvalidOperationException

// Correct: connect to a complete formula
var f1   = runner.Compile(new[] { C(10f) });
var mod  = runner.Compile(new[] { Op(FloatOp.Add), C(5f) });
var combined = f1.Connect(mod); // 10 + 5
```

## Incorrect Results After Connect

`Connect()` does not remap register numbers. If two formulas use the same registers, concatenation causes overwrites. Safe practices:

- Connect empty formulas (`FluxFormula<TData,TOper>.Empty`): zero register conflicts
- Compile the complete expression in a single `Compile()` call rather than post-compilation concatenation

If dynamic composition of many formula fragments is required, concatenate at the token level before a single `Compile()`.

## JIT vs Interpreter Selection

| Scenario | Recommendation |
|------|------|
| Unity Editor development | JIT (faster post-compilation execution) |
| IL2CPP platforms (iOS/WebGL/Console) | No choice needed — JIT auto-degrades to interpreter |
| Formula executed only once or twice | Interpreter (no compilation overhead) |
| Formula compiled once, executed thousands of times | JIT |

The default is `jit: false` (interpreter). Switch to JIT after confirming high formula reuse frequency.

## iOS / WebGL Compatibility

The interpreter path is available on all platforms. `Expression.Compile()` is not supported on IL2CPP platforms; the framework catches the exception and automatically calls `FluxPlatform.DisableJit()`. All subsequent `Instantiate(jit: true)` calls transparently fall back to the interpreter. No platform-specific code is needed.

## Debugging Erroneous Formulas

**1. Inspect bytecode:**

```csharp
var formula = runner.Compile(tokens);
#if UNITY_EDITOR
formula.Dump(); // Requires using FluxFormula.Editor extension
#endif
```

**2. Compare JIT and interpreter results:**

```csharp
float interp = runner.Build(tokens, jit: false).Run();
float jit    = runner.Build(tokens, jit: true).Run();
// If results differ → IFluxDefinition.Compute and GetExpression semantics are out of sync
```

**3. Build incrementally from a simple formula:**

```csharp
C(1f)                          // → 1
C(1f), Add, C(2f)              // → 3
C(1f), Add, C(2f), Mul, C(3f)  // → 7
```

## Why Must Operator Enums Use `: byte`

The framework reads the first byte of the enum via `*(byte*)&oper` as the opcode. If `enum Foo : short` and the value exceeds 255, the byte read is incorrect. A `sizeof(TOper) != 1` check during type initialization throws a clear exception.

```csharp
// Correct
public enum MyOp : byte { Add, Sub, Mul }

// TypeInitializationException
public enum MyOp : int { Add = 256, Sub }
```

## Can TData Use Custom Structs

Yes, as long as the `unmanaged` constraint is satisfied:

```csharp
public struct Vector3f { public float x, y, z; } // Blittable, valid
public struct MyData  { public string Name; }     // Contains reference type, invalid
```

Larger `sizeof(TData)` means more Instruction slots consumed per Immediate in the bytecode. Each slot is 8 bytes. `Vector3f` (12 bytes) = 2 slots.

## Maximum Formula Size

| Bottleneck | Limit |
|------|------|
| Register count | 253 general-purpose registers (R2-R254); long formulas may exhaust them |
| Operator stack | Maximum 64 levels of nesting |
| Single instruction arity | Maximum 6 (Instruction has only Arg0-Arg5) |
| Buffer size | `int` index limit, theoretically ~2^31 instructions |

In practice, formulas are limited by variable count (each Immediate consumes one register), not instruction count. When exceeding ~200 distinct variables, split into multiple formulas.

## Why ref struct

`ref struct` can only exist on the stack — it cannot be boxed (cannot escape to the heap). Combined with `stackalloc` and `unmanaged` generics, the FluxFormula execution hot path guarantees zero GC allocations.

Trade-off: `FluxInstance` cannot be used in lambda captures (e.g., `Assert.Throws(() => inst.Run())`). Use try-catch instead.

## Custom Error Handling

Write the error value to R0. A non-default return value from `Compute()` triggers an early exit:

```csharp
public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
{
    if (op == MyOp.Div && Math.Abs(regs[inst.Arg1]) < 1e-6f)
        return float.NaN; // Write to R0, trigger early exit
    // ...
}
```

JIT path equivalent:

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
{
    return Expression.Condition(
        Expression.Equal(regs[inst.Arg1], Expression.Constant(0f)),
        Expression.Constant(float.NaN),  // Trigger early exit
        Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])
    );
}
```

R0 is checked after every instruction. Once non-default, execution terminates immediately and returns the error value.
