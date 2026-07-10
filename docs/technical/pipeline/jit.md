# 表达式树编译

FluxFormula 提供两条 JIT 编译路径：**IL 发射**（`FluxILCompiler`，Mono/CoreCLR 优先）和 **表达式树**（`FluxExprCompiler`，全平台回退）。本文档覆盖表达式树路径；IL 路径详见 [IL 发射编译器](./il-compiler.md)。两条路径共享同一委托类型 `CompiledFunc<TData>` 和同一缓存入口 `FormulaCache`，调用方不感知委托来源。

## 表达式树编译流程

`FluxExprCompiler<TData, TDef>` 将 `Instruction[]` 字节码编译为 LINQ Expression Tree，再编译为可执行委托。它的核心设计问题：**如何将动态操作码（Definition 定义的任意 byte 值）编译为静态类型的委托，同时保持 2ns 的执行延迟？**

```
Instruction[] → Expression Tree → Delegate → GCHandle → FormulaCache
```

### 1. 寄存器 → ParameterExpression

每条指令的目标寄存器（`inst.Dest`）被映射为一个 `ParameterExpression`：

```csharp
ParameterExpression[] registers = new ParameterExpression[regCount];
for (int i = 0; i < regCount; i++)
    registers[i] = Expression.Parameter(typeof(TData), $"r{i}");
```

寄存器文件在 Expression Tree 中表示为参数数组。运行时的寄存器是栈上的局部变量。JIT 委托内部由 CLR JIT 进一步编译为机器码后，寄存器参数被映射到 CPU 寄存器或栈槽。

### 2. 指令 → Expression

```
Immediate:  reg[dest] = constant      → Expression.Assign(reg[dest], Expression.Constant(value))
Instruction: reg[dest] = Compute(...)  → Definition.GetExpression(opCode, inst, registers)
Return:     返回 reg[dest]             → registers[inst.Dest]
```

`GetExpression` 是 Definition 实现者提供的核心方法：

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers)
{
    return ((MathOp)op) switch
    {
        MathOp.Add => Expression.Add(registers[inst.Arg0], registers[inst.Arg1]),
        MathOp.Sub => Expression.Subtract(registers[inst.Arg0], registers[inst.Arg1]),
        MathOp.Mul => Expression.Multiply(registers[inst.Arg0], registers[inst.Arg1]),
        MathOp.Div => Expression.Divide(registers[inst.Arg0], registers[inst.Arg1]),
        MathOp.Neg => Expression.Negate(registers[inst.Arg0]),
        // ...
    };
}
```

Definition 将 byte 操作码解释为 LINQ 表达式。框架不关心操作码的语义。

### 3. BlockExpression → Lambda → Delegate

所有指令的表达式组合为一个 `BlockExpression`：

```csharp
var body = Expression.Block(registers, expressions);
var lambda = Expression.Lambda<CompiledFunc<TData>>(body, bufferParam);
var compiled = lambda.Compile();  // 或 FastExpressionCompiler
```

`CompiledFunc` 的签名：

```csharp
internal delegate TData CompiledFunc<TData>(Instruction[] dataBuffer)
    where TData : unmanaged;
```

`dataBuffer` 参数是 JIT 模式下的数据缓冲（`Instruction[]`）。Immediate 值通过指针重解释从 `Instruction[]` 中读取，而非嵌入指令流。

## FastExpressionCompiler

在 `FLUX_FAST_EXPRESSION_COMPILER` 宏定义下，使用 [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) 替代标准的 `Expression.Compile()`：

```csharp
#if FLUX_FAST_EXPRESSION_COMPILER
    var compiled = Expression.Lambda<CompiledFunc<TData>>(body, bufferParam).CompileFast();
#else
    var compiled = Expression.Lambda<CompiledFunc<TData>>(body, bufferParam).Compile();
#endif
```

FastExpressionCompiler 的优势：避免 `Expression.Compile()` 内部使用的 `System.Reflection.Emit` 的某些开销，编译速度更快。对于简单表达式，差异不大；对于复杂链式公式，差异显著。

## 委托缓存

编译后的委托通过 `GCHandle` 存入 `FormulaCache`：

```csharp
var handle = GCHandle.Alloc(func);
cache.PutDelegate(hash, GCHandle.ToIntPtr(handle));
```

`DualHash64` 作为缓存键。相同字节码的后续 `Instantiate` 调用直接从缓存获取委托，跳过编译。缓存容量由 `FluxConfig.FormulaCacheCapacity` 控制（默认 256）。这是 JIT 路径能达到 2ns 执行延迟的关键：编译只发生一次，后续都是委托调用。

`CompiledFunc<TData>` 委托类型和 `FormulaCache` 被 IL 发射路径和 表达式树路径共享。同一字节码仅首次编译时取所选路径；后续命中缓存直接复用已编译委托，不关心来源。

## Per-link 链式 JIT

链式公式**始终**逐 link 编译，不论链长。不检查 `MergeThreshold`，不走 `ToAtomic` 合并。每个 link 独立编译为委托，通过 `CompiledFunc[]` 数组串联执行。此行为与编译路径无关（IL 和 Expression 均适用）。

```csharp
for (int i = 0; i < _chainFuncs.Length; i++)
{
    if (i > 0)
        injector = injector.SetIndex(0, prevResult);  // R1 注入
    prevResult = _chainFuncs[i](injector.GetBuffer());
}
```

### 为什么 JIT 不合并长链？

解释器路径在链长超过 `MergeThreshold`（默认 8）时会将链合并为原子公式求值，因为解释器 per-link 的 `BuildLinkBuffer` 每次分配 `Instruction[]`，长链分配开销不可忽略。JIT 路径始终保留 per-link：

1. **热路径零分配**：每个 link 的 delegate 已预编译，运行时仅有 `SetIndex` 写入和函数指针调用，无堆分配。
2. **LEGO 模型**：每个 link 的委托独立缓存在 `FormulaCache` 中。`A.Connect(B).Connect(C)` 共享 `A`、`B`、`C` 各自已缓存的委托，即使它们出现在不同链中。合并为原子公式后将失去这种 link 级复用。
3. **编译成本前置**：合并后的公式是唯一字节码组合，需要重新走 Expression Tree → delegate 编译。保留 per-link 意味着编译成本已分摊到各个 link 的首次使用中。

两条路径的不对称是有意的：解释器按分配成本决策合并，JIT 按缓存复用保留链式。

## JIT 降级

平台降级逻辑统一由 `FluxPlatform` 和 `CompileDelegate` 管理，详见 [平台适配](./platform.md)。表达式树路径在 IL2CPP/AOT 平台抛出 `PlatformNotSupportedException` 后，`CompileDelegate` 内部已先行尝试 IL 路径（如平台支持），Expression 是第二条防线；两次均失败后最终降级到解释器。
