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
internal unsafe struct FluxInjector<TData> where TData : unmanaged
{
    private readonly Instruction[] _buffer;        // 字节码缓冲（共享引用）
    private readonly int[] _offsets;               // 解释器模式: Immediate 在缓冲中的偏移量
    private readonly FluxFormula.VariableSlot[] _variableSlots; // 变量名→位置映射
    private readonly int _slotsPerData;            // 每个 TData 占用的 Instruction 槽位数
}
```

`_buffer` 是共享引用——FluxInjector 不拥有缓冲，只持有引用并写入。这避免了拷贝，但也意味着多个 FluxInstance 不能并发操作同一缓冲（Unity 主线程场景下这不是问题）。

## SetByIndex — 按索引注入

```csharp
public FluxInjector<TData> SetIndex(int paramIndex, TData value)
{
    int offset;
    if (_offsets == null)  // JIT 模式
    {
        offset = paramIndex * _slotsPerData;
    }
    else                   // 解释器模式
    {
        offset = _offsets[paramIndex];
    }

    fixed (Instruction* pBase = _buffer)
    {
        *(TData*)(pBase + offset) = value;  // 指针重解释写入
    }
    return this;
}
```

关键细节：
- `pBase + offset` 的 offset 单位是"Instruction 个数"而非字节。`Instruction*` 算术自动乘 `sizeof(Instruction)` = 8。
- `*(TData*)(pBase + offset)` 将 Instruction 槽位的地址重解释为 TData 指针，直接写入。无需 memcpy，无 boxing。
- 返回 `this` 支持链式调用。

## SetByName — 按变量名注入

`Set(name, value)` 需要将变量名映射到 paramIndex，这是注入器最有技术含量的部分。

### 为什么不用 Dictionary？

标准的 C# 方案是用 `Dictionary<string, int>` 做名称→索引映射。FluxFormula 没有采用，原因：

1. **Dictionary 是堆分配类型**。即使缓存 Dictionary 实例，每次查找也有 virtual dispatch 开销。
2. **公式编译后变量名固定**。VariableSlots 在编译时按变量名字典序排序，天然支持二分查找。
3. **变量数量通常很少**。游戏公式通常 2-20 个变量，内联二分查找的常数因子比 Dictionary 更优。

### 实现：内联二分查找

```csharp
public FluxInjector<TData> Set(string name, TData value)
{
    // 在 VariableSlots 中二分查找变量名
    int lo = 0, hi = _variableSlots.Length - 1;
    while (lo <= hi)
    {
        int mid = (lo + hi) / 2;
        int cmp = string.CompareOrdinal(name, _variableSlots[mid].Name);
        if (cmp == 0)
        {
            // 找到：该变量名的所有出现位置批量更新
            int slotIndex = _variableSlots[mid].SlotIndex;
            // ... 更新所有 slotIndex 对应的位置
            return this;
        }
        if (cmp < 0) hi = mid - 1;
        else lo = mid + 1;
    }
    throw new ArgumentException($"Variable '{name}' not found.");
}
```

值得注意的设计选择：

- **`string.CompareOrdinal`** 而非 `CompareTo`。序数比较避免文化敏感排序，性能更好且行为一致。
- **同名变量的所有出现位置同时更新**。一个变量名可以在公式中出现多次（如 `[atk] * 2 + [atk]`），所有出现共享同一个 SlotIndex，一条 Set 覆写所有位置。
- **排序在编译期完成**。FluxCompiler 在生成 VariableSlots 时按 Name 排序，运行时零排序开销。

### 排序策略：并行数组

FluxCompiler 维护两���并行数组：
- `List<string> varNames`：变量名
- `List<int> varPositions`：对应在 Instruction[] 中的位置

编译完成后，两者按变量名联合排序（任何稳定排序算法均可），变名相同的条目在排序后聚拢。然后压缩为 VariableSlot[]，相邻的同名条目合并为单条记录。

这种"并行数组 + 排序 + 压缩"的策略比 Dictionary 更 GC 友好：排序在编译器的可变 List 上完成，最终产物 VariableSlot[] 是紧凑的只读数组。

## JIT 模式的特殊处理

JIT 模式下 `_offsets` 为 null，偏移量通过 `paramIndex * _slotsPerData` 计算。这意味着 JIT payload 必须严格按 paramIndex 顺序排列 TData，不能有空洞。

JIT 编译器在 `Compile()` 时已经确定了 paramIndex 到 payload 位置的映射关系。它生成一个与变量数无关的 Expression Tree——变量值通过 `GetData<TData>(buffer, index)` 在运行时从 payload 中读取。

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

- [管线全景](./overview.md) — 回到管线总览
