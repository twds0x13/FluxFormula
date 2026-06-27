# FluxFormula / FluxModifier / FluxChain

不可变字节码容器。`FluxFormula<TData, TDef>` 是完整公式（可独立求值），`FluxModifier<TData, TDef>` 是缺少第一操作数的半成品（只能被串联或转为 Formula），`FluxChain<TData, TDef>` 是多次 Connect 串联而成的多段字节码序列。

## 签名

```csharp
// 完整公式，可独立求值
public readonly struct FluxFormula<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>

// 修饰符，缺少第一操作数，不可独立求值
public readonly struct FluxModifier<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>

// 链式公式，不可直接求值
public readonly struct FluxChain<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## FluxFormula 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 指令数量（含末尾 Return） |
| `ImmediateCount` | `int` | Immediate 指令数量，即 `SetIndex()` 的有效索引上限 |
| `VariableSlots` | `VariableSlot[]` | 变量名到槽位索引的映射表，由 Lexer 路径填充 |
| `MaxRegister` | `byte` | 编译期分析的最高寄存器索引（0=未分析，回退到全量 255） |

> `Type` 字段为 `internal`。类型身份由 struct 类型本身保证：`FluxFormula` 始终是原子公式。

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxFormula<TData, TDef>` | 空公式（Count=0），Connect 的单位元 |
| `FromBytes(byte[])` | `FluxFormula<TData, TDef>` | 从字节码反序列化 |

## FluxModifier 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 指令数量 |
| `ImmediateCount` | `int` | Immediate 指令数量 |
| `VariableSlots` | `VariableSlot[]` | 变量槽映射 |
| `MaxRegister` | `byte` | 最高寄存器索引 |

> `FluxModifier` 没有 `Instantiate()` 方法：任何尝试独立求值 Modifier 的代码编译不过。链相关属性（`IsChained`/`ChainLength`/`GetChainLinks()`）已移至 `FluxChain`。

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Empty` | `FluxModifier<TData, TDef>` | 空 Modifier，Connect 的单位元 |

## 构造

构造函数均为 `internal`。用户通过 `FluxAssembler.Compile()` 生成，`Connect()` 返回 `FluxChain`，或使用 `Empty` 获取空实例。

## FluxFormula 方法

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

链式组合当前公式与一个 Modifier。不合并字节码，返回 `FluxChain`。物理拼接推迟到求值时刻。

- 卫语句：`next` 为空时返回单 link 的 `FluxChain`
- **`next` 的类型 `FluxModifier` 由类型系统保证**：传入 Formula 前须先调用 `.ToModifier()` 剥离首操作数
- 参见 [ChainLink 深度解析](../technical/chainlink-deep-dive)

### ToModifier

```csharp
public FluxModifier<TData, TDef> ToModifier()
```

将 Formula 转为 Modifier。移除第一 Immediate 指令及数据槽位，将其 dest 寄存器重命名为 R1（Bus）。

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

将 Modifier 转为 Formula。插入以 `varName` 命名的 Immediate 指令替代 R1 输入，R1 引用重命名为新寄存器。

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

计算字节码的 `DualHash64` 哈希。原子公式始终序列化后哈希。

### Raw / ToBytes / ToString

```csharp
public ReadOnlySpan<Instruction> Raw()              // O(1)，永不分配
public byte[] ToBytes()                             // 序列化为字节数组
public override string ToString()
```

## FluxModifier 方法

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

将两个 Modifier 串联，返回 `FluxChain`。结果仍缺第一操作数，需 `.ToAtomic()` 或连接至 `FluxFormula` 后才能求值。

### ToFormula

```csharp
public FluxFormula<TData, TDef> ToFormula(string varName)
```

Modifier→Formula：插入命名变量替代 R1 输入。这是 `FluxModifier` 转为可求值 `FluxFormula` 的唯一途径。

## FluxChain 方法

详见 [FluxChain API 文档](./flux-chain)。

| 方法 | 返回类型 | 说明 |
|------|------|------|
| `Connect(FluxModifier)` | `FluxChain` | 在链末尾追加 Modifier |
| `ToAtomic()` | `FluxFormula` | 显式合并所有 link 为原子公式 |
| `GetLinks()` | `ReadOnlySpan<ChainLink>` | 获取链式链接的只读视图 |
| `GetByteHash()` | `DualHash64` | 链式字节码组合哈希 |

| 属性 | 类型 | 说明 |
|------|------|------|
| `Length` | `int` | 链中的链接数 |
| `Empty` | `FluxChain`（静态） | 空链（Length=0），Connect 的单位元 |

## 结构体

### ChainLink

链式公式的一个环节。存储该公式片段的字节码引用和元数据，通过 `DualHash64.Key` 从缓存中检索 JIT delegate。通过 `FluxChain.GetLinks()` 访问。

| 字段 | 类型 | 说明 |
|------|------|------|
| `Key` | `DualHash64` | 字节码哈希，缓存中查找 delegate 的键 |
| `Bytecode` | `Instruction[]` | 字节码引用（指向原始公式的 Instruction[]，不复制） |
| `InstructionCount` | `int` | Instruction 数量 |
| `ImmediateCount` | `int` | 该片段的 Immediate 数（用于 SetIndex 偏移计算） |
| `VarSlots` | `VariableSlot[]` | 该片段的变量槽 |
| `MaxRegister` | `byte` | 该片段的最大寄存器索引（0=未分析） |

> `Type` 字段为 `internal`。

## 参见

- [FluxChain API](./flux-chain) — 链式公式专用 API
- [FluxAssembler](./flux-assembler) — 编译入口，产出 FluxFormula
- [FluxInstance](./flux-instance) — 公式实例化后的流式执行器
- [Instruction](./instruction) — 8 字节指令结构体
- [ChainLink 深度解析](../technical/chainlink-deep-dive) — 链式求值原理
