# 解释器执行循环

`FluxEvaluator<TData, TDef>` 是栈式字节码解释器。它的核心设计问题：**如何在 C# 的托管堆之外，用纯栈内存执行字节码，达到 27ns 的单次求值延迟？**

## 零分配执行栈

```csharp
internal unsafe ref struct FluxEvaluator<TData, TDef>
{
    private readonly TDef _definition;

    public TData Compute(ReadOnlySpan<Instruction> program, byte maxRegister = 0)
    {
        int regCount = maxRegister > 1 ? maxRegister + 1 : FluxPlatform.MaxRegisters;
        TData* regs = stackalloc TData[regCount];
        // ... 执行循环 ...
    }
}
```

`ref struct` 保证 `FluxEvaluator` 自身不会逃逸到堆。`stackalloc TData[regCount]` 在栈上分配寄存器文件——对于 `float` + max 8 个寄存器，仅 32 字节栈空间。寄存器数量由公式头部的 `MaxRegister` 字段按需确定（而非始终分配 255 个）。

## R0/R1 总线约定

两个寄存器有特殊语义：

| 寄存器 | 索引 | 语义 |
|--------|------|------|
| R0（Error） | 0 | 错误标志（未使用，保留） |
| R1（Bus） | 1 | 链式串联总线：前一个 link 的输出流入下一个 link 的首操作数 |

R1 的初始值为 `default(TData)`（对于 `float` 为 `0.0f`）。`Return` 指令将其 Dest 寄存器的值写入 R1（覆盖），供下一个 link 读取。

## 核心执行循环

```csharp
for (int ip = 0; ip < program.Length; )
{
    var inst = program[ip];
    byte opCode = inst.OpCode;

    if (opCode == ReturnOp)
    {
        regs[Registers.Bus] = regs[inst.Dest];
        ip++;
    }
    else if (kind == OpType.Immediate)
    {
        regs[inst.Dest] = *(TData*)(pBase + ip + 1);
        ip += 1 + dataSlots;
    }
    else  // Instruction
    {
        regs[inst.Dest] = _definition.Compute(opCode, inst, new ReadOnlySpan<TData>(regs, regCount));
        ip++;
    }
}
```

- **Immediate**：通过指针重解释直接从指令流中读取 `TData` 值，写入目标寄存器。PC 跳过指令头 + 数据槽。
- **Instruction**：委托给 `_definition.Compute()`，由 Definition 实现具体的算术逻辑。
- **Return**：将 Dest 寄存器的值写入 R1（Bus），等待被下一个 link 消费。

## 解释器 vs JIT 的陷阱：Return 语义

`Return` 指令在解释器和 JIT 中的处理方式截然不同：

- **解释器**：`Return` 将 Dest 写入 R1，然后继续执行下一条指令。链式求值时，下一个 link 的代码紧跟在 Return 之后，从 R1 读取输入。这是"fall-through"语义。
- **JIT**：每条指令被编译为独立的 Expression Tree。`Return` 对应的 Expression 返回其 Dest 的值，由 JIT 委托的调用方（`RunJitChain`）负责注入到下一个 link 的 R1 位置。

这种差异意味着**字节码在解释器和 JIT 路径上是语义等价的——只是执行方式不同**。这是 JIT 一致性测试的核心。

## 为什么不用 switch 分派？

传统字节码解释器使用 `switch(opCode)` 分派。FluxFormula 使用三种分支（Immediate/Instruction/Return），因为：

1. **操作码数量未知**：操作码由 `Definition` 定义——框架不知道会有多少操作符。`switch` 无法在框架层穷举。
2. **语义委托**：`Compute()` 委托将操作语义完全交给 Definition，框架不解释操作码含义。
3. **分支预测友好**：三分类（Immediate/Instruction/Return）的分支模式高度可预测，Immediate 和 Instruction 在字节码中交替出现。

## 两种 `Compute` 重载

```csharp
// 标准求值：R1 初始化为 default(TData)
public TData Compute(ReadOnlySpan<Instruction> raw, byte maxRegister = 0)

// 链式求值：R1 由 prevResult 初始化（前一个 link 的输出）
public TData Compute(ReadOnlySpan<Instruction> raw, TData prevResult, byte maxRegister = 0)
```

第二个重载是链式解释器求值的关键——每个 link 的执行不感知其他 link 的存在，它只看到"R1 已经有了一个值"。
