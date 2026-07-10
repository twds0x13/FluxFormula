# BlobEntry

Blob offset table entry: maps a formula's `DualHash64` to its offset and length within a blob binary data segment. Core-layer fundamental type, shared by the source generator, `IFluxBlobRegistry`, and `FluxBlob`.

## Signature

```csharp
public readonly struct BlobEntry : IEquatable<BlobEntry>
```

## Fields

| Field | Type | Description |
|-------|------|-------------|
| `Hash` | `DualHash64` | DualHash64 identifier of the formula bytecode |
| `Offset` | `int` | Starting offset within the blob data segment (relative to the data segment, not the file) |
| `Length` | `int` | Bytecode length in bytes |

## Constructor

```csharp
public BlobEntry(DualHash64 hash, int offset, int length)
```

## Methods

### Equals

```csharp
public bool Equals(BlobEntry other)
```

Equality by Hash + Offset + Length triple.

### GetHashCode / ToString

```csharp
public override int GetHashCode()
public override string ToString()
```

Standard overrides. `ToString()` outputs `{Hash} @{Offset}+{Length}` format.

## Binary Layout

A single entry occupies 24 bytes in a .blob file:

```
XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)
```

At runtime, `BlobFormat.ReadEntryTable()` parses entries into `BlobEntry[]`. The Offset field then directly indexes the `byte[]` produced by `ExtractBlobData()`.

## Usage

```csharp
// Compile-time constants produced by the source generator
BlobEntry[] entries = BlobRegistry.GetEntries();

// Runtime loading
byte[] blobData = BlobFormat.ExtractBlobData(File.ReadAllBytes("flux.blob"));
var handle = FluxBlob.Load(blobData, entries);

// Per-entry lookup
foreach (var entry in entries)
{
    if (FormulaCache.Instance.TryGet(entry.Hash, out IntPtr ptr, out int len))
    {
        // entry.Offset is the position within blobData; len comes from entry.Length
    }
}
```

## See Also

- [BlobFormat](./blob-format) — .blob binary format definition and entry table parsing
- [DualHash64](./dualhash64) — Dual hash key
- [FluxBlob](./flux-blob) — Blob load/unload
- [IFluxBlobRegistry](./iflux-blob-registry) — Mod registry interface
