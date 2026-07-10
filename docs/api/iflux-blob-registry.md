# IFluxBlobRegistry

Mod 公式 blob 注册表接口：由 source generator 为每个含公式的程序集自动生成实现，供运行时扫描和加载。Core 层接口，零 UnityEngine 依赖。

## 定位

在 FluxFormula 的**多 mod 架构**中，游戏本体被视为第一个 mod。每个 mod 程序集中的 `BlobRegistry.g.cs`（SG 生成）实现此接口，`FluxBlobScanner` 在运行时通过反射发现所有实现并加载对应 blob 数据。

不同程序集的 `internal class BlobRegistry` 互不冲突。

## 签名

```csharp
public interface IFluxBlobRegistry
```

## 成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `EntryCount` | `int` | 注册表中的公式条目数。0 表示空 mod（无公式） |
| `BlobKey` | `string` | 对应 blob 二进制文件的加载 key。优先作为 Addressables key 使用；Addressables 不可用时作为文件路径回退 |
| `GetEntries()` | `BlobEntry[]` | 获取编译期固化的偏移表条目（DualHash64 → offset, length） |

## SG 生成示例

Source generator 产出如下代码（位于 mod 程序集中）：

```csharp
[assembly: FluxFormula.Core.FluxBlobRegistryAssembly]

internal sealed class BlobRegistry : IFluxBlobRegistry
{
    public int EntryCount => 42;
    public string BlobKey => "mod_formulas";

    public BlobEntry[] GetEntries() => new BlobEntry[]
    {
        new(new DualHash64(0x..., 0x...), offset: 0,   length: 156),
        new(new DualHash64(0x..., 0x...), offset: 156, length: 208),
        // ... EntryCount 条
    };
}
```

## FluxBlobRegistryAssemblyAttribute

`FluxBlobRegistryAssemblyAttribute` 是程序集级标记 attribute，由 SG 与 `BlobRegistry` 一同生成。`FluxBlobScanner` 通过此 attribute 快速筛选需扫描的程序集，避免对所有已加载程序集做完整 `GetTypes()` 遍历。

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class FluxBlobRegistryAssemblyAttribute : Attribute
```

## FluxBlobScanner

`FluxBlobScanner` 是 Unity 层的静态扫描器（位于 `FluxFormula` 程序集，不依赖 Addressables）。职责仅限于发现，加载由调用方负责。

### DiscoverAll

```csharp
public static List<IFluxBlobRegistry> DiscoverAll()
```

扫描所有已加载程序集中实现 `IFluxBlobRegistry` 的类型。可多次调用（增量扫描，已扫描的程序集自动跳过）。返回本次新发现的 registry 列表。

```csharp
// 典型用法：发现 → 加载
var registries = FluxBlobScanner.DiscoverAll();
foreach (var r in registries)
{
    byte[] blobData = ResolveBlobData(r.BlobKey);
    FluxBlob.Load(blobData, r.GetEntries());
}
```

跳过逻辑：
- `EntryCount == 0` 的 registry 被自动忽略
- `ReflectionTypeLoadException` 被 catch 并记录 warning，不阻断整体扫描
- 需程序集同时满足 `[FluxBlobRegistryAssembly]` 标记和 `IFluxBlobRegistry` 实现两个条件

### ResetScanCache

```csharp
public static void ResetScanCache()
```

重置扫描缓存，强制下次 `DiscoverAll()` 重新扫描全部程序集。

### LoadBlobFromFile

```csharp
public static byte[] LoadBlobFromFile(string key)
```

从文件系统路径加载 blob 数据。若不使用 Addressables，可将此作为 `BlobKey` 的解析方式。

- 先尝试 `key` 作为直接文件路径
- 失败则尝试 `Application.streamingAssetsPath/key`
- 成功时自动调用 `BlobFormat.ExtractBlobData()` 提取 data 段
- 全部失败返回 `null`

### ScannedAssemblyCount

```csharp
public static int ScannedAssemblyCount { get; }
```

已扫描的程序集数量（诊断用）。

## 多 Mod 数据流

```
编译时:
  FluxBlobBuilder.Build() → .blob 二进制文件
  BlobRegistryGenerator (SG) → BlobRegistry.g.cs（含偏移表编译期常量）

运行时:
  FluxBlobScanner.DiscoverAll() → 反射发现 IFluxBlobRegistry 实现
  → BlobFormat.ExtractBlobData() → 纯 data 段
  → FluxBlob.Load(blobData, registry.GetEntries()) → FluxBlobHandle
  → FormulaCache.Put() ×N

卸载:
  FluxBlob.Unload(handle) → FormulaCache.Remove() ×N → GCHandle.Free()
```

## 参见

- [FluxBlob](./flux-blob) — blob 加载/卸载 API
- [BlobFormat](./blob-format) — .blob 二进制格式
- [BlobEntry](./blob-entry) — 偏移表条目结构体
- [Blob 注册表 (指南)](/guide/blob-registry) — 使用指南与多 mod 架构详解
