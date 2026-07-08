# Blob Registry

Pre-compiled formula bytecode build, distribution, and runtime loading pipeline. Formulas are compiled into binary `.bytes` files in the Editor. A Source Generator produces offset-table constants. At runtime, blobs load via `/Mods/` bundles or Addressables.

## Concepts

### What Is a Blob

A **Blob** (Binary Large Object) is a single binary archive containing all pre-compiled formulas in a project. Each formula's bytecode is sorted by hash and concatenated into a contiguous data segment, with file metadata in a header and an entry table recording each formula's position.

Benefits of pre-compiling formulas into a blob:
- Skips the entire Lex → Compile → JIT pipeline at runtime
- Bytecode points directly into pinned memory with zero-copy registration in `FormulaCache`
- The base game and mods each carry their own independent blob

### BlobKey

Every blob has a `BlobKey` — automatically derived by the Source Generator from the `.bytes` filename: `flux.bytes` → `BlobKey = "flux"`.

At runtime, `BlobKey` is used to locate the `.bytes` TextAsset: by asset name lookup within `/Mods/` bundles, or as an Addressables loading key.

### File Format

The `.bytes` extension ensures Unity natively imports the file as a `TextAsset` (UTF-8 text asset), while the actual content is binary.

```
Header (20B):
  Magic "FLXB" (4) + Version(1) + Flags(1) + Reserved(2) +
  EntryCount(4 LE) + BlobDataSize(4 LE) + BlobDataOffset(4 LE)

Entry Table (EntryCount × 24B, immediately after header):
  XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)

Blob Data (BlobDataSize bytes, starting at BlobDataOffset):
  Concatenated formula bytecode (optionally Brotli-compressed)
```

Flag bits:
- bit 0 = compression enabled (Brotli)

Each entry is 24 bytes. The `Offset` field is relative to the start of the Blob Data segment (i.e., file position `BlobDataOffset`).

## Build Pipeline

```
FluxAsset (.ff / .vff)
  │
  ▼
FluxBlobBuilder (Editor)
  │ Scan all FluxAssets in the project
  │ DualHash64.Compute() → offset table
  │ Optional Brotli compression
  │ Sort by hash → concatenate → write .bytes
  ▼
Assets/StreamingAssets/flux.bytes
  │
  ▼
BlobRegistryGenerator (Source Generator)
  │ Read .bytes header + entry table
  │ Generate BlobRegistry.g.cs
  │ Emit [assembly: FluxBlobRegistryAssembly]
  ▼
BlobRegistry.g.cs (compiled into assembly)
```

### Manual Build

Menu `FluxFormula > Build Blob` triggers a build. Output is written to `FluxConfig.Current.BlobFilePath` (default: `Assets/StreamingAssets/flux.bytes`).

A build is automatically triggered before Player Build via `IPreprocessBuildWithReport`.

### Source Generator Output

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

The SG derives `BlobKey` from the `.bytes` filename (`flux.bytes` → `"flux"`), reads `EntryCount` from the header, and reads each entry's offset/length from the entry table.

## Runtime Loading

Two loading paths for different scenarios:

### Path 1: /Mods/ AssetBundle

For PC-style distribution where mods are distributed as files on disk.

```csharp
// Scan and load all .bundle files in /Mods/ in one call
var handles = FluxBundleScanner.ScanAndLoad("/Mods/");

// Optionally unload individual handles later
FluxBlob.Unload(handles[0]);
```

Internal flow:

1. `FluxBundleScanner` scans `/Mods/` for `*.bundle` files
2. `AssetBundle.LoadFromFile` loads each bundle
3. `FluxBlobScanner.DiscoverAll()` reflectively scans assemblies within bundles
4. Loads the corresponding `TextAsset` by name from the bundle
5. `BlobFormat.ExtractBlobData` extracts the data segment
6. `FluxBlob.Load(blobData, registry.GetEntries())` registers with FormulaCache

```csharp
// Unload all bundles
FluxBundleScanner.UnloadAll();
FluxBlob.Shutdown();
```

The bundle search directory is configurable via `FluxBundleScanner.BundleDirectory` (default: `"Mods"`, relative to the project root).

### Path 2: Addressables

For scenarios using the Addressables asset system (hot-update friendly for mobile).

```csharp
// Async scan and load all discovered registry blobs
var handles = await FluxBlobAddressablesLoader.ScanAndLoadAllAsync();

// Or load a single one
var handle = await FluxBlobAddressablesLoader.LoadBlobAsync(
    "my_mod_blob", registry.GetEntries());
```

Internal flow:

1. `FluxBlobScanner.DiscoverAll()` reflectively discovers all registries
2. For each registry, `Addressables.LoadAssetAsync<TextAsset>(registry.BlobKey)` loads the asset
3. `BlobFormat.ExtractBlobData` extracts the data segment
4. `FluxBlob.Load` registers with FormulaCache
5. Repeat calls are incremental — already-scanned assemblies are skipped

`.bytes` files are automatically registered with an Addressables group via `FluxBlobBuildHook` (`IPostprocessBuild`).

## Multi-Mod Architecture

The base game and mods use the same path — the base game is simply "the first mod."

Each mod assembly contains a single SG-generated `BlobRegistry : IFluxBlobRegistry` implementation. Implementations in different assemblies do not conflict.

```
Base game assembly
  └─ BlobRegistry (BlobKey = "base_game")
       EntryCount = 500
       GetEntries() → BlobEntry[500]

Mod A assembly  
  └─ BlobRegistry (BlobKey = "mod_a_blob")
       EntryCount = 42
       GetEntries() → BlobEntry[42]

Mod B assembly
  └─ BlobRegistry (BlobKey = "mod_b_blob")  
       EntryCount = 0  // empty mod, no pre-compiled formulas
```

At runtime, `FluxBlobScanner` reflectively scans for types implementing `IFluxBlobRegistry`. `FluxBlobRegistryAssemblyAttribute` enables fast filtering of candidate assemblies, avoiding a full `GetTypes()` traversal over all loaded assemblies.

## API Reference

### FluxBlob

```csharp
// Load a blob (returns an independent FluxBlobHandle)
var handle = FluxBlob.Load(blobData, entries);

// Unload a specific blob — removes all entries from FormulaCache and frees pinned memory
FluxBlob.Unload(handle);

// Unload all loaded blobs
FluxBlob.Shutdown();

// Verify bytecode integrity for a formula
bool ok = FluxBlob.VerifyIntegrity(hash);

// State
bool initialized = FluxBlob.IsInitialized;
int totalCount  = FluxBlob.TotalEntryCount;
int totalSize   = FluxBlob.TotalBlobSize;
```

`Load()` supports compressed entries: if `FluxCompression.IsCompressed()` returns true, data is decompressed before caching. Decompressed temporary arrays are lifecycle-managed by `FluxBlobHandle`.

Multiple `Load()` calls create independent `FluxBlobHandle` instances.

### FluxBlobHandle

```csharp
public sealed class FluxBlobHandle : IDisposable
{
    public int EntryCount { get; }       // number of formula entries in this blob
    public bool IsLoaded { get; }        // whether this blob is still loaded
    
    public void Dispose();               // equivalent to FluxBlob.Unload(this)
}
```

### BlobEntry

```csharp
public readonly struct BlobEntry
{
    public readonly DualHash64 Hash;  // formula bytecode hash
    public readonly int Offset;       // byte offset from start of blob data segment
    public readonly int Length;       // bytecode length in bytes
}
```

### BlobFormat

```csharp
public static class BlobFormat
{
    const uint Magic = 0x42584C46;   // "FLXB" LE
    const int HeaderSize = 20;
    const int EntrySize  = 24;
    
    // Parsing
    static bool TryParseHeader(ReadOnlySpan<byte>, out int entryCount, ...);
    static byte[] ExtractBlobData(byte[] fileBytes);
    static BlobEntry[] ReadEntryTable(ReadOnlySpan<byte>, int entryCount);
    
    // Writing (for FluxBlobBuilder)
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

## Compression

`FluxConfig.Current.CompressBlob` controls whether Brotli compression is enabled during blob builds (default: `false`). When enabled, each formula is independently compressed — `FluxBlob.Load()` auto-detects and decompresses at runtime.

```csharp
FluxConfig.Set(new FluxConfig
{
    CompressBlob = true,
    BlobFilePath = "Assets/StreamingAssets/flux.bytes"
});
```

## See Also

- [Compile Cache Pipeline](../technical/compile-cache) — Blob → FormulaCache → Delegate full chain
- [FormulaCache](../api/formula-cache) — Cache implementation and Remove method
- [FluxConfig](../api/flux-config) — `BlobFilePath`, `CompressBlob` configuration
- [DualHash64](../api/dualhash64) — Hash keys
