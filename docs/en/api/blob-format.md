# BlobFormat

.blob binary file format definition and parsing. Core-layer static class with zero UnityEngine dependency. Provides header validation, entry table parsing, data segment extraction, and write primitives.

## Role

BlobFormat is the **single source of format truth** for .blob files:

- **Read side**: `TryParseHeader()` + `ReadEntryTable()` for the source generator to parse offset tables at compile time; `ExtractBlobData()` for the runtime agent to extract the pure data segment
- **Write side**: `WriteHeader()` + `WriteEntry()` for `FluxBlobBuilder` to generate .blob files in the Editor

## Binary Layout

```
Header (20 B):
  Magic "FLXB" (4) + Version(1) + Flags(1) + Reserved(2) +
  EntryCount(4 LE) + BlobDataSize(4 LE) + BlobDataOffset(4 LE)

Entry Table (EntryCount × 24 B, immediately after header):
  XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)

Blob Data (BlobDataSize bytes, starting at BlobDataOffset):
  Concatenated formula bytecode (may contain FluxCompression headers)
```

Each entry's Offset field is relative to the start of the Blob Data segment. After `ExtractBlobData()` copies the data segment at runtime, entry offsets directly index the resulting array.

## Constants

| Constant | Type | Value | Description |
|----------|------|-------|-------------|
| `Magic` | `uint` | `0x42584C46` | File header magic bytes (`'F' 'L' 'X' 'B'` little-endian) |
| `Version` | `byte` | `1` | Current format version |
| `HeaderSize` | `int` | `20` | Fixed header size in bytes |
| `EntrySize` | `int` | `24` | Single entry size in bytes (DualHash64 16 B + Offset 4 B + Length 4 B) |
| `FlagCompressed` | `byte` | `1 << 0` | Bit 0: blob data uses Brotli compression |

## Methods

### TryParseHeader

```csharp
public static bool TryParseHeader(
    ReadOnlySpan<byte> data,
    out int entryCount,
    out int blobDataOffset,
    out int blobDataSize,
    out bool compressed)
```

Parses the 20-byte blob file header. Returns `true` if the magic matches and data is sufficient; `false` on magic mismatch or insufficient length.

The caller uses `entryCount` to determine how many entry table entries to read next, and `blobDataOffset`/`blobDataSize` to locate the data segment.

### ExtractBlobData

```csharp
public static byte[] ExtractBlobData(byte[] fileBytes)
```

Extracts the pure data segment from a complete .blob file (strips header and entry table). The returned `byte[]` can be passed directly to `FluxBlob.Load()`.

**Exceptions:**

| Condition | Exception Message |
|-----------|------------------|
| Bad magic or truncated header | `"Invalid blob file: bad magic or truncated header."` |
| Data section out of bounds | `"Truncated blob file: data section (…) exceeds file size (…)."` |

### ReadEntryTable

```csharp
public static BlobEntry[] ReadEntryTable(ReadOnlySpan<byte> data, int entryCount)
```

Reads `entryCount` entries from the entry table region following the header. Starts at offset `HeaderSize`; each entry is 24 bytes, parsed in little-endian.

### WriteHeader

```csharp
public static void WriteHeader(
    Span<byte> dest,
    int entryCount,
    int blobDataSize,
    bool compressed)
```

Writes the header into the first 20 bytes of the span. `blobDataOffset` is computed automatically as `HeaderSize + entryCount × EntrySize`.

### WriteEntry

```csharp
public static void WriteEntry(
    Span<byte> dest,
    ulong xxHash64,
    ulong fnvHash64,
    int entryOffset,
    int length)
```

Writes a single entry into 24 bytes of the span. Used by `FluxBlobBuilder` on the write side.

## Usage

### Runtime Loading

```csharp
// Read .blob file → extract data segment + parse offset table
byte[] fileBytes = File.ReadAllBytes("flux.blob");
byte[] blobData = BlobFormat.ExtractBlobData(fileBytes);

BlobEntry[] entries;
if (BlobFormat.TryParseHeader(fileBytes, out int count, out _, out _, out _))
    entries = BlobFormat.ReadEntryTable(fileBytes, count);
else
    throw new InvalidDataException("Invalid blob file");

// Load into cache
var handle = FluxBlob.Load(blobData, entries);
```

### Source Generator Compile-Time Parsing

```csharp
// Read only header + entry table — never touch the data segment
if (!BlobFormat.TryParseHeader(fileBytes, out int entryCount,
        out int dataOffset, out int dataSize, out bool compressed))
    return; // Not a blob file

var entries = BlobFormat.ReadEntryTable(fileBytes, entryCount);
// Generate BlobRegistry.g.cs: BlobEntry[] compile-time constants
```

### Building a .blob File

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

## See Also

- [BlobEntry](./blob-entry) — Offset table entry struct
- [FluxBlob](./flux-blob) — Runtime blob load/unload
- [VffFormat](./vff-format) — VFF format definition (sibling format type)
