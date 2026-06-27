# IL Compiler

`FluxILCompiler<TData, TDef>` compiles bytecode directly into executable delegates using `DynamicMethod` + `ILGenerator`, bypassing Expression Tree AST construction and dual-traversal overhead. Its core design question: **how do you dynamically generate IL at runtime while maintaining zero-GC execution and safe degradation on AOT platforms?**

## Why an IL Path

The Expression Tree path (`FluxJITCompiler`) bottlenecks at compile time, not execution. Building the `Expression` node tree followed by `Compile()` is a dual traversal: first constructing the Expression object graph, then `LambdaCompiler` converting it to IL. For high-frequency formula compilation scenarios (e.g., script hot-reload, first-time instantiation of many chained formulas), this intermediate representation cost is avoidable.

IL emission skips the intermediate AST, generating delegate-body IL directly and merging dual traversal into a single pass. The trade-off is platform restriction: `DynamicMethod` depends on `System.Reflection.Emit`, available only on Mono and CoreCLR, not on IL2CPP.

## Compiler Positioning

JIT delegate compilation has two paths, selected by priority in `FluxAssembler.CompileDelegate`:

```
CompileDelegate(bytecode, definition)
  ├─ FluxILCompiler.Compile()     ← IL emission (preferred, Mono/CoreCLR)
  │   └─ PlatformNotSupportedException → degrade
  └─ FluxJITCompiler.Compile()    ← Expression Tree (universal fallback)
      └─ PlatformNotSupportedException → interpreter
```

Both paths share the same delegate type `CompiledFunc<TData>` and the same cache entry `FormulaCache`. Callers (`FluxInstance`) are unaware of which path produced the delegate.

## Three-Pass Compilation

Unlike the Expression path's "Instruction → Expression → Lambda → Delegate" chain, the IL compiler completes three consecutive passes within a single `Compile` call, producing a delegate via `DynamicMethod.CreateDelegate`.

### Pass 1: Payload Construction

Scans the bytecode and collects data slots from all `Immediate` instructions into an `Instruction[]` payload:

```csharp
int totalDataSlots = 0;
for (int i = 0; i < raw.Length; i++)
{
    if (definition.GetKind(raw[i].OpCode) == OpType.Immediate)
    {
        totalDataSlots += DataSlots;
        i += DataSlots;  // skip TData raw bytes trailing the instruction
    }
}
payload = new Instruction[totalDataSlots];
```

The payload is the runtime argument for the IL delegate (`CompiledFunc<TData>`'s `dataBuffer` parameter). Immediate data is read on demand via `ReadData(payload, index)` in the emitted IL.

### Pass 2: Register Counting

Scans all instruction `Dest` and `Arg0-5` fields to determine the maximum register actually used:

```csharp
byte actualMax = maxRegister > Registers.Bus ? maxRegister : Registers.FirstAlloc;
for (int i = 0; i < raw.Length; i++)
{
    var inst = raw[i];
    if (inst.Dest > actualMax) actualMax = inst.Dest;
    // ... iterate all arg indices ...
}
int regCount = actualMax + 1;
```

The result determines the length of the `TData[]` array allocated in the IL — demand-allocated rather than fixed at 256.

### Pass 3: IL Emission

Creates a `DynamicMethod` and emits IL instruction by instruction:

```csharp
var dm = new DynamicMethod(
    "FluxEval_IL",
    typeof(TData),
    new[] { typeof(Instruction[]) },
    typeof(FluxILCompiler<TData, TDef>).Module,
    skipVisibility: true);

var il = dm.GetILGenerator();

// Register array: TData[] regArr = new TData[regCount]
var regArr = il.DeclareLocal(typeof(TData[]));
il.Emit(OpCodes.Ldc_I4, regCount);
il.Emit(OpCodes.Newarr, typeof(TData));
il.Emit(OpCodes.Stloc, regArr);

// TDef struct local
var defLocal = il.DeclareLocal(typeof(TDef));
il.Emit(OpCodes.Ldloca, defLocal);
il.Emit(OpCodes.Initobj, typeof(TDef));

// ... per-instruction emission ...

return (CompiledFunc<TData>)dm.CreateDelegate(typeof(CompiledFunc<TData>));
```

The `DynamicMethod` is bound to `FluxILCompiler`'s `Module`; `skipVisibility: true` enables access to internal members such as `ReadData`.

## Three-Way Dispatch

Each instruction dispatches to one of three branches based on `Definition.GetKind(opCode)`:

### Immediate: Immediate Load

Reads a `TData` value from the payload by index, writing to the destination register:

```csharp
if (kind == OpType.Immediate)
{
    raw.Slice(ip + 1, DataSlots).CopyTo(payload.AsSpan(currentDataIdx));

    // IL: regArr[inst.Dest] = ReadData(payload, currentDataIdx)
    il.Emit(OpCodes.Ldloc, regArr);          // arr
    il.Emit(OpCodes.Ldc_I4, inst.Dest);      // idx
    il.Emit(OpCodes.Ldarg_0);                // payload
    il.Emit(OpCodes.Ldc_I4, currentDataIdx);  // data index
    il.Emit(OpCodes.Call, ReadDataMethod);   // TData value
    il.Emit(OpCodes.Stelem, typeof(TData));  // arr[idx] = value

    currentDataIdx += DataSlots;
    ip += DataSlots;  // skip data slots
}
```

`ReadData` performs type reinterpretation from `Instruction[]` to `TData*` via `fixed` pinning:

```csharp
private static unsafe TData ReadData(Instruction[] payload, int index)
{
    fixed (Instruction* pBase = payload)
    {
        return *(TData*)(pBase + index);
    }
}
```

### Instruction: Operator Evaluation

Two-tier inlining: Tier B (`EmitOp`) preferred → fallback to Tier A (`Compute` pointer). See the next section.

### Return: Short-Circuit Return

Checks whether R0 (error register) is non-default. If so, returns R0 as error propagation; otherwise returns the destination register:

```csharp
// if (r0 != default) return r0; else return regArr[inst.Dest];
il.Emit(OpCodes.Call, DefaultComparerProp.GetGetMethod()!);
il.Emit(/* ldloc regArr, ldc_i4 0, ldelem */);  // r0 value
il.Emit(/* ldloca defaultTmp, initobj, ldloc */); // default(TData)
il.Emit(OpCodes.Callvirt, EqualsMethod);
var ok = il.DefineLabel();
il.Emit(OpCodes.Brtrue_S, ok);       // r0 == default → return dest
il.Emit(/* ldloc regArr, ldc_i4 0, ldelem, ret */);  // return r0

il.MarkLabel(ok);
il.Emit(/* ldloc regArr, ldc_i4 dest, ldelem, ret */);  // return regArr[dest]
```

## Two-Tier Inlining System

The IL compiler offers two inlining depths for operator evaluation, chosen by the Definition implementor.

### Tier A: Compute Pointer Overload (Default)

`IFluxDefinition<TData>` declares a pointer-based `Compute` overload:

```csharp
TData Compute(byte op, Instruction inst, IntPtr registers, int regCount);
```

The IL compiler calls this method via `constrained.callvirt`. The register array's first-element address (`&arr[0]`) is passed as `IntPtr`; the Definition reads operands and writes results internally via `unsafe` pointers:

```csharp
// Emitted IL:
il.Emit(OpCodes.Ldloca, defLocal);       // &def (this)
il.Emit(OpCodes.Ldc_I4, (int)inst.OpCode); // op
il.Emit(OpCodes.Ldloc, instLocal);        // Instruction
il.Emit(OpCodes.Ldloc, regArr);           // TData[]
il.Emit(OpCodes.Ldc_I4_0);
il.Emit(OpCodes.Ldelema, typeof(TData));  // &arr[0]
il.Emit(OpCodes.Conv_I);                  // → IntPtr
il.Emit(OpCodes.Ldc_I4, regCount);       // regCount
il.Emit(OpCodes.Constrained, typeof(TDef));
il.Emit(OpCodes.Callvirt, ComputePtrMethod);
```

The `Constrained` prefix ensures the value-type `TDef` is never boxed. If a Definition does not override the pointer-based `Compute`, the base default bridges to the managed `Compute` via `Span<T>`.

Tier A advantage: zero additional interface implementation cost. Any type implementing `IFluxJITDefinition<TData>` automatically gains IL path support.

### Tier B: EmitOp Inline Emission (Optional)

Definitions implementing `IFluxILDefinition<TData>` can hand-write IL instructions per opcode:

```csharp
public interface IFluxILDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
{
    bool EmitOp(byte op, Instruction inst, ILGenerator il, LocalBuilder regArr);
}
```

`EmitOp` returns `true` when the opcode has been handled, `false` when unrecognized — the compiler automatically falls back to Tier A:

```csharp
if (HasTierB)
{
    var ilDef = (IFluxILDefinition<TData>)(object)definition;
    tierBHandled = ilDef.EmitOp(inst.OpCode, inst, il, regArr);
}
if (!tierBHandled)
{
    // Fallback Tier A: constrained.callvirt Compute(...)
}
```

Tier B use case: operators with sufficiently simple semantics (e.g., `Add`, `Mul`) where hand-written inline IL eliminates the virtual call overhead entirely. The library's built-in `FloatMathDef` provides a Tier B reference implementation: addition and multiplication directly emit `add`/`mul` IL instructions; other opcodes return `false` to take Tier A.

Tier B requires the caller to understand the IL stack model:
- `regArr` is a `TData[]` local variable, accessed via `ldelem` (read) and `stelem` (write)
- Operands are obtained from register indices via `inst.Arg0-5`
- The result must be written to the register specified by `inst.Dest`
- The `ILGenerator` stack depth is automatically managed by the CLR; manual tracking is unnecessary

## Register Model

The IL compiler uses a `TData[]` array as the register file, not `stackalloc` pointers. Reasons:

1. IL inside a `DynamicMethod` cannot directly reference `stackalloc` memory from the caller's stack frame
2. Arrays are accessed via `ldelem`/`stelem`, natively supported by `ILGenerator`
3. Array memory is contiguous; `&arr[0]` can be passed as `IntPtr` to Tier A's `Compute`

Registers are demand-allocated: Pass 2 determines `regCount`, then an exact-length array is allocated rather than a fixed 256 elements.

## Compiler Selector

`FluxAssembler.CompileDelegate` is the unified entry point for both JIT paths:

```csharp
private static CompiledFunc<TData> CompileDelegate(
    ReadOnlySpan<Instruction> instSpan,
    TDef definition,
    out Instruction[] payload,
    byte maxRegister)
{
    // 1. IL emission (Mono / CoreCLR only)
    if (FluxPlatform.IsIlSupported)
    {
        try
        {
            return FluxILCompiler<TData, TDef>.Compile(
                instSpan, definition, out payload, maxRegister: maxRegister);
        }
        catch (PlatformNotSupportedException) { /* degrade to Expression Tree */ }
    }

    // 2. Expression Tree (IL2CPP fallback)
    return FluxJITCompiler<TData, TDef>.Compile(
        instSpan, definition, out payload, maxRegister: maxRegister);
}
```

Selection logic:
- IL path is preferred when `IsIlSupported` is true
- A `PlatformNotSupportedException` from the IL path silently degrades to Expression Tree
- If Expression Tree also fails, `TryResolveJitDelegate` returns false and the caller degrades to the interpreter

## Relationship with the Expression Path

| Dimension | IL Emission | Expression Tree |
|-----------|------------|-----------------|
| Compilation method | `DynamicMethod` + `ILGenerator` | LINQ Expression Tree + `Compile()` |
| Traversal count | One (three consecutive passes) | Two (Expression object graph + `LambdaCompiler`) |
| Delegate type | `CompiledFunc<TData>` | `CompiledFunc<TData>` |
| Cache entry | `FormulaCache` | `FormulaCache` |
| Chained JIT | Per-link compilation (same as Expression path) | Per-link compilation |
| Platforms | Mono / CoreCLR | All platforms (including IL2CPP) |
| Custom inlining | `IFluxILDefinition.EmitOp` (Tier B) | `GetExpression` (no inlining tiers) |

Both paths have identical runtime execution performance (delegate invocation). The difference exists only at compilation time: the IL path skips AST construction, yielding lower first-compilation latency.

## Platform Constraints

The IL compiler's availability is detected via `FluxPlatform.IsIlSupported`:

```csharp
public static bool IsIlSupported =>
    System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
```

- **Mono / CoreCLR**: `IsDynamicCodeSupported` returns `true`; the IL path is available
- **IL2CPP / NativeAOT**: returns `false`; `CompileDelegate` skips the IL branch entirely, taking the Expression Tree path

`DynamicMethod` on IL2CPP triggers `PlatformNotSupportedException`. The detection logic short-circuits early via `IsIlSupported` at the `CompileDelegate` entry, avoiding the exception throw overhead. The inner try-catch only covers extreme race conditions (e.g., runtime domain switch invalidating `IsDynamicCodeSupported`).

## Next Steps

- [JIT Compilation](./jit.md) — full documentation of the Expression Tree path
- [Platform](./platform.md) — dual-flag (`IsIlSupported` + `IsJitDisabled`) degradation system
- [Pipeline Overview](./overview.md) — four-stage architecture overview
