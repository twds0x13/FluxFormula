# FluxConfig

Single injection point for project-level global configuration. All hard-coded constants are centralized here and take effect globally via the `Current` static property.

## Signature

```csharp
public class FluxConfig
```

## Static Members

| Member | Type | Description |
|------|------|------|
| `Default` | `FluxConfig` | Factory default (FormulaCacheCapacity=256, MergeThreshold=8) |
| `Current` | `FluxConfig` | Currently active global config. Returns `Default` when not explicitly set |

### Set

```csharp
public static void Set(FluxConfig config)
```

Replaces the current config. Equivalent to `Current = config`. Passing `null` throws `ArgumentNullException`.

## Configuration Properties

| Property | Type | Default | Description |
|------|------|------|------|
| `FormulaCacheCapacity` | `int` | `256` | `FormulaCache` hashmap slot count. Larger = fewer collisions, more memory |
| `NativeBytecodeCacheCapacity` | `int` | `256` | `NativeBytecodeCache` hashmap slot count. Unique formula type count in Burst Jobs path is typically far smaller than instance count |
| `MergeThreshold` | `int` | `8` | Chain merge threshold — `ToAtomic()` merges when chain length exceeds this |
| `BlobFilePath` | `string` | `null` | Blob binary file path. null = `StreamingAssets/flux.bytes` |
| `CompressBlob` | `bool` | `false` | Enables Brotli compression per-formula in blob. Auto-decompressed by `FluxBlob.Load()` at runtime |
| `DiskCacheDirectory` | `string` | `null` | Disk cache directory. null = `Application.persistentDataPath` |

All properties use `init` accessors — immutable after construction.

## Usage

```csharp
// At application startup
FluxConfig.Set(new FluxConfig
{
    FormulaCacheCapacity = 4096,
    MergeThreshold       = 16,
});

// Read
int cap = FluxConfig.Current.FormulaCacheCapacity; // 4096

// Reset to default
FluxConfig.Set(FluxConfig.Default);
```

## Unity Integration

`FluxConfigAsset` (`ScriptableObject`) auto-loads from `Resources/FluxConfig.asset` via `RuntimeInitializeOnLoadMethod` and calls `Apply()`. Create via: Right-click → Create → FluxFormula → Config.

## See Also

- [FormulaCache](./formula-cache) — Cache implementation, reads `FormulaCacheCapacity` at construction
- [FluxFormula](./flux-formula) — `ToAtomic()` uses `MergeThreshold`
