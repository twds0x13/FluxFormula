# FluxBlob

Blob 公式数据库门面：管理预编译公式字节码的 pinned 内存块和偏移表注册。支持多个 blob 共存（游戏本体 + mod），每次 `Load()` 返回独立的 `FluxBlobHandle`。

## 签名

```csharp
public static unsafe class FluxBlob
```

## 设计要点

| 特性 | 说明 |
|------|------|
| **可加 Load** | 多次 `Load()` 互不干扰，不触发 `FormulaCache.Reset()`。与旧版单 blob 模式不同 |
| **零拷贝注册** | blob 字节码通过 pinned 指针直接存入 `FormulaCache`，无额外内存分配 |
| **压缩透明** | 自动检测 `FluxCompression` 头部，按需解压后独立 pin 存缓存 |
| **句柄追踪** | 每次 Load 返回 `FluxBlobHandle`，卸载时逐 key 清理 |
| **线程安全** | `LoadedBlobCount` 等计数受 `lock` 保护；`FormulaCache` 自身线程安全 |

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `LoadedBlobCount` | `int` | 当前已加载的 blob 总数 |
| `TotalEntryCount` | `int` | 所有 blob 中的公式条目总数 |
| `IsInitialized` | `bool` | 是否有任何 blob 已加载（等价于 `LoadedBlobCount > 0`） |
| `TotalBlobSize` | `int` | 所有已加载 blob 的 data 段总字节数 |
| `EntryCount` | `int` | 向后兼容别名，等价于 `TotalEntryCount`（已废弃） |

## 方法

### Load

```csharp
public static FluxBlobHandle Load(byte[] blobData, ReadOnlySpan<BlobEntry> entries)
```

加载一个 blob 数据块并将其所有条目注册到 `FormulaCache`。可多次调用。

| 参数 | 类型 | 说明 |
|------|------|------|
| `blobData` | `byte[]` | 拼接后的公式字节码（纯 data 段，不含 header 和 entry table）。通常由 `BlobFormat.ExtractBlobData()` 产出 |
| `entries` | `ReadOnlySpan<BlobEntry>` | 偏移表，来自 `BlobRegistry.GetEntries()` 或 `BlobFormat.ReadEntryTable()` |

**加载流程：**

1. `GCHandle.Alloc(blobData, Pinned)` — 固定整块 data，获得跨运行时稳定的 `byte*` 指针
2. 遍历 entries，逐条检查边界；遇压缩条目（`FluxCompression.IsCompressed`）则解压后独立 pin
3. `FormulaCache.Put(hash, ptr, length)` — 每条公式的字节码指针注册到缓存
4. 返回 `FluxBlobHandle` 记录所有 pinned 句柄和 entry key

**异常：**

| 条件 | 异常 |
|------|------|
| `blobData` 为 null | `ArgumentNullException` |
| entry offset/length 越界 | `ArgumentException`（含 slot index、offset、length 和 blobSize） |

空 data 或空 entries 返回 `FluxBlobHandle.Empty`（`IsLoaded = false`），无异常。

### Unload

```csharp
public static void Unload(FluxBlobHandle handle)
```

卸载指定 blob handle 对应的所有条目。流程：

1. 从 `_loadedBlobs` 移除 handle
2. 遍历 `handle.EntryKeys` → `FormulaCache.Remove(key)` 逐条清理
3. 释放解压后的独立 pinned 数组（`GCHandle.Free`）
4. 释放 blob 主 pinned handle
5. 更新 `TotalEntryCount` / `TotalBlobSize`

传入 `null` 或 `IsLoaded == false` 的 handle 为无操作。

### Shutdown

```csharp
public static void Shutdown()
```

卸载全部已加载的 blob，释放所有资源。等价于对每个 handle 调用 `Unload()`。

### VerifyIntegrity

```csharp
public static bool VerifyIntegrity(DualHash64 expectedHash)
```

验证指定哈希的公式在缓存中的完整性：实际计算缓存中字节码的 `DualHash64` 并与期望值比对。若未命中缓存或哈希不一致返回 `false`。

### Initialize（已废弃）

```csharp
public static void Initialize(byte[] blob, ReadOnlySpan<BlobEntry> entries)
```

向后兼容方法：内部先 `Shutdown()` 再 `Load()`。新代码应使用 `Load()` + `FluxBlobHandle`。

## FluxBlobHandle

单个 blob 加载的句柄：持有 pinned 内存、解压临时数组和条目追踪。通过 `FluxBlob.Load()` 获取，通过 `FluxBlob.Unload()` 或 `Dispose()` 释放。

### 签名

```csharp
public unsafe sealed class FluxBlobHandle : IDisposable
```

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `EntryCount` | `int` | 此 blob 中包含的公式条目数 |
| `IsLoaded` | `bool` | 此 blob 是否仍处于已加载状态 |

### Dispose

```csharp
public void Dispose()
```

释放此 blob 及其所有注册条目。等价于 `FluxBlob.Unload(this)`。

## 使用示例

### 基础加载

```csharp
// 从 StreamingAssets 加载游戏本体 blob
string path = Path.Combine(Application.streamingAssetsPath, "flux.blob");
byte[] fileBytes = File.ReadAllBytes(path);
byte[] blobData = BlobFormat.ExtractBlobData(fileBytes);

BlobEntry[] entries;
if (BlobFormat.TryParseHeader(fileBytes, out int count, out _, out _, out _))
    entries = BlobFormat.ReadEntryTable(fileBytes, count);

var handle = FluxBlob.Load(blobData, entries);
Debug.Log($"Loaded {handle.EntryCount} formulas");
```

### Mod 加载与卸载

```csharp
// 用 FluxBlobScanner 发现所有 mod
var registries = FluxBlobScanner.DiscoverAll();
var handles = new List<FluxBlobHandle>();

foreach (var r in registries)
{
    byte[] blobData = FluxBlobScanner.LoadBlobFromFile(r.BlobKey);
    if (blobData != null)
        handles.Add(FluxBlob.Load(blobData, r.GetEntries()));
}

// Mod 卸载时
foreach (var h in handles)
    FluxBlob.Unload(h);  // 或 h.Dispose()
```

### 完整性校验

```csharp
bool ok = FluxBlob.VerifyIntegrity(entry.Hash);
if (!ok)
    Debug.LogError($"Formula {entry.Hash} is corrupted in cache");
```

### 诊断

```csharp
Debug.Log($"Blobs: {FluxBlob.LoadedBlobCount}, Entries: {FluxBlob.TotalEntryCount}");
Debug.Log($"Total data: {FluxBlob.TotalBlobSize} bytes");
```

## 参见

- [BlobFormat](./blob-format) — .blob 二进制格式
- [BlobEntry](./blob-entry) — 偏移表条目
- [IFluxBlobRegistry](./iflux-blob-registry) — mod 注册表接口与扫描器
- [FormulaCache](./formula-cache) — 底层缓存实现
- [FluxConfig](./flux-config) — `CompressBlob` / `BlobFilePath` 配置
