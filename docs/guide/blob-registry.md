# Blob 注册表

如果你已经会用 `runner.Compile(lexer.Lex(formulaString))` 了，公式跑得很快，那你为什么还需要把公式预编译成 `.bytes` 文件、走一整套 Blob 管线？答案是规模。你的游戏有 500 条公式时，每个玩家每次启动都要 Lex + Compile 全部 500 条，加载时间会增加几百毫秒。Blob 管线的思路是把这些公式在打包时预编译，运行时走 pinned 内存零拷贝加载：500 条公式的加载时间从编译开销变成纯 I/O。

预编译公式字节码的构建、分发与运行时加载管线。公式在 Editor 中编译为二进制 `.bytes` 文件，由 Source Generator 生成偏移表常量，运行时通过 `/Mods/` bundle 或 Addressables 加载。

## 概念

### Blob 是什么

**Blob**（Binary Large Object）是项目中所有预编译公式的单一二进制归档。每条公式的字节码按哈希排序后拼接为一个连续数据段，头部携带文件元信息，偏移表记录每条公式的位置。

将公式预编译为 blob 的好处：
- 跳过运行时的 Lex → Compile → JIT 全管线
- 字节码直接来自 pinned 内存，零拷贝存入 `FormulaCache`
- 游戏本体和 mod 可以各自携带独立 blob

> **为什么哈希在这里不能出错**：Blob 管线的运行时路径是从 pinned 内存直接索引字节码然后执行，没有校验步骤。如果两条不同公式发生了哈希碰撞，运行时会把公式 A 的字节码当成公式 B 执行，产生一个几乎不可能调试的隐蔽逻辑错误。DualHash64（XxHash64 + FNV-1a 联立）的设计保证联立碰撞在计算上不可行，因此每条公式通过内容寻址保证正确性。详见 [编译缓存管线](../technical/compile-cache)。

### BlobKey

每个 blob 有一个 `BlobKey`：Source Generator 从 `.bytes` 文件名自动推导：`flux.bytes` → `BlobKey = "flux"`。

运行时 `BlobKey` 用于定位 `.bytes` TextAsset：在 `/Mods/` bundle 中作为资产名查找，或作为 Addressables 加载 key。

### 文件格式

`.bytes` 扩展名确保 Unity 原生导入为 `TextAsset`（UTF-8 文本资产），实际内容是二进制数据。

```
Header (20B):
  Magic "FLXB" (4) + Version(1) + Flags(1) + Reserved(2) +
  EntryCount(4 LE) + BlobDataSize(4 LE) + BlobDataOffset(4 LE)

Entry Table (EntryCount × 24B, 紧接 header):
  XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)

Blob Data (BlobDataSize bytes, 起始于 BlobDataOffset):
  拼接后的公式字节码（可选 Brotli 压缩）
```

Flags 位定义：
- bit 0 = 压缩启用（Brotli）

每条 Entry 24 字节，`Offset` 字段相对于 Blob Data 段起始（即 `BlobDataOffset` 在文件中的位置）。

## 构建流程

```
FluxAsset (.ff / .vff)
  │
  ▼
FluxBlobBuilder (Editor)
  │ 扫描项目中所有 FluxAsset
  │ DualHash64.Compute() → 偏移表
  │ 可选 Brotli 压缩
  │ 按哈希排序 → 拼接 → 写入 .bytes
  ▼
Assets/StreamingAssets/flux.bytes
  │
  ▼
BlobRegistryGenerator (Source Generator)
  │ 读取 .bytes header + entry table
  │ 生成 BlobRegistry.g.cs
  │ 写入 [assembly: FluxBlobRegistryAssembly]
  ▼
BlobRegistry.g.cs (编译进 assembly)
```

### 手动构建

菜单 `FluxFormula > Build Blob` 触发构建。产物写入 `FluxConfig.Current.BlobFilePath`（默认 `Assets/StreamingAssets/flux.bytes`）。

Player Build 前通过 `IPreprocessBuildWithReport` 自动触发。

### Source Generator 产出

```csharp
[assembly: FluxFormula.Core.FluxBlobRegistryAssembly]

internal sealed class BlobRegistry : IFluxBlobRegistry
{
    public int EntryCount => 42;
    public string BlobKey => "flux";
    
    private static readonly BlobEntry[] _entries = new BlobEntry[42]
    {
        new(new DualHash64(0x..., 0x...), offset: 0,    length: 256),
        new(new DualHash64(0x..., 0x...), offset: 256,  length: 128),
        // ...
    };
    
    public BlobEntry[] GetEntries() => _entries;
}
```

SG 从 `.bytes` 文件名推导 `BlobKey`（`flux.bytes` → `"flux"`），从 header 读取 `EntryCount`，从 entry table 读取每条 offset/length。

## 运行时加载

两种加载路径，适用于不同场景：

### 路径 1：/Mods/ AssetBundle

适用于使用文件系统分发 mod 的场景（PC 端）。

```csharp
// 一次性扫描并加载 /Mods/ 目录下所有 .bundle
var handles = FluxBundleScanner.ScanAndLoad("/Mods/");

// 后续可按需卸载
FluxBlob.Unload(handles[0]);
```

内部流程：

1. `FluxBundleScanner` 扫描 `/Mods/` 目录中 `*.bundle` 文件
2. `AssetBundle.LoadFromFile` 加载每个 bundle
3. `FluxBlobScanner.DiscoverAll()` 反射扫描 bundle 内程序集
4. 从 bundle 加载对应名称的 `TextAsset`
5. `BlobFormat.ExtractBlobData` 提取 data 段
6. `FluxBlob.Load(blobData, registry.GetEntries())` 注册到 FormulaCache

```csharp
// 卸载所有 bundle
FluxBundleScanner.UnloadAll();
FluxBlob.Shutdown();
```

Bundle 搜索目录可通过 `FluxBundleScanner.BundleDirectory` 配置（默认 `"Mods"`，相对于项目根目录）。

### 路径 2：Addressables

适用于使用 Addressables 资源系统的场景（移动端热更新友好）。

```csharp
// 异步扫描并加载所有已发现 registry 对应 blob
var handles = await FluxBlobAddressablesLoader.ScanAndLoadAllAsync();

// 也可加载单个
var handle = await FluxBlobAddressablesLoader.LoadBlobAsync(
    "my_mod_blob", registry.GetEntries());
```

内部流程：

1. `FluxBlobScanner.DiscoverAll()` 反射发现所有 registry
2. 对每个 registry，`Addressables.LoadAssetAsync<TextAsset>(registry.BlobKey)` 加载
3. `BlobFormat.ExtractBlobData` 提取 data 段
4. `FluxBlob.Load` 注册到 FormulaCache
5. 重复调用增量扫描，已扫描程序集自动跳过

`.bytes` 文件通过 `FluxBlobBuildHook`（`IPostprocessBuild`）自动注册到 Addressables group。

## 多 Mod 架构

游戏本体和 mod 走同一条路径：游戏本体即为"第一个 mod"。

每个 mod 程序集中有一个 SG 生成的 `BlobRegistry : IFluxBlobRegistry` 实现。不同程序集的实现互不冲突。

```
游戏本体 assembly
  └─ BlobRegistry (BlobKey = "base_game")
       EntryCount = 500
       GetEntries() → BlobEntry[500]

Mod A assembly  
  └─ BlobRegistry (BlobKey = "mod_a_blob")
       EntryCount = 42
       GetEntries() → BlobEntry[42]

Mod B assembly
  └─ BlobRegistry (BlobKey = "mod_b_blob")  
       EntryCount = 0  // 空 mod，无预编译公式
```

运行时通过 `FluxBlobScanner` 反射扫描实现 `IFluxBlobRegistry` 的类型，自动发现已加载 mod 的注册表。`FluxBlobRegistryAssemblyAttribute` 用于快速筛选需扫描的程序集，避免对所有已加载程序集做完整 `GetTypes()` 遍历。

## API 参考

### FluxBlob

```csharp
// 加载 blob（获取独立的 FluxBlobHandle）
var handle = FluxBlob.Load(blobData, entries);

// 卸载指定 blob：从 FormulaCache 移除所有条目并释放 pinned 内存
FluxBlob.Unload(handle);

// 卸载全部已加载 blob
FluxBlob.Shutdown();

// 验证某条公式的字节码完整性
bool ok = FluxBlob.VerifyIntegrity(hash);

// 状态
bool initialized = FluxBlob.IsInitialized;
int totalCount  = FluxBlob.TotalEntryCount;
int totalSize   = FluxBlob.TotalBlobSize;
```

`Load()` 支持压缩条目：若 `FluxCompression.IsCompressed()` 返回 true，自动解压后再存入缓存。解压后的临时数组由 `FluxBlobHandle` 管理生命周期。

多次 `Load()` 创建独立的 `FluxBlobHandle`，互不干扰。

### FluxBlobHandle

```csharp
public sealed class FluxBlobHandle : IDisposable
{
    public int EntryCount { get; }       // 此 blob 中的公式条目数
    public bool IsLoaded { get; }        // 是否仍处于已加载状态
    
    public void Dispose();               // 等价于 FluxBlob.Unload(this)
}
```

### BlobEntry

```csharp
public readonly struct BlobEntry
{
    public readonly DualHash64 Hash;  // 公式字节码哈希
    public readonly int Offset;       // 在 blob data 段中的起始偏移（字节）
    public readonly int Length;       // 字节码长度（字节）
}
```

### BlobFormat

```csharp
public static class BlobFormat
{
    const uint Magic = 0x42584C46;   // "FLXB" LE
    const int HeaderSize = 20;
    const int EntrySize  = 24;
    
    // 解析
    static bool TryParseHeader(ReadOnlySpan<byte>, out int entryCount, ...);
    static byte[] ExtractBlobData(byte[] fileBytes);
    static BlobEntry[] ReadEntryTable(ReadOnlySpan<byte>, int entryCount);
    
    // 写入（供 FluxBlobBuilder 使用）
    static void WriteHeader(Span<byte>, int entryCount, int blobDataSize, bool compressed);
    static void WriteEntry(Span<byte>, ulong xxHash64, ulong fnvHash64, int offset, int length);
}
```

### IFluxBlobRegistry

```csharp
public interface IFluxBlobRegistry
{
    int EntryCount { get; }
    string BlobKey { get; }
    BlobEntry[] GetEntries();
}
```

## 压缩

`FluxConfig.Current.CompressBlob` 控制 blob 构建时是否启用 Brotli 压缩（默认 `false`）。启用后每条公式独立压缩：`FluxBlob.Load()` 在运行时自动检测并解压。

```csharp
FluxConfig.Set(new FluxConfig
{
    CompressBlob = true,
    BlobFilePath = "Assets/StreamingAssets/flux.bytes"
});
```

## 参见

- [编译缓存管线](../technical/compile-cache) — Blob → FormulaCache → Delegate 全链路
- [FormulaCache](../api/formula-cache) — 缓存实现与 Remove 方法
- [FluxConfig](../api/flux-config) — `BlobFilePath`、`CompressBlob` 配置
- [DualHash64](../api/dualhash64) — 哈希键
