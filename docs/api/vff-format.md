# VffFormat

VFF (Virtual FluxFormula) 字节格式定义与解析器。VFF 不是独立资产类型——它是 blob 中的一种条目，通过 `"VFF\0"` magic 与公式条目区分，两者共用 `FluxBlob.Entry` 偏移表。

## 定位

VFF 的核心用途是**持久化公式组合引用**：在离线构建阶段将多条公式的引用及参数覆写信息打包为一个 VFF 条目写入 blob，运行时通过 `Resolve()` 一键展开为可执行的链式公式，无需重新编译。

## 字节布局

```
Header (8B):     Magic(4 "VFF\0") + Version(1) + LinkCount(1) + OverrideCount(1) + Flags(1)
LinkTable:       LinkCount × 22B — Hash(16) + ImmCount(1) + InstCount(2) + Type(1) + VarSlotCount(2)
OverrideTable:   OverrideCount × variable — GlobalSlot(2) + Kind(1) + [DataLen(1) + Data(var)]
```

变量名不存储在 VFF 内——resolve 时通过 `FormulaFormat.ReadVariableSlots()` 从被引用公式的字节码中直接读取。

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

### VffResolveResult\<TData, TOper\>

VFF 解析结果。

| 字段 | 类型 | 说明 |
|------|------|------|
| `Formula` | `FluxFormula<TData, TOper>` | 解析产出的链式公式（可传入 `Instantiate()`） |
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
public static VffResolveResult<TData, TOper> Resolve<TData, TOper>(
    DualHash64 vffHash)
    where TData : unmanaged
    where TOper : unmanaged, Enum
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

## 使用示例

```csharp
// 假设 blob 中已有以下条目：
//   hash_a: 公式 "[atk] * 2"     (变量 atk, slot 0)
//   hash_b: 公式 "[def] + 10"    (变量 def, slot 0)
//   hash_v: VFF 引用 hash_a + hash_b，且覆盖 slot 0 为固定值 100

var result = VffFormat.Resolve<float, FloatOp>(hash_v);

// result.Formula 为链式公式，含 2 个 ChainLink
// result.Formula.ChainLength == 2
// result.Formula.ImmediateCount == 2  (两个 link 各 1 imm)

// result.Overrides 包含 VFF 中定义的参数覆写
// 可在 Instantiate 时将 overrides 传给 FluxInstance

var instance = assembler.Instantiate(result.Formula, jit: true);
// 应用 overrides...
float value = instance.Run();
```

## 内部细节

`ResolveLinks<TData, TOper>(vffBytes, visited)` 是核心递归方法，返回 `(ChainLink[], VffOverride<TData>[], totalImm)`。

- **嵌套 VFF 展平**：递归展开子 VFF 的 links，其 SlotIndex 和 GlobalSlot 按当前累积的 `cumImm` 偏移，确保合并管道中的序号连续
- **变量槽合并**：所有 link 的 `VariableSlot[]` 按顺序拼接，SlotIndex 已由偏移修正
- **Override 合并**：当前 VFF 自身的 overrides 在前，递归展平的在后，`GlobalSlot` 均已偏移

## 参见

- [FluxFormula](./flux-formula) — 链式公式与 Connect 机制
- [FormulaCache](./formula-cache) — 字节码缓存（VFF resolve 的数据源）
- [FormulaFormat](./formula-format) — 公式字节码格式（`ReadVariableSlots`、`ReadHeader`）
