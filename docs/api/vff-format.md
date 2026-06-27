# VffFormat

VFF (Virtual FluxFormula) 字节格式定义、编码器与解析器。VFF 条目与公式条目共存于 blob，通过 `"VFF\0"` magic 区分。

## 定位

VFF 提供两种操作方向：

- **编码**：`ToBytes()` 将链式公式引用（`ChainLink[]`）和参数覆写序列化为 VFF 字节数组。这是 VFF 的**创建侧**：在编辑器内拼出长管道后持久化为独立 `.vff` 文件，供 `FluxBlobBuilder` 嵌入 blob。
- **解码**：`Resolve()` 从 `FormulaCache` 读取 blob 中的 VFF 条目；`FromBytes()` 从裸字节数组解析。两者均递归展开为可执行的链式公式，无需重新编译。

## 字节布局

```
Header (8B):     Magic(4 "VFF\0") + Version(1) + LinkCount(1) + OverrideCount(1) + Flags(1)
LinkTable:       LinkCount × 22B — Hash(16) + ImmCount(1) + InstCount(2) + Type(1) + VarSlotCount(2)
OverrideTable:   OverrideCount × variable — GlobalSlot(2) + Kind(1) + [DataLen(1) + Data(var)]
```

变量名不存储在 VFF 内，resolve 时通过 `FormulaFormat.ReadVariableSlots()` 从被引用公式的字节码中直接读取。

## 常量

| 常量 | 类型 | 值 | 说明 |
|------|------|------|------|
| `Magic` | `byte[]` | `"VFF\0"` | VFF 条目识别 magic bytes |
| `HeaderSize` | `int` | `8` | 头部字节数 |
| `LinkEntrySize` | `int` | `22` | 单条 link entry 字节数 |
| `FlagHasConstants` | `byte` | `1 << 0` | bit0：包含硬编码常量数据 |

## 结构体

### VffHeader

8 字节头（`[StructLayout(Size = 8)]`）。

| 字段 | 类型 | 偏移 | 说明 |
|------|------|------|------|
| `Version` | `byte` | +4 | 格式版本号（当前为 1） |
| `LinkCount` | `byte` | +5 | 被引用公式数量（上限 255） |
| `OverrideCount` | `byte` | +6 | 参数覆写数量（上限 255） |
| `Flags` | `byte` | +7 | 标志位 |
| `HasConstants` | `bool` | — | `(Flags & FlagHasConstants) != 0` |

构造：`new VffHeader(version, linkCount, overrideCount, flags)`

### VffLinkEntry

22 字节公式引用（`[StructLayout(Size = 22)]`）。

| 字段 | 类型 | 偏移 | 说明 |
|------|------|------|------|
| `Hash` | `DualHash64` | +0 (16B) | 被引用公式的 DualHash64 |
| `ImmCount` | `byte` | +16 (1B) | 该 link 的 Immediate 数量 |
| `InstCount` | `ushort` | +17 (2B) | 该 link 的 Instruction 数量 |
| `Type` | `FluxType` | +19 (1B) | Formula 或 Modifier |
| `VarSlotCount` | `ushort` | +20 (2B) | 该 link 的变量槽数 |

构造：`new VffLinkEntry(hash, immCount, instCount, type, varSlotCount)`

### VffOverride\<TData\>

解析后的参数覆写元数据。

| 字段 | 类型 | 说明 |
|------|------|------|
| `GlobalSlot` | `int` | 合并管道中的 Immediate 全局序号 |
| `Kind` | `VffOverrideKind` | 覆写类型 |
| `ConstantValue` | `TData` | Kind=Constant 时的硬编码值（否则为 default） |

构造：`new VffOverride<TData>(globalSlot, kind, constantValue)`

### VffResolveResult\<TData, TDef\>

VFF 解析结果。

| 字段 | 类型 | 说明 |
|------|------|------|
| `Formula` | `FluxFormula<TData, TDef>` | 解析产出的链式公式（可传入 `Instantiate()`） |
| `Overrides` | `VffOverride<TData>[]` | 参数覆写列表（空数组 = 纯引用无覆写） |

## 枚举

### VffOverrideKind

```csharp
public enum VffOverrideKind : byte
{
    Inject  = 0,   // 求值时由调用方通过 Injector 注入
    Constant = 1,  // VFF 定义时已硬编码为固定值
}
```

## 方法

### IsVff

```csharp
public static bool IsVff(ReadOnlySpan<byte> bytes)
```

检测一段字节码是否为 VFF 条目。检查前 4 字节是否等于 `"VFF\0"`。内联为 `AggressiveInlining`。

### Resolve

```csharp
public static VffResolveResult<TData, TDef> Resolve<TData, TDef>(
    DualHash64 vffHash)
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

从 `FormulaCache` 读取 VFF 条目，递归解析为链式公式。

**解析流程：**

1. 从缓存按 `vffHash` 查找 VFF 字节码
2. 校验 magic + version
3. 遍历 LinkTable，对每条 link：
   - 若目标是普通公式 → 构建 `ChainLink`，SlotIndex 偏移 `cumImm`
   - 若目标是另一个 VFF → **递归展开**，SlotIndex 和 GlobalSlot 自动偏移
4. 解析 OverrideTable，合并当前层与递归展平的 overrides
5. 合并所有 link 的变量槽
6. 返回 `VffResolveResult`

**异常：**

| 条件 | 异常消息 |
|------|----------|
| 缓存未命中 | `"VFF entry not found in cache for hash: …"` |
| magic 不匹配 | `"Blob entry is not a VFF (magic mismatch). Hash: …"` |
| 版本不支持 | `"Unsupported VFF version: … Expected: 1."` |
| 引用公式不在缓存 | `"VFF link [i] references entry not in cache. Hash: …"` |
| 循环引用 | `"Circular VFF reference detected: link [i] references VFF …"` |
| override dataLen 不匹配 | `"VFF override [i] constant data length mismatch: expected …, got …"` |

**循环检测：** 使用 `HashSet<DualHash64>` 维护递归栈。遇到已访问的哈希时抛出 `InvalidOperationException`。递归返回时从栈中移除，允许 DAG 中不同分支共享同一子 VFF。

### ToBytes

```csharp
public static byte[] ToBytes<TData>(
    ChainLink[] links,
    VffOverride<TData>[] overrides)
    where TData : unmanaged
```

将链式公式引用序列化为 VFF 字节数组。与 `FromBytes` 配对，往返保证链路等价。

| 参数 | 类型 | 说明 |
|------|------|------|
| `links` | `ChainLink[]` | 链式链接数组（如来自 `FluxChain.GetLinks()`） |
| `overrides` | `VffOverride<TData>[]` | 参数覆写列表（无覆写传空数组） |

产出的字节布局与本文档"字节布局"节一致：Header（"VFF\0" + Version + LinkCount + OverrideCount + Flags）+ LinkTable + OverrideTable。Flags 的 `HasConstants` 位自动根据 overrides 中是否存在 `Constant` 类型计算。

### FromBytes

```csharp
public static VffResolveResult<TData, TDef> FromBytes<TData, TDef>(
    byte[] data)
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

从裸字节数组解析 VFF，产出链式公式。与 `Resolve()` 功能等价，但 VFF 字节直接来自参数而非 `FormulaCache` 查找。

被引用的公式仍通过 `FormulaCache` 解析，调用前须将依赖公式的字节码注入缓存。

| 参数 | 类型 | 说明 |
|------|------|------|
| `data` | `byte[]` | VFF 格式的字节数组（以 `"VFF\0"` magic 开头） |

**异常：**

| 条件 | 异常消息 |
|------|----------|
| magic 不匹配 | `"Data is not a VFF entry (magic mismatch)."` |
| 版本不支持 | `"Unsupported VFF version: … Expected: 1."` |
| 引用公式不在缓存 | `"VFF link [i] references entry not in cache. Hash: …"` |

## 使用示例

### 创建并持久化 VFF

```csharp
// 编译两条公式并注入缓存
var fA = assembler.Compile(lexer.Lex("[atk] * 2"));
var fB = assembler.Compile(lexer.Lex("[def] + 10"));
byte[] bytesA = fA.ToBytes(), bytesB = fB.ToBytes();
var hashA = FormulaCache.Instance.Put(bytesA);
var hashB = FormulaCache.Instance.Put(bytesB);

// 构建 ChainLink 引用
var links = new[]
{
    new ChainLink { Key = hashA, Bytecode = FormulaFormat.GetInstructionSpan(bytesA).ToArray(),
        InstructionCount = fA.Count, Type = (byte)FluxType.Formula,  // internal enum; 0=Modifier, 1=Formula
        ImmediateCount = fA.ImmediateCount, VarSlots = fA.VariableSlots,
        MaxRegister = fA.MaxRegister },
    new ChainLink { Key = hashB, Bytecode = FormulaFormat.GetInstructionSpan(bytesB).ToArray(),
        InstructionCount = fB.Count, Type = FluxType.Formula,
        ImmediateCount = fB.ImmediateCount, VarSlots = fB.VariableSlots,
        MaxRegister = fB.MaxRegister },
};

// 序列化为 VFF 字节数组
byte[] vffData = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());

// 保存为 .vff 文件（通过 IFluxFileFormatter）
builder.Save(vffData, FluxArtifactKind.Virtual, "AttackDefenseChain.vff");
```

### 从字节解析 VFF

```csharp
// 从 .vff 文件加载字节 → 解析
byte[] loaded = File.ReadAllBytes("AttackDefenseChain.vff");
var result = VffFormat.FromBytes<float, MathDef>(loaded);

// result.Chain 为 FluxChain，可直接传入 Instantiate
var instance = assembler.Instantiate(result.Chain, jit: true);
instance.Set("atk", 100f).Set("def", 50f);
float value = instance.Run();
```

## 内部细节

`ResolveLinks<TData, TDef>(vffBytes, visited)` 是 `Resolve()` 和 `FromBytes()` 共享的核心递归方法，返回 `(ChainLink[], VffOverride<TData>[], totalImm)`。

`ToBytes<TData>()` 按上述字节布局生成 VFF 字节数组，所有多字节写入通过 `BinaryFormat` 统一处理。

- **嵌套 VFF 展平**：递归展开子 VFF 的 links，其 SlotIndex 和 GlobalSlot 按当前累积的 `cumImm` 偏移，确保合并管道中的序号连续
- **变量槽合并**：所有 link 的 `VariableSlot[]` 按顺序拼接，SlotIndex 已由偏移修正
- **Override 合并**：当前 VFF 自身的 overrides 在前，递归展平的在后，`GlobalSlot` 均已偏移

## 参见

- [FluxFormula](./flux-formula) — 链式公式与 Connect 机制
- [FormulaCache](./formula-cache) — 字节码缓存（VFF resolve 的数据源）
- [FormulaFormat](./formula-format) — 公式字节码格式（`ReadVariableSlots`、`ReadHeader`）
