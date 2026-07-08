# FluxBlob

Blob formula database facade: manages pinned memory blocks for precompiled formula bytecode and offset table registration. Supports multiple coexisting blobs (base game + mods); each `Load()` returns an independent `FluxBlobHandle`.

## Signature

```csharp
public static unsafe class FluxBlob
```

## Design Highlights

| Feature | Description |
|---------|-------------|
| **Additive Load** | Multiple `Load()` calls do not interfere; does not trigger `FormulaCache.Reset()`. Unlike the legacy single-blob mode |
| **Zero-copy registration** | Blob bytecode is stored in `FormulaCache` via pinned pointers — no extra memory allocation |
| **Transparent compression** | Automatically detects `FluxCompression` headers and decompresses on demand, pinning decompressed data separately |
| **Handle tracking** | Each Load returns a `FluxBlobHandle`; unload removes entries key by key |
| **Thread safety** | Counters (`LoadedBlobCount`, etc.) are `lock`-protected; `FormulaCache` is inherently thread-safe |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `LoadedBlobCount` | `int` | Number of currently loaded blobs |
| `TotalEntryCount` | `int` | Total number of formula entries across all blobs |
| `IsInitialized` | `bool` | Whether any blob is currently loaded (equivalent to `LoadedBlobCount > 0`) |
| `TotalBlobSize` | `int` | Total size in bytes of all loaded blob data segments |
| `EntryCount` | `int` | Backward-compatibility alias for `TotalEntryCount` (deprecated) |

## Methods

### Load

```csharp
public static FluxBlobHandle Load(byte[] blobData, ReadOnlySpan<BlobEntry> entries)
```

Loads a blob data block and registers all its entries into `FormulaCache`. May be called multiple times.

| Parameter | Type | Description |
|-----------|------|-------------|
| `blobData` | `byte[]` | Concatenated formula bytecode (pure data segment, without header or entry table). Typically produced by `BlobFormat.ExtractBlobData()` |
| `entries` | `ReadOnlySpan<BlobEntry>` | Offset table from `BlobRegistry.GetEntries()` or `BlobFormat.ReadEntryTable()` |

**Load flow:**

1. `GCHandle.Alloc(blobData, Pinned)` — pins the entire data block, yielding a stable `byte*` pointer
2. Iterates entries, checking bounds; compressed entries (`FluxCompression.IsCompressed`) are decompressed and pinned separately
3. `FormulaCache.Put(hash, ptr, length)` — each formula's bytecode pointer is registered in the cache
4. Returns a `FluxBlobHandle` recording all pinned handles and entry keys

**Exceptions:**

| Condition | Exception |
|-----------|-----------|
| `blobData` is null | `ArgumentNullException` |
| Entry offset/length out of bounds | `ArgumentException` (includes slot index, offset, length, and blobSize) |

Empty data or empty entries returns `FluxBlobHandle.Empty` (`IsLoaded = false`) without throwing.

### Unload

```csharp
public static void Unload(FluxBlobHandle handle)
```

Unloads all entries for the specified blob handle. Flow:

1. Removes handle from `_loadedBlobs`
2. Iterates `handle.EntryKeys` → `FormulaCache.Remove(key)` per entry
3. Frees decompressed pinned arrays (`GCHandle.Free`)
4. Frees the main blob pinned handle
5. Updates `TotalEntryCount` / `TotalBlobSize`

Passing `null` or a handle with `IsLoaded == false` is a no-op.

### Shutdown

```csharp
public static void Shutdown()
```

Unloads all currently loaded blobs and releases all resources. Equivalent to calling `Unload()` on every handle.

### VerifyIntegrity

```csharp
public static bool VerifyIntegrity(DualHash64 expectedHash)
```

Verifies the integrity of a cached formula by computing its `DualHash64` from the cached bytecode and comparing with the expected hash. Returns `false` on cache miss or hash mismatch.

### Initialize (deprecated)

```csharp
public static void Initialize(byte[] blob, ReadOnlySpan<BlobEntry> entries)
```

Backward-compatibility method: internally calls `Shutdown()` then `Load()`. New code should use `Load()` + `FluxBlobHandle`.

## FluxBlobHandle

Handle for a single blob load: holds pinned memory, decompression temporary arrays, and entry tracking. Obtained via `FluxBlob.Load()`; released via `FluxBlob.Unload()` or `Dispose()`.

### Signature

```csharp
public unsafe sealed class FluxBlobHandle : IDisposable
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `EntryCount` | `int` | Number of formula entries in this blob |
| `IsLoaded` | `bool` | Whether this blob is still in the loaded state |

### Dispose

```csharp
public void Dispose()
```

Releases this blob and all its registered entries. Equivalent to `FluxBlob.Unload(this)`.

## Usage

### Basic Loading

```csharp
// Load the base game blob from StreamingAssets
string path = Path.Combine(Application.streamingAssetsPath, "flux.blob");
byte[] fileBytes = File.ReadAllBytes(path);
byte[] blobData = BlobFormat.ExtractBlobData(fileBytes);

BlobEntry[] entries;
if (BlobFormat.TryParseHeader(fileBytes, out int count, out _, out _, out _))
    entries = BlobFormat.ReadEntryTable(fileBytes, count);

var handle = FluxBlob.Load(blobData, entries);
Debug.Log($"Loaded {handle.EntryCount} formulas");
```

### Mod Load and Unload

```csharp
// Discover all mods with FluxBlobScanner
var registries = FluxBlobScanner.DiscoverAll();
var handles = new List<FluxBlobHandle>();

foreach (var r in registries)
{
    byte[] blobData = FluxBlobScanner.LoadBlobFromFile(r.BlobKey);
    if (blobData != null)
        handles.Add(FluxBlob.Load(blobData, r.GetEntries()));
}

// On mod unload
foreach (var h in handles)
    FluxBlob.Unload(h);  // or h.Dispose()
```

### Integrity Check

```csharp
bool ok = FluxBlob.VerifyIntegrity(entry.Hash);
if (!ok)
    Debug.LogError($"Formula {entry.Hash} is corrupted in cache");
```

### Diagnostics

```csharp
Debug.Log($"Blobs: {FluxBlob.LoadedBlobCount}, Entries: {FluxBlob.TotalEntryCount}");
Debug.Log($"Total data: {FluxBlob.TotalBlobSize} bytes");
```

## See Also

- [BlobFormat](./blob-format) — .blob binary format
- [BlobEntry](./blob-entry) — Offset table entry
- [IFluxBlobRegistry](./iflux-blob-registry) — Mod registry interface and scanner
- [FormulaCache](./formula-cache) — Underlying cache implementation
- [FluxConfig](./flux-config) — `CompressBlob` / `BlobFilePath` configuration
