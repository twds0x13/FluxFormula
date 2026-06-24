# IFluxBinaryBuilder

Minimal persistence contract. Core performs no file I/O — consumers inject an external saver implementation.

## Signature

```csharp
public interface IFluxBinaryBuilder
{
    void Save(byte[] data, FluxArtifactKind kind, string path);
}
```

## Design Rationale

The interface is deliberately non-generic — the caller serializes data to `byte[]` before calling `Save()` (via `FluxFormula.ToBytes()` or `VffFormat.ToBytes()`). The interface only needs to know "where to put the bytes."

Core provides no implementation. Dependents supply their own saver:

- **Standalone .NET**: `System.IO.File.WriteAllBytes`
- **Unity Editor**: `AssetDatabase.CreateAsset` or `File.WriteAllBytes`
- **Test environments**: in-memory streams or temp files

## Methods

### Save

```csharp
void Save(byte[] data, FluxArtifactKind kind, string path)
```

Persists the binary artifact to the given path.

| Parameter | Type | Description |
|------|------|------|
| `data` | `byte[]` | Serialized bytecode |
| `kind` | `FluxArtifactKind` | Artifact type (`.ff` or `.vff`) |
| `path` | `string` | Target path (filesystem path or Unity-relative path) |

## Usage Example

```csharp
// Basic System.IO implementation
public class FileBinaryBuilder : IFluxBinaryBuilder
{
    public void Save(byte[] data, FluxArtifactKind kind, string path)
    {
        string ext = kind == FluxArtifactKind.Formula ? ".ff" : ".vff";
        string fullPath = path.EndsWith(ext) ? path : path + ext;
        System.IO.File.WriteAllBytes(fullPath, data);
    }
}

var builder = new FileBinaryBuilder();

// Save a single formula
builder.Save(formula.ToBytes(), FluxArtifactKind.Formula, "Damage");

// Save a chain reference
var links = chain.GetChainLinks().ToArray();
builder.Save(VffFormat.ToBytes<float>(links, overrides),
    FluxArtifactKind.Virtual, "DamageChain");
```

## See Also

- [FluxArtifactKind](./flux-artifact-kind) — artifact type enum
- [FluxFormula](./flux-formula) — formula bytecode serialization
- [VffFormat](./vff-format) — VFF format serialization and parsing
