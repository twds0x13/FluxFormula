# FormulaFormat

原子公式（`.ff`）字节码格式定义与读写辅助。

## 字节布局

```
Header (14B): Count(4 LE) + Type(1) + ImmediateCount(4 LE) + VarSlotCount(4 LE) + MaxRegister(1)
Body:         Instruction[Count] (Count × 8B)
Tail:         VariableSlot[] — 每槽: NameLen(4 LE) + UTF8 + SlotIndex(4 LE)
```

与 `VffFormat` 的关系：VFF 通过 `"VFF\0"` magic 与公式条目区分。读取侧先调用 `VffFormat.IsVff()` 判类型。

## 常量

| 常量 | 值 | 说明 |
|------|------|------|
| `HeaderSize` | `14` | 头部大小（字节） |
| `InstructionOffset` | `14` | Instruction 段起始偏移（= HeaderSize） |

## 静态属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `InstructionSize` | `int` | 每条 Instruction 的字节数 = `sizeof(Instruction)`。自动跟踪结构体大小变化 |

## 静态方法

### DataSlots\<TData\>

```csharp
public static int DataSlots<TData>() where TData : unmanaged
```

TData 值在 Instruction 数组中占用的槽位数 = `ceil(sizeof(TData) / sizeof(Instruction))`。项目中所有 dataSlots 计算的唯一来源。

```csharp
// float(4B) / Instruction(8B) = 1 slot
int slots = FormulaFormat.DataSlots<float>(); // 1

// Vector3(12B) / Instruction(8B) = 2 slots
int vecSlots = FormulaFormat.DataSlots<Vector3>(); // 2
```

### ReadHeader

```csharp
public static FormulaHeader ReadHeader(ReadOnlySpan<byte> data)
```

从字节码中读取 14 字节头，返回 `FormulaHeader` 结构体。

### ReadVariableSlots

```csharp
public static VariableSlot[] ReadVariableSlots(
    ReadOnlySpan<byte> data, int baseSlotOffset = 0)
```

从字节码尾部读取变量槽列表。`baseSlotOffset` 用于链式公式中偏移 SlotIndex。

### WriteHeader

```csharp
public static void WriteHeader(byte[] data, ref int offset, FormulaHeader header)
```

将 `FormulaHeader` 写入字节数组，`offset` 自动推进 14 字节。

### GetInstructionSpan

```csharp
public static ReadOnlySpan<Instruction> GetInstructionSpan(ReadOnlySpan<byte> data)
```

从完整字节码中提取 Instruction 段（跳过 14 字节头），返回 `ReadOnlySpan<Instruction>`。

### IsFormula

```csharp
public static bool IsFormula(ReadOnlySpan<byte> bytes)
```

检测一段字节码是否为公式格式（非 VFF，无 `"VFF\0"` magic）。与 `VffFormat.IsVff()` 互补。

## FormulaHeader 结构体

| 字段 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | Instruction 条数 |
| `Type` | `FluxType` | Formula / Modifier |
| `ImmediateCount` | `int` | Immediate 指令数量 |
| `VarSlotCount` | `int` | 变量槽数量 |
| `MaxRegister` | `byte` | 编译期分析的最高寄存器索引 |

## 修改 Instruction 结构体大小时的步骤

1. 调整 `Instruction.cs` 的 `FieldOffset` 和 `Raw` 字段
2. 更新 `FluxFormula.ToBytes()` / `FromBytes()` 序列化
3. 重新生成所有 blob（`FluxBlobBuilder.Build()`）
4. 运行全量测试确认

`InstructionSize` 和 `DataSlots<TData>` 自动通过 `sizeof` 跟踪，无需手动更新。

## 参见

- [VffFormat](./vff-format) — VFF 条目格式
- [DualHash64](./dualhash64) — 字节码完整性验证
- [BinaryFormat](./overview) — 小端序二进制读写原语（API 总览）
