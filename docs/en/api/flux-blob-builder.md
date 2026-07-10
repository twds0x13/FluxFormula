# FluxBlobBuilder

Blob build pipeline: scans all FluxAsset formula bytecode in the project and assembles a single binary .bytes file. The output is consumed by the source generator at compile time and by `FluxBlob.Load()` at runtime.

## Signature

```csharp
public static class FluxBlobBuilder
```

## Methods

### Build

```csharp
public static int Build()
```

Runs the full blob build pipeline:

1. Scans all `FluxAsset` components via `AssetDatabase.FindAssets("t:FluxAsset")`
2. Deduplicates by `DualHash64` (identical content is skipped)
3. If `FluxConfig.Current.CompressBlob` is true, applies `FluxCompression.Compress()` to each formula
4. Sorts by dual hash, concatenates into a single byte array, and builds a `BlobEntry[]` offset table
5. Writes the .bytes file via `BlobFormat.WriteHeader` / `WriteEntry`
6. Calls `AssetDatabase.Refresh()` to refresh the asset database

| Return | Description |
|--------|-------------|
| `int` | Number of formula entries written. 0 means no valid formulas or no FluxAssets in the project |

### BuildFromMenu

```csharp
[MenuItem("FluxFormula/Build Blob", priority = 200)]
public static void BuildFromMenu()
```

Editor menu entry. Calls `Build()` and displays the result in a dialog.

### ClearFromMenu

```csharp
[MenuItem("FluxFormula/Clear Blob", priority = 201)]
public static void ClearFromMenu()
```

Editor menu entry. After confirmation, deletes the .bytes file and its `.meta` file, then refreshes the asset database.

## Nested Types

### BuildPreprocessor

```csharp
public class BuildPreprocessor : IPreprocessBuildWithReport
```

Automatically triggers `Build()` before player builds, ensuring published builds include the latest formula blob.

| Member | Value |
|--------|-------|
| `callbackOrder` | `-100` |

## Usage

```csharp
// Manual build
int count = FluxBlobBuilder.Build();
Debug.Log($"Blob built: {count} formulas");

// Via menu
// Menu path: FluxFormula > Build Blob

// Pre-build trigger: BuildPreprocessor automatically calls Build() during Player Build
```

## See Also

- [BlobFormat](./blob-format) — .blob binary format definition
- [FluxBlob](./flux-blob) — Runtime blob loading and unloading
- [IFluxBlobRegistry](./iflux-blob-registry) — Blob registry interface and source generator
