# IFluxFileFormatter

Minimal file persistence contract. Core provides read/write methods — consumers inject an external implementation or use the built-in `FileFluxFileFormatter`.

## Signature

```csharp
public interface IFluxFileFormatter
{
    void Save(byte[] data, FluxArtifactKind kind, string path);
    byte[] Load(string path, out FluxArtifactKind kind);
}
```

## Design Rationale

The interface is deliberately non-generic — the caller serializes data to `byte[]` before `Save()` (via `FluxFormula.ToBytes()` or `VffFormat.ToBytes()`), and deserializes after `Load()` (via `FluxFormula.FromBytes()` or `VffFormat.FromBytes()`). The interface only needs to know "where to put bytes" and "where to read bytes from."

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

### Load

```csharp
byte[] Load(string path, out FluxArtifactKind kind)
```

Loads the raw byte data of a binary artifact from the given path.

| Parameter | Type | Description |
|------|------|------|
| `path` | `string` | Source path |
| `kind` | `FluxArtifactKind` | Artifact type (auto-detected via magic bytes) |

## Built-in Implementation: FileFluxFileFormatter

```csharp
public sealed class FileFluxFileFormatter : IFluxFileFormatter
```

Default implementation backed by `System.IO.File`. Automatically appends `.ff` / `.vff` extensions based on `FluxArtifactKind`; detects type on load via `VffFormat.IsVff()` magic byte check.

## Usage Example

```csharp
var formatter = new FileFluxFileFormatter();

// Save
formatter.Save(formula.ToBytes(), FluxArtifactKind.Formula, "Damage");
// → Damage.ff

// Load
byte[] data = formatter.Load("Damage", out var kind);
var loaded = FluxFormula<float, MathDef>.FromBytes(data);

// VFF roundtrip
var links = chain.GetLinks().ToArray();
formatter.Save(VffFormat.ToBytes<float>(links, overrides),
    FluxArtifactKind.Virtual, "DamageChain");
// → DamageChain.vff

byte[] vffData = formatter.Load("DamageChain", out var vffKind);
var result = VffFormat.FromBytes<float, MathDef>(vffData);
```

## See Also

- [FluxArtifactKind](./flux-artifact-kind) — artifact type enum
- [FluxFormula](./flux-formula) — formula bytecode serialization
- [VffFormat](./vff-format) — VFF format serialization and parsing
