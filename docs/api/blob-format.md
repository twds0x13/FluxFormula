# BlobFormat

.blob 二进制文件格式定义与解析。Core 层静态类，零 UnityEngine 依赖。提供 header 校验、entry table 解析、data 段提取和写入原语。

## 定位

BlobFormat 是 .blob 文件的**格式单一来源**：

- **读取侧**：`TryParseHeader()` + `ReadEntryTable()` 供 source generator 在编译期解析偏移表；`ExtractBlobData()` 供运行时代理提取纯 data 段
- **写入侧**：`WriteHeader()` + `WriteEntry()` 供 `FluxBlobBuilder` 在编辑器内生成 .blob 文件

## 字节布局

```
Header (20B):
  Magic "FLXB" (4) + Version(1) + Flags(1) + Reserved(2) +
  EntryCount(4 LE) + BlobDataSize(4 LE) + BlobDataOffset(4 LE)

Entry Table (EntryCount × 24B，紧接 header):
  XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)

Blob Data (BlobDataSize bytes，起始于 BlobDataOffset):
  拼接后的公式字节码（可含 FluxCompression 压缩头部）
```

Entry 的 Offset 字段相对于 Blob Data 段起点。运行时 `ExtractBlobData()` 将 data 段拷出后，entry offset 直接索引新数组。

## 常量

| 常量 | 类型 | 值 | 说明 |
|------|------|------|------|
| `Magic` | `uint` | `0x42584C46` | 文件头 magic bytes（`'F' 'L' 'X' 'B'` 小端序） |
| `Version` | `byte` | `1` | 当前格式版本 |
| `HeaderSize` | `int` | `20` | 固定头部字节数 |
| `EntrySize` | `int` | `24` | 单条 entry 字节数（DualHash64 16B + Offset 4B + Length 4B） |
| `FlagCompressed` | `byte` | `1 << 0` | bit0：blob 数据启用 Brotli 压缩 |

## 方法

### TryParseHeader

```csharp
public static bool TryParseHeader(
    ReadOnlySpan<byte> data,
    out int entryCount,
    out int blobDataOffset,
    out int blobDataSize,
    out bool compressed)
```

解析 blob 文件 20 字节 header。返回 `true` 表示 magic 匹配且数据充足；`false` 表示 magic 不匹配或长度不足。

调用方通过 `entryCount` 确定后续需读取的 entry table 条目数，通过 `blobDataOffset` 和 `blobDataSize` 定位 data 段。

### ExtractBlobData

```csharp
public static byte[] ExtractBlobData(byte[] fileBytes)
```

从完整 .blob 文件字节中提取纯 data 段（去除 header 和 entry table）。返回的新 `byte[]` 可直接传给 `FluxBlob.Load()`。

**异常：**

| 条件 | 异常消息 |
|------|----------|
| magic 不匹配或 header 不足 | `"Invalid blob file: bad magic or truncated header."` |
| data 段越界 | `"Truncated blob file: data section (…) exceeds file size (…)."` |

### ReadEntryTable

```csharp
public static BlobEntry[] ReadEntryTable(ReadOnlySpan<byte> data, int entryCount)
```

从 header 后的 entry table 区域读取 `entryCount` 条 `BlobEntry`。从 `HeaderSize` 偏移开始，每条 24 字节，小端序解析。

### WriteHeader

```csharp
public static void WriteHeader(
    Span<byte> dest,
    int entryCount,
    int blobDataSize,
    bool compressed)
```

将 header 写入 span 前 20 字节。`blobDataOffset` 自动计算为 `HeaderSize + entryCount × EntrySize`。

### WriteEntry

```csharp
public static void WriteEntry(
    Span<byte> dest,
    ulong xxHash64,
    ulong fnvHash64,
    int entryOffset,
    int length)
```

将单条 entry 写入 span 的 24 字节。写入侧由 `FluxBlobBuilder` 调用。

## 使用示例

### 运行时加载

```csharp
// 读取 .blob 文件 → 提取 data 段 + 解析偏移表
byte[] fileBytes = File.ReadAllBytes("flux.blob");
byte[] blobData = BlobFormat.ExtractBlobData(fileBytes);

BlobEntry[] entries;
if (BlobFormat.TryParseHeader(fileBytes, out int count, out _, out _, out _))
    entries = BlobFormat.ReadEntryTable(fileBytes, count);
else
    throw new InvalidDataException("Invalid blob file");

// 加载到缓存
var handle = FluxBlob.Load(blobData, entries);
```

### Source Generator 编译期解析

```csharp
// 仅读 header + entry table，不碰 data 段
if (!BlobFormat.TryParseHeader(fileBytes, out int entryCount,
        out int dataOffset, out int dataSize, out bool compressed))
    return; // 非 blob 文件

var entries = BlobFormat.ReadEntryTable(fileBytes, entryCount);
// 生成 BlobRegistry.g.cs: BlobEntry[] 编译期常量
```

### 构建 .blob 文件

```csharp
int entryCount = formulas.Count;
int dataSize = totalBytecodeLength;
int totalSize = BlobFormat.HeaderSize + entryCount * BlobFormat.EntrySize + dataSize;
byte[] blob = new byte[totalSize];

BlobFormat.WriteHeader(blob, entryCount, dataSize, compressed: true);
for (int i = 0; i < entryCount; i++)
{
    int offset = BlobFormat.HeaderSize + entryCount * BlobFormat.EntrySize;
    BlobFormat.WriteEntry(blob.AsSpan(BlobFormat.HeaderSize + i * BlobFormat.EntrySize),
        entry.XxHash64, entry.FnvHash64, currentOffset, entry.Length);
}
```

## 参见

- [BlobEntry](./blob-entry) — 偏移表条目结构体
- [FluxBlob](./flux-blob) — 运行时 blob 加载/卸载
- [FluxCompression](./flux-config) — Brotli 压缩原语（`FlagCompressed` 对应）
- [VffFormat](./vff-format) — VFF 格式定义（同级格式类型）
