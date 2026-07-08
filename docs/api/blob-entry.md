# BlobEntry

Blob 偏移表条目：将一条公式的 `DualHash64` 映射到其在 blob 二进制数据段中的偏移与长度。Core 层基础类型，供 source generator、`IFluxBlobRegistry` 和 `FluxBlob` 共用。

## 签名

```csharp
public readonly struct BlobEntry : IEquatable<BlobEntry>
```

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `Hash` | `DualHash64` | 公式字节码的 DualHash64 标识 |
| `Offset` | `int` | 在 blob 数据段中的起始偏移（相对于 data 段起点，非文件起点） |
| `Length` | `int` | 字节码长度（字节） |

## 构造

```csharp
public BlobEntry(DualHash64 hash, int offset, int length)
```

## 方法

### Equals

```csharp
public bool Equals(BlobEntry other)
```

按 Hash + Offset + Length 三元组判等。

## 二进制布局

单条 entry 在 .blob 文件中占 24 字节：

```
XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)
```

运行时通过 `BlobFormat.ReadEntryTable()` 解析为 `BlobEntry[]` 后，Offset 可直接索引 `ExtractBlobData()` 产出的 `byte[]`。

## 使用示例

```csharp
// source generator 产出编译期常量
BlobEntry[] entries = BlobRegistry.GetEntries();

// 运行时加载
byte[] blobData = BlobFormat.ExtractBlobData(File.ReadAllBytes("flux.blob"));
var handle = FluxBlob.Load(blobData, entries);

// 逐条查询
foreach (var entry in entries)
{
    if (FormulaCache.Instance.TryGet(entry.Hash, out IntPtr ptr, out int len))
    {
        // entry.Offset 对应 blobData 中的位置，len 由 entry.Length 确定
    }
}
```

## 参见

- [BlobFormat](./blob-format) — .blob 二进制格式定义与 entry table 解析
- [DualHash64](./dualhash64) — 双哈希键
- [FluxBlob](./flux-blob) — blob 加载/卸载
- [IFluxBlobRegistry](./iflux-blob-registry) — mod 注册表接口
