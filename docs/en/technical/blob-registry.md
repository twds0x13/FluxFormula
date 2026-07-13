# Blob Registry

Its core design question: how to distribute pre-compiled formula bytecode to end users, load it at runtime with zero parsing overhead, while supporting multiple mods each carrying their own formula set independently.

The answer is to move offset table parsing from runtime to compile time: the `IFluxBlobRegistry` interface defines the registry contract; `BlobRegistryGenerator` (IIncrementalGenerator) reads `.bytes` files at compile time to generate compile-time-constant `BlobEntry[]`; `FluxBlob.Load/Unload` additive model supports independent loading and unloading of multiple blobs.

## Architecture Overview

```mermaid
flowchart TD
    subgraph BuildTime["Build Time (Editor)"]
        A[FluxBlobBuilder] -->|Scan FluxAsset| B[.bytes file]
    end
    subgraph CompileTime["Compile Time"]
        C[BlobRegistryGenerator] -->|Read .bytes| D[BlobRegistry.g.cs]
        D -->|BlobEntry[] constants| E[assembly: FluxBlobRegistryAssembly]
    end
    subgraph RuntimeStartup["Runtime (Startup)"]
        F[Load .bytes] --> G[BlobFormat.ExtractBlobData]
        G --> H[FluxBlob.Load]
        H -->|GCHandle.Pin| I[FormulaCache.Put]
    end
    subgraph RuntimeMod["Runtime (Mod Load)"]
        J[FluxBlobScanner] -->|Reflection discover IFluxBlobRegistry| K[Load mod .blob]
        K --> H
    end
```

## IFluxBlobRegistry Interface

The core abstraction in the Core layer (zero UnityEngine dependency):

```csharp
public interface IFluxBlobRegistry
{
    int EntryCount { get; }
    string BlobKey { get; }
    BlobEntry[] GetEntries();
}
```

Three members:
- `EntryCount`: number of formula entries. 0 indicates an empty mod (no blob file).
- `BlobKey`: the loading key for the corresponding `.blob` file (Addressables key or file path).
- `GetEntries()`: returns compile-time-constant `BlobEntry[]`, each mapping a `DualHash64` to an offset and length in the blob data segment.

`FluxBlobRegistryAssemblyAttribute` is an assembly-level marker with no data, used solely by `FluxBlobScanner` to quickly filter assemblies containing registries, avoiding full `GetTypes()` traversal on all loaded assemblies.

## .blob Binary Format

`BlobFormat` (Core layer) defines the format:

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
  Concatenated formula bytecode. Entry offsets are relative to the start of this segment.
```

`BlobEntry` (24 bytes): `DualHash64` Key(16) + Offset(4) + Length(4). One entry per formula; Offset is relative to the blob data segment start, not the file start.

## BlobRegistryGenerator

`IIncrementalGenerator`, located in `fluxformula/SourceGenerator/`. Reads `.bytes` file header + entry table at compile time, generates `BlobRegistry.g.cs`:

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

Key properties:
- `internal` class: different assemblies each generate their own `BlobRegistry`, naturally isolated with no conflicts
- `[assembly: FluxBlobRegistryAssembly]`: enables `FluxBlobScanner` to quickly filter assemblies
- `BlobKey`: extracted from the `.bytes` filename, used as the Addressables key
- Offset table as compile-time constants: zero runtime parsing overhead

If no `.bytes` file is found, generates an empty stub (`EntryCount=0`) to ensure compilation does not break.

## Multi-Mod Architecture

Core model: the game assembly = the first mod.

```
Game Assembly (Assembly-CSharp)
  BlobRegistry.g.cs (internal, IFluxBlobRegistry)
  flux.bytes (Addressables or StreamingAssets)

Mod A (ModA.dll, AssetBundle)
  BlobRegistry.g.cs (internal, IFluxBlobRegistry)
  modA.blob (TextAsset in AssetBundle)
  FluxBlobScanner.DiscoverAll() → FluxBlob.Load()

Shared State: FormulaCache.Instance
  DualHash64 is content-based → identical formulas from different mods
  automatically share cache entries
```

Key design properties:
- `internal` classes in different assemblies do not conflict: each mod's `BlobRegistry` is naturally isolated
- `IFluxBlobRegistry` interface provides a unified discovery contract
- `FluxBlob.Load()` is additive: no dependency on Reset/Shutdown; each call creates an independent `FluxBlobHandle`
- `FluxBlobHandle` tracks each blob's lifecycle: GCHandle + EntryKeys + IDisposable

## FluxBlob.Load/Unload Additive Model

`FluxBlob.Load(byte[] blobData, ReadOnlySpan<BlobEntry> entries)`:

1. `GCHandle.Pin(blobData)` pins the blob data to prevent GC relocation
2. Per-entry `FormulaCache.Put(hash, ptr+offset, length)` registers bytecode pointers
3. Creates a `FluxBlobHandle` recording the GCHandle and all EntryKeys
4. If the blob has Brotli compression enabled (Flags bit 0), decompresses before pinning

`FluxBlob.Unload(FluxBlobHandle)`:

1. Per-EntryKey `FormulaCache.Remove(key)` deletes cache entries
2. `GCHandle.Free()` releases memory
3. Releases Brotli decompression handles

## Key Type Distribution

| Layer | Type | Responsibility |
|-------|------|----------------|
| Core | `BlobEntry` | Offset table entry struct |
| Core | `IFluxBlobRegistry` | Registry interface |
| Core | `BlobFormat` | .blob binary format definition |
| Core | `FluxBlobRegistryAssemblyAttribute` | Assembly marker |
| Core | `FormulaCache.Remove()` | Per-key deletion |
| Unity | `FluxBlob` | Static facade: Load/Unload/Shutdown |
| Unity | `FluxBlobHandle` | Single blob handle: IDisposable |
| Unity | `FluxBlobScanner` | Reflection scan for IFluxBlobRegistry |
| SG | `BlobRegistryGenerator` | IIncrementalGenerator |

## Comparison with Old Architecture

| Dimension | v5.1.x (Old) | v5.8+ (New) |
|-----------|-------------|------------|
| Offset table | C# byte[] literal (~6× code bloat) | Compile-time constant `BlobEntry[]` (1:1) |
| Multi-blob | Not supported (Initialize causes Shutdown) | Load/Unload additive model |
| Mod discovery | None | FluxBlobScanner reflection scan |
| Content Update | Not feasible | .bytes files independently updatable |
| Runtime overhead | Parse → BuildEntries at startup | Done at compile time, zero overhead |

## References

- [Compile Cache Pipeline](./compile-cache.md) -- DualHash64 + FormulaCache full chain
- [Blob Registry Guide](../guide/blob-registry.md) -- user-facing API guide
- [Architecture Decisions](./architecture-decisions.md) -- ADR v5.2.0 BlobRegistry Source Generator
