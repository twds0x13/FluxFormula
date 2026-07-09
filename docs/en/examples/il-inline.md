# Example: IL Inline Operators (EmitOp inline)

Demonstrates the `IFluxILDefinition<TData>.EmitOp` interface: hand-writing IL instructions for specific opcodes, eliminating virtual call overhead entirely at compile time.

> This example assumes familiarity with the [IL Compiler](../technical/pipeline/il-compiler.md)'s two-tier inlining system. EmitOp is the EmitOp inline interface: return `true` when the opcode is handled; return `false` and the compiler automatically falls back to Compute pointer call (pointer-based Compute call).

## Operator Enum

```csharp
public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, Return = 255,
}
```

## Definition Declaration

Implements both `IFluxILDefinition<float>` (EmitOp inline inlining) and `IFluxExprDefinition<float>` (Expression Tree fallback + interpreter):

```csharp
public readonly struct FloatMathILDef : IFluxILDefinition<float>, IFluxExprDefinition<float>
```

Base methods (`GetKind`, `GetArity`, `GetPrecedence`, `GetFirstPosition`, `Compute`, etc.) are identical to a standard Definition. The only addition is the `EmitOp` method.

## EmitOp — Hand-Written IL

`EmitOp` receives an `ILGenerator` and `regArr` (a local variable of type `TData[]`). Register values are accessed via `ldelem`/`stelem` on the array, eliminating all method calls.

### Add: Two ldelem + add + stelem

```csharp
if ((FloatOp)op == FloatOp.Add)
{
    il.Emit(OpCodes.Ldloc, regArr);          // [arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Dest); // [arr, destIdx]
    il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0); // [arr, destIdx, arr, idx0]
    il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0]]
    il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr[arg0], arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1); // [arr, destIdx, arr[arg0], arr, idx1]
    il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0], arr[arg1]]
    il.Emit(OpCodes.Add);                    // [arr, destIdx, sum]
    il.Emit(OpCodes.Stelem, typeof(float));  // arr[destIdx] = sum; []
    return true;
}
```

**Stack trace** (step-by-step for a single Add instruction):

| Step | IL Instruction | Top of Stack After | Stack Depth |
|------|---------------|-------------------|-------------|
| 1 | `ldloc regArr` | `arr` | 1 |
| 2 | `ldc.i4 Dest` | `destIdx` | 2 |
| 3 | `ldloc regArr` | `arr` | 3 |
| 4 | `ldc.i4 Arg0` | `idx0` | 4 |
| 5 | `ldelem float` | `arr[arg0]` | 3 |
| 6 | `ldloc regArr` | `arr` | 4 |
| 7 | `ldc.i4 Arg1` | `idx1` | 5 |
| 8 | `ldelem float` | `arr[arg1]` | 4 |
| 9 | `add` | `sum` | 3 |
| 10 | `stelem float` | _empty_ | 0 |

### Mul: Differs only in the arithmetic opcode

```csharp
if ((FloatOp)op == FloatOp.Mul)
{
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Dest);
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0);
    il.Emit(OpCodes.Ldelem, typeof(float));
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1);
    il.Emit(OpCodes.Ldelem, typeof(float));
    il.Emit(OpCodes.Mul);                    // ※ only difference from Add
    il.Emit(OpCodes.Stelem, typeof(float));
    return true;
}
```

### Fallback Pattern

Return `false` for unrecognized or deliberately non-inlined opcodes:

```csharp
// Opcodes such as Sub, Div, Neg are not handled — automatic Compute pointer call fallback
return false;
```

When EmitOp returns false, the compiler automatically generates the Compute pointer call `constrained.callvirt Compute(IntPtr)` call path.

## Full Source

`samples/ILInlineExample/FloatMathILDef.cs` contains the complete, compilable Definition with EmitOp inline inlining for Add/Mul and Compute pointer call fallback for the remaining opcodes.

## Usage

```csharp
var def = default(FloatMathILDef);
var assembler = new FluxAssembler<float, FloatMathILDef>(def);

var lexer = new FluxLexer<float>(new LexerConfig<float>
{
    LiteralOper    = (byte)FloatOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
    Operators =
    {
        new("+", (byte)FloatOp.Add),
        new("-", (byte)FloatOp.Sub),
        new("*", (byte)FloatOp.Mul),
        new("/", (byte)FloatOp.Div),
    },
});

var formula = assembler.Compile(lexer.Lex("1 + 2 * 3").Tokens);
float result = assembler.Instantiate(formula, jit: true).Run();
// result: 7.  Add and Mul are executed via inline IL; Sub/Div via Compute pointer call pointer call.
```

## Comparison with Compute pointer call

| Dimension | Compute pointer call (Compute Pointer) | EmitOp inline (EmitOp) |
|-----------|-------------------------|-----------------|
| Implementation cost | Override `IFluxDefinition.Compute(IntPtr)` | Implement `IFluxILDefinition.EmitOp`; requires IL stack model understanding |
| Runtime overhead | One `constrained.callvirt` + method call | Zero calls; operator logic fully inlined into the delegate body |
| Coverage | All opcodes automatically gain IL path | Selective opcodes only (e.g., Add/Mul — frequent simple operators) |
| Fallback chain | None (Compute pointer call is the default) | Return false → compiler degrades to Compute pointer call |

## stelem Stack Order

This is the single easiest mistake in EmitOp inline implementation. `stelem` per ECMA-335 pops `value`, `index`, and `array` from the top of the stack:

```
stelem expected stack: [ ..., array, index, value ]  ← top = value
```

`array` and `index` must be pushed **first**, with the `value` computed last. **Push the destination address, then compute the source operands, then stelem.**

The Add and Mul implementations above follow this order: steps 1-2 push `arr` and `destIdx`, steps 3-8 compute operand values, step 9 performs the operation, step 10 writes the result.

## Notes

- IL instructions in EmitOp use `inst.Arg0..5` for operand register indices and `inst.Dest` for the destination register index. These indices are verified to be within the array bounds during the IL compiler's Pass 2 (register counting phase).
- The `ILGenerator` evaluation stack depth is managed automatically by the CLR: `stelem` pops 3 elements, `ldelem` pops 2 and pushes 1, `add` pops 2 and pushes 1.
- EmitOp inline inlining only affects the IL emission path (Mono/CoreCLR). On IL2CPP platforms, the Expression Tree path is used and EmitOp is never invoked.
- Deep inlining (e.g., Sum6) rapidly decreases IL readability. Reserve EmitOp inline for frequent, simple opcodes; return false for the rest.
