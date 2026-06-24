# JIT 编译：从字节码到委托

`FluxJITCompiler<TData, TDef>` 将 `Instruction[]` 字节码编译为 LINQ Expression Tree，再编译为可执行委托。它的核心设计问题：**如何将动态操作码（Definition 定义的任意 byte 值）编译为静态类型的委托，同时保持 2ns 的执行延迟？**

## 编译流程

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
    return ((FloatOp)op) switch
    {
        FloatOp.Add => Expression.Add(registers[inst.Arg0], registers[inst.Arg1]),
        FloatOp.Sub => Expression.Subtract(registers[inst.Arg0], registers[inst.Arg1]),
        FloatOp.Mul => Expression.Multiply(registers[inst.Arg0], registers[inst.Arg1]),
        FloatOp.Div => Expression.Divide(registers[inst.Arg0], registers[inst.Arg1]),
        FloatOp.Neg => Expression.Negate(registers[inst.Arg0]),
        // ...
    };
}
```

Definition 将 byte 操作码解释为 LINQ 表达式。框架不关心操作码的语义。

### 3. BlockExpression → Lambda → Delegate

所有指令的表达式组合为一个 `BlockExpression`：

```csharp
var body = Expression.Block(registers, expressions);
var lambda = Expression.Lambda<CompiledFunc>(body, injectorParam);
var compiled = lambda.Compile();  // 或 FastExpressionCompiler
```

`CompiledFunc` 的签名：

```csharp
public delegate TData CompiledFunc(TData[] injector);
```

`injector` 参数是 JIT 模式下的数据缓冲。Immediate 值通过数组索引访问，而非嵌入指令流。

## FastExpressionCompiler

在 `FLUX_FAST_EXPRESSION_COMPILER` 宏定义下，使用 [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) 替代标准的 `Expression.Compile()`：

```csharp
#if FLUX_FAST_EXPRESSION_COMPILER
    var compiled = Expression.Lambda<CompiledFunc>(body, injectorParam).CompileFast();
#else
    var compiled = Expression.Lambda<CompiledFunc>(body, injectorParam).Compile();
#endif
```

FastExpressionCompiler 的优势：避免 `Expression.Compile()` 内部使用的 `System.Reflection.Emit` 的某些开销，编译速度更快。对于简单表达式，差异不大；对于复杂链式公式，差异显著。

## 委托缓存

编译后的委托通过 `GCHandle` 存入 `FormulaCache`：

```csharp
var handle = GCHandle.Alloc(func);
cache.PutDelegate(hash, GCHandle.ToIntPtr(handle));
```

`DualHash64` 作为缓存键。相同字节码的后续 `Instantiate` 调用直接从缓存获取委托，跳过编译。缓存容量由 `FluxConfig.FormulaCacheCapacity` 控制（默认 2048）。这是 JIT 路径能达到 2ns 执行延迟的关键：编译只发生一次，后续都是委托调用。

## Per-link 链式 JIT

链式公式不走 `ToAtomic` 合并。每个 link 独立编译为委托，通过 `CompiledFunc[]` 数组串联执行：

```csharp
for (int i = 0; i < _chainFuncs.Length; i++)
{
    if (i > 0)
        injector = injector.SetIndex(0, prevResult);  // R1 注入
    prevResult = _chainFuncs[i](injector.GetBuffer());
}
```

每个 link 的委托独立缓存。`A.Connect(B).Connect(C)` 在 JIT 路径上共享 `A`、`B`、`C` 各自的缓存，组合爆炸得到控制。

## JIT 失败降级

当平台不支持 `Expression.Compile()`（IL2CPP/AOT）时，编译抛出 `PlatformNotSupportedException`。`FluxAssembler.Instantiate` 捕获此异常并：

1. 调用 `FluxPlatform.DisableJit()`，同进程内后续不再尝试 JIT
2. 回退到解释器路径（`jit: false`）

降级是自动的，调用方无需感知。唯一的差异在性能（2ns → 27ns），不在正确性。
