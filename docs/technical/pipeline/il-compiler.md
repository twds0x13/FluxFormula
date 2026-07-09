# IL 发射编译器

`FluxILCompiler<TData, TDef>` 通过 `DynamicMethod` + `ILGenerator` 直接将字节码编译为可执行委托，跳过 表达式树的 AST 构建和二次遍历开销。它的核心设计问题：**如何在运行时动态生成 IL 指令，既保持零 GC 执行特性，又兼容 AOT 平台安全降级？**

## 为什么需要 IL 路径

表达式树路径（`FluxExprCompiler`）的瓶颈在编译过程，而非执行。构建 `Expression` 节点树后再调用 `Compile()` 是两次遍历：第一次构建 Expression 对象图，第二次 `LambdaCompiler` 将其转为 IL。对于高频率公式编译场景（如脚本热重载、大量链式公式首次实例化），这层中间表示的开销可以避免。

IL 发射跳过中间 AST，直接生成委托体 IL，将两次遍历合并为一次。代价是平台受限：`DynamicMethod` 依赖 `System.Reflection.Emit`，仅在 Mono 和 CoreCLR 上可用，IL2CPP 不可用。

## 编译器定位

JIT 委托编译有两条路径，在 `FluxAssembler.CompileDelegate` 中按优先级选择：

```
CompileDelegate(bytecode, definition)
  ├─ FluxILCompiler.Compile()     ← IL 发射 (Mono/CoreCLR 优先)
  │   └─ PlatformNotSupportedException → 降级
  └─ FluxExprCompiler.Compile()    ← 表达式树 (全平台回退)
      └─ PlatformNotSupportedException → 解释器
```

两条路径共享同一委托类型 `CompiledFunc<TData>` 和同一缓存入口 `FormulaCache`。调用方 (`FluxInstance`) 不感知委托来自哪条路径。

## 三遍编译

与 Expression 路径的"指令→Expression→Lambda→Delegate"链式转换不同，IL 编译器在一次 `Compile` 调用内完成三个连续 Pass，最终通过 `DynamicMethod.CreateDelegate` 产出委托。

### Pass 1: Payload 构建

扫描字节码，收集所有 `Immediate` 类型指令的数据槽，填充到 `Instruction[]` payload 中：

```csharp
int totalDataSlots = 0;
for (int i = 0; i < raw.Length; i++)
{
    if (definition.GetKind(raw[i].OpCode) == OpType.Immediate)
    {
        totalDataSlots += DataSlots;
        i += DataSlots;  // 跳过指令后的 TData 原始字节
    }
}
payload = new Instruction[totalDataSlots];
```

payload 是 IL 委托的运行时参数（`CompiledFunc<TData>` 的 `dataBuffer` 参数）。立即数数据在 IL 中通过 `ReadData(payload, index)` 按需读取。

### Pass 2: 寄存器计数

扫描全部指令的 `Dest` 和 `Arg0-5` 字段，确定实际使用的最大寄存器号：

```csharp
byte actualMax = maxRegister > Registers.Bus ? maxRegister : Registers.FirstAlloc;
for (int i = 0; i < raw.Length; i++)
{
    var inst = raw[i];
    if (inst.Dest > actualMax) actualMax = inst.Dest;
    // ... 遍历所有 arg 索引 ...
}
int regCount = actualMax + 1;
```

结果决定 IL 中分配的 `TData[]` 数组长度，按需分配而非固定 256。

### Pass 3: IL 发射

创建 `DynamicMethod` 并逐条指令发射 IL：

```csharp
var dm = new DynamicMethod(
    "FluxEval_IL",
    typeof(TData),
    new[] { typeof(Instruction[]) },
    typeof(FluxILCompiler<TData, TDef>).Module,
    skipVisibility: true);

var il = dm.GetILGenerator();

// 寄存器数组分配: TData[] regArr = new TData[regCount]
var regArr = il.DeclareLocal(typeof(TData[]));
il.Emit(OpCodes.Ldc_I4, regCount);
il.Emit(OpCodes.Newarr, typeof(TData));
il.Emit(OpCodes.Stloc, regArr);

// TDef 结构体本地变量
var defLocal = il.DeclareLocal(typeof(TDef));
il.Emit(OpCodes.Ldloca, defLocal);
il.Emit(OpCodes.Initobj, typeof(TDef));

// ... 逐条指令发射 ...

return (CompiledFunc<TData>)dm.CreateDelegate(typeof(CompiledFunc<TData>));
```

`DynamicMethod` 绑定到 `FluxILCompiler` 的 `Module`，`skipVisibility: true` 允许访问 internal 成员（如 `ReadData` 方法）。

## 三路调度

每条指令按 `Definition.GetKind(opCode)` 的结果分派到三个分支：

### Immediate: 立即数加载

从 payload 中按索引读取 TData 值，写入目标寄存器：

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
    ip += DataSlots;  // 跳过数据槽
}
```

`ReadData` 通过 `fixed` pinning 实现 `Instruction[]` 到 `TData*` 的类型重解释：

```csharp
private static unsafe TData ReadData(Instruction[] payload, int index)
{
    fixed (Instruction* pBase = payload)
    {
        return *(TData*)(pBase + index);
    }
}
```

### Instruction: 操作符求值

二级内联：优先 Tier B（`EmitOp`）→ 回退 Tier A（`Compute` 指针重载）。详见下节。

### Return: 短路返回

检查 R0（错误寄存器）是否非 default。若非 default，返回 R0 作为错误传播；若为 default，返回目标寄存器值：

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

## 二级内联体系

IL 编译器为操作符求值提供两层内联深度，由 Definition 实现者自主选择。

### Tier A: Compute 指针重载（默认路径）

`IFluxDefinition<TData>` 声明了指针版 `Compute` 重载：

```csharp
TData Compute(byte op, Instruction inst, IntPtr registers, int regCount);
```

IL 编译器通过 `constrained.callvirt` 调用此方法。寄存器数组的首元素地址（`&arr[0]`）作为 `IntPtr` 传入，Definition 内部通过 `unsafe` 指针读取操作数并写回结果：

```csharp
// 生成的 IL:
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

`Constrained` 前缀确保值类型的 `TDef` 不会被装箱。如果 Definition 未覆写指针版 `Compute`，基类默认实现通过 `Span<T>` 桥接到托管版 `Compute`。

Tier A 的优势：零额外接口实现成本。任何实现 `IFluxExprDefinition<TData>` 的类型自动获得 IL 路径支持。

### Tier B: EmitOp 内联发射（可选优化）

实现 `IFluxILDefinition<TData>` 接口的 Definition 可以为每个操作码手写 IL 指令：

```csharp
public interface IFluxILDefinition<TData> : IFluxDefinition<TData>
    where TData : unmanaged
{
    bool EmitOp(byte op, Instruction inst, ILGenerator il, LocalBuilder regArr);
}
```

`EmitOp` 返回 `true` 表示已处理此操作码，`false` 表示不识别，编译器自动回退 Tier A：

```csharp
if (HasTierB)
{
    var ilDef = (IFluxILDefinition<TData>)(object)definition;
    tierBHandled = ilDef.EmitOp(inst.OpCode, inst, il, regArr);
}
if (!tierBHandled)
{
    // 回退 Tier A: constrained.callvirt Compute(...)
}
```

Tier B 的场景：操作符语义足够简单（如 `Add`、`Mul`），手写内联 IL 完全消除虚调用开销。库内置的 `FloatMathDef` 提供了 Tier B 的参考实现：加法和乘法直接发射 `add`/`mul` IL 指令，其他操作码返回 `false` 走 Tier A。

Tier B 要求调用方理解 IL 栈模型：
- `regArr` 是 `TData[]` 类型本地变量，通过 `ldelem` 读取、`stelem` 写入
- 操作数通过 `inst.Arg0-5` 获取寄存器索引
- 结果必须写入 `inst.Dest` 指定的寄存器
- `ILGenerator` 的栈深度由 CLR 自动管理，无需手动追踪

## 寄存器模型

IL 编译器使用 `TData[]` 数组作为寄存器文件，而非 `stackalloc` 指针。原因：

1. `DynamicMethod` 的 IL 中无法直接引用调用方栈帧的 `stackalloc` 内存
2. 数组通过 `ldelem`/`stelem` 访问，ILGenerator 原生支持
3. 数组内存连续，`&arr[0]` 可作为 `IntPtr` 传入 Tier A 的 `Compute`

寄存器采用按需分配：Pass 2 确定 `regCount` 后分配精确长度的数组，而非固定 256 个元素。

## 编译器选择器

`FluxAssembler.CompileDelegate` 是两条 JIT 路径的统一入口：

```csharp
private static CompiledFunc<TData> CompileDelegate(
    ReadOnlySpan<Instruction> instSpan,
    TDef definition,
    out Instruction[] payload,
    byte maxRegister)
{
    // 1. IL 发射（仅 Mono / CoreCLR）
    if (FluxPlatform.IsIlSupported)
    {
        try
        {
            return FluxILCompiler<TData, TDef>.Compile(
                instSpan, definition, out payload, maxRegister: maxRegister);
        }
        catch (PlatformNotSupportedException) { /* 降级到 表达式树 */ }
    }

    // 2. 表达式树（IL2CPP 回退）
    return FluxExprCompiler<TData, TDef>.Compile(
        instSpan, definition, out payload, maxRegister: maxRegister);
}
```

选择逻辑：
- `IsIlSupported` 为 true 时优先 IL 路径
- IL 路径抛出 `PlatformNotSupportedException` 时静默降级到 表达式树
- 表达式树再次失败时，`TryResolveJitDelegate` 返回 false，调用方降级到解释器

## 与 Expression 路径的关系

| 维度 | IL 发射 | 表达式树 |
|------|--------|---------------|
| 编译方式 | `DynamicMethod` + `ILGenerator` | LINQ Expression Tree + `Compile()` |
| 遍历次数 | 一次（三 Pass 连续） | 两次（Expression 对象图 + `LambdaCompiler`） |
| 委托类型 | `CompiledFunc<TData>` | `CompiledFunc<TData>` |
| 缓存入口 | `FormulaCache` | `FormulaCache` |
| 链式 JIT | 逐 link 编译（与 Expression 路径相同） | 逐 link 编译 |
| 平台 | Mono / CoreCLR | 全平台（含 IL2CPP） |
| 自定义内联 | `IFluxILDefinition.EmitOp`（Tier B） | `GetExpression`（无内联层级） |

两条路径的运行时执行性能相同（委托调用）。差异仅存在于编译阶段：IL 路径跳过 AST 构建，首次编译延迟更低。

## 平台约束

IL 编译器通过 `FluxPlatform.IsIlSupported` 检测可用性：

```csharp
public static bool IsIlSupported =>
    System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
```

- **Mono / CoreCLR**：`IsDynamicCodeSupported` 返回 `true`，IL 路径可用
- **IL2CPP / NativeAOT**：返回 `false`，`CompileDelegate` 直接跳过 IL 分支，走 表达式树

`DynamicMethod` 在 IL2CPP 平台触发 `PlatformNotSupportedException`。检测逻辑在 `CompileDelegate` 入口处通过 `IsIlSupported` 提前短路，避免异常抛出开销。内层 try-catch 仅捕获极端竞争条件（如运行时域切换导致 `IsDynamicCodeSupported` 结果失效）。

## 下一步

- [JIT 编译](./jit.md)：表达式树路径的完整文档
- [平台适配](./platform.md)：`IsIlSupported` 和 `IsJitDisabled` 双标志降级系统
- [管线全景](./overview.md)：四阶段架构总览
