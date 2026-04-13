# FluxFormula

不可变字节码容器。

## 签名

```csharp
public readonly struct FluxFormula<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 指令数量（含末尾 Return） |
| `Type` | `FluxType` | `Formula`（可独立执行）或 `Modifier`（需拼接） |
| `ImmediateCount` | `int` | Immediate 指令数量，即 `SetIndex()` 的有效索引上限 |
| `VariableSlots` | `VariableSlot[]` | 变量名到槽位索引的映射表，由 Lexer 路径填充 |

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxFormula<TData, TOper>` | 空公式（Count=0），用于 Connect 边界场景 |

## 方法

### Connect

```csharp
public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
```

拼接两个公式。去掉当前公式末尾的 Return 指令，拼接 next 的全部内容。

- 卫语句：若任一方为空则直接返回另一方
- 不重映射寄存器号，需确保 next 不覆写当前公式的寄存器分配

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

返回 `Instruction[]` 的只读视图，仅暴露 `Count` 长度的有效区域。

### ToBytes

```csharp
public byte[] ToBytes()
```

将公式序列化为字节数组。格式：13 字节头（Count + Type + ImmediateCount + VariableSlot 数量）+ 指令区（Count × 8 字节，每条写 Raw 的 long 值）+ 变量槽区（每个槽：nameLen + UTF8 name + slotIndex）。序列化为零开销的 memcpy，无反射。

### FromBytes

```csharp
public static FluxFormula<TData, TOper> FromBytes(byte[] data)
```

从 `ToBytes()` 产出的字节数组反序列化。无需重新编译，字节码直接可用。

```csharp
// 持久化
byte[] raw = formula.ToBytes();
File.WriteAllBytes("damage.ff", raw);

// 加载（零编译）
var loaded = FluxFormula<float, FloatOp>.FromBytes(raw);
float r = runner.Instantiate(loaded).Set("atk", 100f).Run();
```

`FromBytes` 在类型初始化阶段校验 `sizeof(TOper) == 1`，不满足则抛出 `TypeInitializationException`。

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single> [Type: Formula, Instructions: 4]"
```

## 构造

构造函数为 `internal`。用户通过 `FluxAssembler.Compile()` 生成，或使用 `FluxFormula<TData, TOper>.Empty` 获取空实例。
