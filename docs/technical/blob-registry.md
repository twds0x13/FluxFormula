# Blob 注册表

其核心设计问题：如何将预编译公式字节码分发给终端用户，在运行时零解析开销加载，同时支持多个 mod 独立携带各自的公式集。

答案是将偏移表解析从运行时移至编译期：`IFluxBlobRegistry` 接口定义注册表契约；`BlobRegistryGenerator`（IIncrementalGenerator）在编译期读取 `.bytes` 文件生成编译期常量 `BlobEntry[]`；`FluxBlob.Load/Unload` 可加模式支持多个 blob 独立加载和卸载。

## 架构全景

```mermaid
flowchart TD
    subgraph 构建时["构建时 (Editor)"]
        A[FluxBlobBuilder] -->|扫描 FluxAsset| B[.bytes 文件]
    end
    subgraph 编译时["编译时"]
        C[BlobRegistryGenerator] -->|读取 .bytes| D[BlobRegistry.g.cs]
        D -->|BlobEntry[] 常量| E[assembly: FluxBlobRegistryAssembly]
    end
    subgraph 运行时["运行时 (启动)"]
        F[加载 .bytes] --> G[BlobFormat.ExtractBlobData]
        G --> H[FluxBlob.Load]
        H -->|GCHandle.Pin| I[FormulaCache.Put]
    end
    subgraph 运行时["运行时 (Mod 加载)"]
        J[FluxBlobScanner] -->|反射发现 IFluxBlobRegistry| K[加载 mod .blob]
        K --> H
    end
```

## IFluxBlobRegistry 接口

Core 层（零 UnityEngine 依赖）的核心抽象：

```csharp
public interface IFluxBlobRegistry
{
    int EntryCount { get; }
    string BlobKey { get; }
    BlobEntry[] GetEntries();
}
```

三个成员：
- `EntryCount`：公式条目数。0 表示空 mod（无 blob 文件）。
- `BlobKey`：对应 `.blob` 文件的加载 key（Addressables key 或文件路径）。
- `GetEntries()`：返回编译期常量的 `BlobEntry[]`，每个条目将 `DualHash64` 映射到 blob 数据段中的 offset 和 length。

`FluxBlobRegistryAssemblyAttribute` 是一个 assembly 级 marker，不含数据，仅用于 `FluxBlobScanner` 快速筛选含注册表的程序集，避免对所有已加载程序集做完整的 `GetTypes()` 遍历。

## .blob 二进制格式

`BlobFormat`（Core 层）定义格式：

```
Offset  Size   Field
0       4      Magic: 'F' 'L' 'X' 'B'
4       1      Version: 1
5       1      Flags (bit 0 = Brotli compression)
6       2      Reserved
8       4      EntryCount (uint32 LE)
12      4      BlobDataSize (uint32 LE)
16      4      BlobDataOffset (uint32 LE) = 20 + EntryCount × 24

Entry Table (offset 20): EntryCount × 24 bytes
  Each: XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)

Blob Data (offset BlobDataOffset): BlobDataSize bytes
  拼接后的公式字节码。Entry offset 相对于此段起点。
```

`BlobEntry`（24 字节）：`DualHash64` Key(16) + Offset(4) + Length(4)。每条公式一个条目，Offset 相对于 blob data 段起点，而非文件起点。

## BlobRegistryGenerator

`IIncrementalGenerator`，位于 `fluxformula/SourceGenerator/`。编译期读取 `.bytes` 文件 header + entry table，生成 `BlobRegistry.g.cs`：

```csharp
[assembly: FluxBlobRegistryAssembly]

namespace FluxFormula.Generated
{
    internal sealed class BlobRegistry : IFluxBlobRegistry
    {
        public int EntryCount => 42;
        public string BlobKey => "flux";

        private static readonly BlobEntry[] _entries = new BlobEntry[42]
        {
            new BlobEntry(new DualHash64(0x...UL, 0x...UL), 0, 128),
            // ...
        };

        public BlobEntry[] GetEntries() => _entries;
    }
}
```

关键属性：
- `internal` 类：不同程序集各自生成各自的 `BlobRegistry`，天然隔离不冲突
- `[assembly: FluxBlobRegistryAssembly]`：供 `FluxBlobScanner` 快速筛选
- `BlobKey`：从 `.bytes` 文件名提取，作为 Addressables key
- 偏移表为编译期常量：运行时零解析开销

若未找到 `.bytes` 文件，生成空 stub（`EntryCount=0`），保证编译不中断。

## 多 Mod 架构

核心模型：游戏本体 = 第一个 mod。

```
游戏本体 (Assembly-CSharp)
  BlobRegistry.g.cs (internal, IFluxBlobRegistry)
  flux.bytes（Addressables 或 StreamingAssets）

Mod A (ModA.dll, AssetBundle)
  BlobRegistry.g.cs (internal, IFluxBlobRegistry)
  modA.blob（AssetBundle 内 TextAsset）
  FluxBlobScanner.DiscoverAll() → FluxBlob.Load()

共享状态: FormulaCache.Instance
  DualHash64 基于公式内容 → 不同 mod 的相同公式自动共享缓存条目
```

关键设计属性：
- `internal` 类在不同程序集中不冲突：每个 mod 的 `BlobRegistry` 天然隔离
- `IFluxBlobRegistry` 接口提供统一的发现契约
- `FluxBlob.Load()` 可加：不依赖 Reset/Shutdown，每次调用创建独立 `FluxBlobHandle`
- `FluxBlobHandle` 追踪每个 blob 的生命周期：GCHandle + EntryKeys + IDisposable

## FluxBlob.Load/Unload 可加模式

`FluxBlob.Load(byte[] blobData, ReadOnlySpan<BlobEntry> entries)`：

1. `GCHandle.Pin(blobData)` 钉住 blob 数据防止 GC 移动
2. 逐条 `FormulaCache.Put(hash, ptr+offset, length)` 注册字节码指针
3. 创建 `FluxBlobHandle` 记录 GCHandle 和所有 EntryKey
4. 若 blob 启用了 Brotli 压缩（Flags bit 0），先解压再 Pin

`FluxBlob.Unload(FluxBlobHandle)`：

1. 逐 `EntryKey` 调用 `FormulaCache.Remove(key)` 删除缓存
2. `GCHandle.Free()` 释放内存
3. 释放 Brotli 解压句柄

## 关键类型分布

| 层 | 类型 | 职责 |
|----|------|------|
| Core | `BlobEntry` | 偏移表条目结构体 |
| Core | `IFluxBlobRegistry` | 注册表接口 |
| Core | `BlobFormat` | .blob 二进制格式定义 |
| Core | `FluxBlobRegistryAssemblyAttribute` | Assembly marker |
| Core | `FormulaCache.Remove()` | 逐 key 删除 |
| Unity | `FluxBlob` | 静态门面：Load/Unload/Shutdown |
| Unity | `FluxBlobHandle` | 单 blob 句柄：IDisposable |
| Unity | `FluxBlobScanner` | 反射扫描 IFluxBlobRegistry |
| SG | `BlobRegistryGenerator` | IIncrementalGenerator |

## 与旧架构的对比

| 维度 | v5.1.x（旧） | v5.8+（新） |
|------|------------|----------|
| 偏移表 | C# byte[] 字面量（约 6× 代码膨胀） | 编译期常量 `BlobEntry[]`（1:1） |
| 多 blob | 不支持（Initialize 会 Shutdown） | 支持 Load/Unload 可加模式 |
| Mod 发现 | 无 | FluxBlobScanner 反射扫描 |
| Content Update | 不可行 | .bytes 文件可独立更新 |
| 运行时开销 | 启动时 Parse→BuildEntries | 编译期完成，零开销 |

## 参考

- [编译缓存管线](./compile-cache.md) — DualHash64 + FormulaCache 全链路
- [Blob 注册表指南](../guide/blob-registry.md) — 面向用户的 API 使用指南
- [架构决策记录](./architecture-decisions.md) — ADR v5.2.0 BlobRegistry Source Generator
