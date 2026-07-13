# JIT 注入器: FluxJITInjector

其核心设计问题：JIT 编译后的委托在执行时只需要按 SlotIndex 直接写入字节码缓冲区，不需要变量名映射、修饰符检测或值回读。为何要为这些不需要的功能付出分支代价？

答案是将 JIT 热路径注入逻辑拆分为独立类型 `FluxJITInjector<TData>`：2 个字段，零分支，`ref struct` 栈分配。完整的 `FluxInjector<TData>`（11 字段，多分支）保留给解释器路径和链式求值。

## 拆分原因（v5.7.1）

v5.7.0 之前，JIT 编译后的委托在执行时使用的注入器与解释器是同一个 `FluxInjector<TData>`：

- 11 个字段：`_buffer`、`_offsets`、`_slotsPerData`、`_varNames`、`_varSlotIndexes`、`_varCount`、`_values` 等
- `SetIndex(int index, TData value)` 包含多个分支：`_offsets == null` 检查（JIT 模式 vs 解释器模式）、偏移表查找、边界检查
- JIT 热路径中每次变量注入都经过这些分支，即使 JIT 场景只需要最简单的 `buffer[offset] = value`

v5.7.1 将 JIT 热路径注入逻辑拆分为独立类型：

## 实现

```csharp
internal readonly struct FluxJITInjector<TData>
    where TData : unmanaged
{
    private readonly Instruction[] _buffer;   // payload 缓冲区（共享引用）
    private readonly int _slotsPerData;       // DataSlots<TData>()

    internal FluxJITInjector(Instruction[] buffer)
    {
        _buffer = buffer;
        _slotsPerData = FormulaFormat.DataSlots<TData>();
    }

    internal readonly FluxJITInjector<TData> SetIndex(int paramIndex, TData value)
    {
        int offset = paramIndex * _slotsPerData;
        unsafe
        {
            fixed (Instruction* pBase = _buffer)
                *(TData*)(pBase + offset) = value;
        }
        return this;
    }
}
```

核心特征：
- **2 个字段**：`_buffer`（payload 缓冲区） + `_slotsPerData`（每个 TData 占用的指令槽位数）
- **零分支**：`SetIndex` 直接计算偏移量，指针写入，无任何条件判断
- **零字典查询**：不需要变量名到 SlotIndex 的映射（JIT 编译器在编译期已经确定了 SlotIndex）
- **零值回读**：不需要 `_values` 数组（链式 JIT 的 `BuildLinkBuffer` 不需要从此注入器读回值）

## 性能对比

| | FluxInjector（完整） | FluxJITInjector |
|---|---|---|
| 字段数 | 11 | 2 |
| SetIndex 分支 | 2（`_offsets == null` + 偏移查找） | 0 |
| 名称查找 | 二分查找 O(log n) | 不需要 |
| 值回读 | `_values[]` 数组 | 不需要 |
| 分配 | 堆（含数组字段） | 栈（`Instruction[]` 为共享引用） |
| 用途 | 解释器 + 链式 JIT 合并读回 | JIT 热路径 |

## 双路径设计

编译期选择注入器类型，不做运行时多态：

```mermaid
flowchart TD
    A[FluxAssembler.Instantiate] --> B{JIT 启用?}
    B -->|是| C{链式?}
    B -->|否| D[FluxInjector + 解释器]
    C -->|单公式| E[FluxJITInjector + CompiledFunc]
    C -->|链式| F[FluxInjector + per-link CompiledFunc[]]
```

- **JIT 热路径**（单公式）：走 `FluxJITInjector`。JIT 委托是 `CompiledFunc<TData>(Instruction[] dataBuffer)` 签名，注入器在调用栈上作为值类型传递，零 GC。
- **链式 JIT**：仍走 `FluxInjector`。`BuildLinkBuffer` 需要从注入器读回上游 link 的输出值（通过 `GetValue`），`FluxJITInjector` 不支持值回读。
- **解释器路径**：永远走 `FluxInjector`。需要完整的变量名映射和修饰符检测。

## 设计决策

1. **编译期选择，非运行时分支**：`FluxJITCompiler` 在编译阶段决定使用哪种注入器，不在运行时判断 `_offsets == null`。

2. **共享引用而非拷贝**：`_buffer` 是 `Instruction[]` 引用，`FluxJITInjector` 与其调用方共享同一数组。注入器本身不拥有缓冲区。

3. **原 FluxInjector 保留所有功能**：解释器需要完整的变量槽位映射、修饰符支持、链式 JIT 合并读回能力，这些不因拆分而移除。

4. **不是 ref struct**：`FluxJITInjector` 需要作为方法返回值传递，因此是普通 `readonly struct`。但其字段中唯一的引用类型（`Instruction[]`）是共享的，不涉及堆分配的拷贝。

## 参考

- [数据注入器](./injector.md) — FluxInjector 完整注入器（解释器路径）
- [解释器执行循环](./evaluator.md) — 热路径求值器
- [表达式树编译](./jit.md) — JIT 委托编译
- [架构决策记录](../architecture-decisions.md) — ADR v5.7.0 FluxJITInjector 拆分
