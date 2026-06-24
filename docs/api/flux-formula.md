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
| `MaxRegister` | `byte` | 编译期分析的最高寄存器索引（0=未分析，回退到全量 255） |

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxFormula<TData, TOper>` | 空公式（Count=0），用于 Connect 边界场景 |

## 结构体

### ChainLink

链式公式的一个环节。存储该公式片段的字节码引用和元数据，通过 `DualHash64.Key` 从缓存中检索 JIT delegate。2.0 起公开。

| 字段 | 类型 | 说明 |
|------|------|------|
| `Key` | `DualHash64` | 字节码哈希——在缓存中查找 delegate 的键 |
| `Bytecode` | `Instruction[]` | 字节码引用（指向原始公式的 Instruction[]，不复制） |
| `InstructionCount` | `int` | Instruction 数量 |
| `Type` | `FluxType` | `Formula` 或 `Modifier` |
| `ImmediateCount` | `int` | 该片段的 Immediate 数（用于 SetIndex 偏移计算） |
| `VarSlots` | `VariableSlot[]` | 该片段的变量槽 |
| `MaxRegister` | `byte` | 该片段的最大寄存器索引（0=未分析） |

高级用户通过 `GetChainLinks()` 获取链结构，配合 `VffFormat.ToBytes()` 将链式引用持久化为 VFF 文件。

## 构造

构造函数为 `internal`。用户通过 `FluxAssembler.Compile()` 生成，或使用 `FluxFormula<TData, TOper>.Empty` 获取空实例。

## 方法

### Connect

```csharp
public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
```

链式组合当前公式与一个 Modifier。不合并字节码——追加 `ChainLink` 引用切片。物理拼接推迟到求值时刻。

- 卫语句：任一方为空则直接返回另一方
- **`next` 必须是 Modifier**（`next.Type == FluxType.Modifier`），否则抛出 `ArgumentException`。传入 Formula 前请先调用 `.ToMultiplier()` 剥离首操作数
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

### Raw

```csharp
public ReadOnlySpan<Instruction> Raw()
```

返回公式底层指令的只读视图。链式公式自动调用 `ToAtomic()` 合并后返回，对外表现为统一的原子表示。

### ToBytes

```csharp
public byte[] ToBytes()
```

将公式序列化为字节数组。链式公式自动合并为原子公式后序列化。格式：14 字节头（Count(4) + Type(1) + ImmediateCount(4) + VarSlotCount(4) + MaxRegister(1)）+ 指令区（Count × InstructionSize 字节，每条写 Raw 字段）+ 变量槽区（每个槽：nameLen + UTF8 name + slotIndex）。格式定义集中由 `FormulaFormat` 管理，字节级读写由 `BinaryFormat` 统一处理。

### FromBytes

```csharp
public static FluxFormula<TData, TOper> FromBytes(byte[] data)
public static FluxFormula<TData, TOper> FromBytes(ReadOnlySpan<byte> data)
```

从 `ToBytes()` 产出的字节数组反序列化。无需重新编译，字节码直接可用。`ReadOnlySpan<byte>` 重载允许从 pinned 内存指针零拷贝反序列化。

```csharp
// 持久化
byte[] raw = formula.ToBytes();
File.WriteAllBytes("damage.ff", raw);

// 加载（零编译）
var loaded = FluxFormula<float, FloatOp>.FromBytes(raw);
float r = runner.Instantiate(loaded).Set("atk", 100f).Run();

// 从 blob 指针零拷贝加载
var fromBlob = FluxFormula<float, FloatOp>.FromBytes(blobSpan.Slice(offset, length));
```

`FromBytes` 在类型初始化阶段校验 `sizeof(TOper) == 1`，不满足则抛出 `TypeInitializationException`。

### IsChained

```csharp
public bool IsChained { get; }
```

公式是否为链式公式（由 `Connect()` 产生）。链式公式内部分多个 `ChainLink` 存储，原子公式则为 `false`。

### ChainLength

```csharp
public int ChainLength { get; }
```

链式公式的链接数。原子公式返回 `0`。

### GetChainLinks

```csharp
public ReadOnlySpan<ChainLink> GetChainLinks()
```

获取链式链接的只读视图。原子公式返回空 span。高级用户可通过此接口读取链结构，配合 `VffFormat.ToBytes()` 将链式公式持久化为 `.vff` 文件。

```csharp
var chain = formulaA.Connect(formulaB);
if (chain.IsChained)
{
    var links = chain.GetChainLinks();
    byte[] vffData = VffFormat.ToBytes<float>(links.ToArray(), Array.Empty<VffOverride<float>>());
    builder.Save(vffData, FluxArtifactKind.Virtual, "ComboChain.vff");
}
```

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single> [Type: Formula, Instructions: 4]"
```

## 参见

- [FluxAssembler](./flux-assembler) — 编译入口，产出 FluxFormula
- [FluxInstance](./flux-instance) — 公式实例化后的流式执行器
- [Instruction](./instruction) — 8 字节指令结构体
- [FormulaFormat](./formula-format) — 字节码序列化格式定义
