# FluxFormula / FluxModifier

不可变字节码容器。`FluxFormula<TData, TDef>` 是完整公式（可独立求值），`FluxModifier<TData, TDef>` 是缺少第一操作数的半成品（只能被串联或转为 Formula）。

## 签名

```csharp
// 完整公式，可独立求值
public readonly struct FluxFormula<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>

// 修饰符，缺少第一操作数，不可独立求值
public readonly struct FluxModifier<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
```

## FluxFormula 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 指令数量（含末尾 Return） |
| `ImmediateCount` | `int` | Immediate 指令数量，即 `SetIndex()` 的有效索引上限 |
| `VariableSlots` | `VariableSlot[]` | 变量名到槽位索引的映射表，由 Lexer 路径填充 |
| `MaxRegister` | `byte` | 编译期分析的最高寄存器索引（0=未分析，回退到全量 255） |

> `Type` 字段为 `internal`。类型身份由 struct 类型本身保证：`FluxFormula` 始终是 Formula，`FluxModifier` 始终是 Modifier。

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxFormula<TData, TDef>` | 空公式（Count=0），用于 Connect 边界场景 |

## FluxModifier 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 指令数量 |
| `ImmediateCount` | `int` | Immediate 指令数量 |
| `VariableSlots` | `VariableSlot[]` | 变量槽映射 |
| `MaxRegister` | `byte` | 最高寄存器索引 |
| `IsChained` | `bool` | 是否为链式 |
| `ChainLength` | `int` | 链式链接数 |

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxModifier<TData, TDef>` | 空 Modifier，Connect 的单位元 |

## 结构体

### ChainLink

链式公式的一个环节。存储该公式片段的字节码引用和元数据，通过 `DualHash64.Key` 从缓存中检索 JIT delegate。公开结构体，可通过 `GetChainLinks()` 访问。

| 字段 | 类型 | 说明 |
|------|------|------|
| `Key` | `DualHash64` | 字节码哈希，缓存中查找 delegate 的键 |
| `Bytecode` | `Instruction[]` | 字节码引用（指向原始公式的 Instruction[]，不复制） |
| `InstructionCount` | `int` | Instruction 数量 |
| `ImmediateCount` | `int` | 该片段的 Immediate 数（用于 SetIndex 偏移计算） |
| `VarSlots` | `VariableSlot[]` | 该片段的变量槽 |
| `MaxRegister` | `byte` | 该片段的最大寄存器索引（0=未分析） |

> `Type` 字段为 `internal`。

高级用户通过 `GetChainLinks()` 获取链结构，配合 `VffFormat.ToBytes()` 将链式引用持久化为 VFF 文件。

## 构造

构造函数为 `internal`。用户通过 `FluxAssembler.Compile()` 生成，或使用 `Empty` 获取空实例。

## FluxFormula 方法

### Connect

```csharp
public FluxFormula<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

链式组合当前公式与一个 Modifier。不合并字节码，追加 `ChainLink` 引用切片。物理拼接推迟到求值时刻。

- 卫语句：任一方为空则直接返回另一方
- **`next` 的类型 `FluxModifier` 由类型系统保证**：传入 Formula 前须先调用 `.ToModifier()` 剥离首操作数
- 参见 [ChainLink 深度解析](../technical/chainlink-deep-dive)

### ToModifier

```csharp
public FluxModifier<TData, TDef> ToModifier()
```

将 Formula 转为 Modifier。移除第一 Immediate 指令及数据槽位，将其 dest 寄存器重命名为 R1（Bus）。已为 Modifier 则包装返回。链式公式先 `ToAtomic` 再转换。

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

将 Modifier 转为 Formula。插入以 `varName` 命名的 Immediate 指令替代 R1 输入，R1 引用重命名为新寄存器。已为 Formula 则返回自身。

### ToAtomic

```csharp
internal FluxFormula<TData, TDef> ToAtomic()
```

将链式公式合并为原子公式。所有 link 的 `Instruction[]` 完整拼接（含中间 Return）。JIT 路径和长链（>8）解释器路径自动调用。

### GetByteHash / Raw / ToBytes / FromBytes / IsChained / ChainLength / GetChainLinks

各方法签名见上方代码块，行为细节见源码 XML doc 注释。

### ToString

```csharp
public override string ToString()
// "FluxFormula<Single, MathDef> [Type: Formula, Instructions: 4]"
```

## FluxModifier 方法

### Connect

```csharp
public FluxModifier<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

将两个 Modifier 串联。结果仍为 Modifier（仍然缺少第一操作数）。

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Modifier→Formula：插入命名变量替代 R1 输入。这是 `FluxModifier` 转为可求值 `FluxFormula` 的唯一途径。

### Raw / ToBytes / GetByteHash / GetChainLinks / FromBytes

与 `FluxFormula` 对应方法一致。

## 参见

- [FluxAssembler](./flux-assembler) — 编译入口，产出 FluxFormula
- [FluxInstance](./flux-instance) — 公式实例化后的流式执行器
- [Instruction](./instruction) — 8 字节指令结构体
- [FormulaFormat](./formula-format) — 字节码序列化格式定义
