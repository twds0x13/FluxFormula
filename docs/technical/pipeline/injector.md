# 数据注入器

`FluxInjector<TData>` 负责将用户参数写入公式字节码缓冲。它的核心设计问题：**如何在一个紧凑的 `Instruction[]` 中高效定位并覆写特定变量的值，同时保持零 GC？**

## 两种注入模式

FluxInjector 有两种工作模式，取决于执行后端：

| 模式 | 触发条件 | 数据布局 | 定位方式 |
|------|---------|---------|---------|
| **JIT 模式** | `jit: true` | payload 数组（紧凑的 TData 序列） | 线性索引：`paramIndex * slotsPerData` |
| **解释器模式** | `jit: false` | 完整公式缓冲（Instruction[] + 内联 TData） | 偏移量数组：`_offsets[paramIndex]` |

两种模式的根本差异来自 Instruction 在 JIT 和解释器中的不同角色：
- **JIT 模式**：指令和数据分离。指令成为 Expression Tree（编译期消耗），数据留在 payload 数组中供运行时读取。payload 是紧凑的 TData 序列，无 Instruction 头。
- **解释器模式**：指令和数据共存于同一个 Instruction[] 缓冲中。TData 紧跟在 Immediate 指令头后面，通过指针偏移写入。

## 核心数据结构

```csharp
internal readonly struct FluxInjector<TData> where TData : unmanaged
{
    private readonly Instruction[] _buffer;         // 字节码缓冲（共享引用）
    private readonly int[] _offsets;                // 解释器模式: Immediate 在缓冲中的偏移量
    private readonly int _slotsPerData;             // 每个 TData 占用的 Instruction 槽位数

    // 变量查找：并行数组，二分查找，零 GC
    private readonly string[] _varNames;            // 唯一变量名（字典序）
    private readonly int[][] _varSlotIndexes;       // 每个变量名对应的所有 SlotIndex
    private readonly int _varCount;                 // 唯一变量数

    // 值回读数组：按 SlotIndex 索引，Set/SetIndex 写入，GetValue 读取
    private readonly TData[] _values;
}
```

关键变化（相比旧架构）：变量名查找从单一 `VariableSlot[]` 改为**并行数组 `_varNames[]` + `_varSlotIndexes[][]`**。每组同名变量在 `_varSlotIndexes` 中存储为 `int[]` 数组，一条 `Set` 同时覆写所有出现位置。`_values[]` 按 SlotIndex 存值，`GetValue()` 用 O(1) 回读，供链式求值 `BuildLinkBuffer` 使用。

## SetIndex：按索引注入

```csharp
internal readonly FluxInjector<TData> SetIndex(int paramIndex, TData value)
{
    // 值回读（链式求值时 BuildLinkBuffer 依赖此数组）
    if (_values != null && paramIndex < _values.Length)
        _values[paramIndex] = value;

    int offset;
    if (_offsets == null)  // JIT 模式
    {
        offset = paramIndex * _slotsPerData;
        if (offset + _slotsPerData > _buffer.Length)
            throw new IndexOutOfRangeException(
                $"Parameter index {paramIndex} is out of bounds.");
    }
    else                   // 解释器模式
    {
        if (paramIndex < 0 || paramIndex >= _offsets.Length)
            throw new IndexOutOfRangeException(
                $"Parameter index {paramIndex} is out of bounds.");
        offset = _offsets[paramIndex];
    }

    unsafe
    {
        fixed (Instruction* pBase = _buffer)
            *(TData*)(pBase + offset) = value;
    }
    return this;
}
```

关键细节：
- `pBase + offset` 的 offset 单位是"Instruction 个数"而非字节。`Instruction*` 算术自动乘 `sizeof(Instruction)` = 8。
- `*(TData*)(pBase + offset)` 将 Instruction 槽位的地址重解释为 TData 指针，直接写入。无需 memcpy，无 boxing。
- 两种模式均有越界检查，抛出 `IndexOutOfRangeException`。
- 返回 `this` 支持链式调用。

## Set：按变量名注入

`Set(name, value)` 需要将变量名映射到 paramIndex，这是注入器最有技术含量的部分。

### 为什么不用 Dictionary？

标准的 C# 方案是用 `Dictionary<string, int>` 做名称→索引映射。FluxFormula 没有采用，原因：

1. **Dictionary 是堆分配类型**。即使缓存 Dictionary 实例，每次查找也有 virtual dispatch 开销。
2. **公式编译后变量名固定**。VariableSlots 在编译时按变量名字典序排序，天然支持二分查找。
3. **变量数量通常很少**。游戏公式通常 2-20 个变量，内联二分查找的常数因子比 Dictionary 更优。

### 实现：内联二分查找

```csharp
internal readonly FluxInjector<TData> Set(string name, TData value)
{
    int lo = 0, hi = _varCount - 1;
    while (lo <= hi)
    {
        int mid = lo + (hi - lo) / 2;
        int cmp = string.CompareOrdinal(_varNames[mid], name);
        if (cmp == 0)
        {
            int[] slotIndexes = _varSlotIndexes[mid];
            unsafe
            {
                fixed (Instruction* pBase = _buffer)
                {
                    for (int i = 0; i < slotIndexes.Length; i++)
                    {
                        int si = slotIndexes[i];
                        if (_values != null && si < _values.Length)
                            _values[si] = value;

                        int offset = _offsets != null
                            ? _offsets[si]
                            : si * _slotsPerData;
                        *(TData*)(pBase + offset) = value;
                    }
                }
            }
            return this;
        }
        if (cmp < 0) lo = mid + 1;
        else         hi = mid - 1;
    }
    throw new ArgumentException($"Variable '{name}' is not defined in this formula.");
}
```

- **`string.CompareOrdinal`**：序数比较避免文化敏感排序。
- **同名变量的所有出现位置同时更新**：通过 `_varSlotIndexes[mid]`（`int[]` 数组）一次遍历覆写所有位置。
- **安全中点计算**：`lo + (hi - lo) / 2` 避免整数溢出。
- **`_values` 回写**：每个覆写位置同时更新 `_values[si]`，供 `GetValue()` O(1) 回读。

### 排序策略：构造时去重 + 排序

FluxInjector 的第二个构造函数在运行时从 `VariableSlot[]` 构建查找表：

1. 遍历 `varSlots` 去重统计唯一变量名
2. 为每个唯一名创建 `int[]` 数组收集所有 `SlotIndex`
3. 通过 `Array.Sort(_varNames, _varSlotIndexes, ...)` 按名字典序联合排序
4. 查找时二分 `_varNames[]`，命中后遍历 `_varSlotIndexes[mid]` 更新所有槽位

排序在 Injector 构造时一次性完成，后续每次 `Set` 调用零分配。

## JIT 模式的特殊处理

JIT 模式下 `_offsets` 为 null，偏移量通过 `paramIndex * _slotsPerData` 计算。这意味着 JIT payload 必须严格按 paramIndex 顺序排列 TData，不能有空洞。

JIT 编译器在 `Compile()` 时已经确定了 paramIndex 到 payload 位置的映射关系。它生成一个与变量数无关的 Expression Tree。变量值通过 `GetData<TData>(buffer, index)` 在运行时从 payload 中读取。

## 指针写入的安全性

```csharp
fixed (Instruction* pBase = _buffer)
{
    *(TData*)(pBase + offset) = value;
}
```

这个写入的安全性由以下保证：
- `TData : unmanaged` 约束确保类型可安全通过指针访问
- `sizeof(TData)` 可变，但 `_slotsPerData = (sizeof(TData) + 7) / 8` 保证了足够的 Instruction 槽位
- 越界检查在 `SetIndex` 入口处完成（但未对极大值做溢出保护，参见[技术分析](../technical-analysis.md#27-fluxinjectorcs)）

## 下一步

- [管线全景](./overview.md)：回到管线总览
