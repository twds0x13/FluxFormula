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

链式组合两条公式。不合并字节码——追加 `ChainLink` 引用切片。物理拼接推迟到求值时刻。

- 卫语句：任一方为空则直接返回另一方
- `next` 若非 Modifier，保持原样（第一操作数不从 R1 读取）。若需 `next` 消费当前公式的输出，显式使用 `next.ToMultiplier()`
- 参见 [ChainLink 深度解析](../technical/chainlink-deep-dive)

### ToMultiplier

```csharp
public FluxFormula<TData, TOper> ToMultiplier()
```

将 Formula 转为 Modifier。移除第一 Immediate 指令及数据槽位，将其 dest 寄存器重命名为 1（R1）。已为 Modifier 则返回自身。链式公式先 `ToAtomic` 再转换。

### ToFormula

```csharp
public FluxFormula<TData, TOper> ToFormula(string varName)
```

将 Modifier 转为 Formula。插入以 `varName` 命名的 Immediate 指令替代 R1 输入，R1 引用重命名为新寄存器。已为 Formula 则返回自身。

### ToAtomic

```csharp
internal FluxFormula<TData, TOper> ToAtomic()
```

将链式公式合并为原子公式。所有 link 的 `Instruction[]` 完整拼接（含中间 Return）。JIT 路径和长链（>8）解释器路径自动调用。

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

返回公式字节码的 `DualHash64`。原子公式哈希等价于 `ToBytes()` 的哈希；链式公式为所有 link Key 的顺序 `Combine`。用于缓存键计算。

### IsChained / ChainLength

```csharp
public bool IsChained { get; }
public int ChainLength { get; }
```

`IsChained`：公式是否为链式（内部持有 `ChainLink[]`）。`ChainLength`：链式公式的 link 数量，原子公式返回 0。

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

返回 `Instruction[]` 的只读视图，仅暴露 `Count` 长度的有效区域。

### ToBytes

```csharp
public byte[] ToBytes()
```

将公式序列化为字节数组。格式：14 字节头（Count(4) + Type(1) + ImmediateCount(4) + VarSlotCount(4) + MaxRegister(1)）+ 指令区（Count × InstructionSize 字节，每条写 Raw 字段）+ 变量槽区（每个槽：nameLen + UTF8 name + slotIndex）。格式定义集中由 `FormulaFormat` 管理，字节级读写由 `BinaryFormat` 统一处理。

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
