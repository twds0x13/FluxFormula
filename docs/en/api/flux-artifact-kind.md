# FluxArtifactKind

Enum mapping binary artifact types to file extensions: `.ff` and `.vff`.

## Signature

```csharp
public enum FluxArtifactKind : byte
{
    Formula = 0,  // .ff — formula bytecode
    Virtual = 1,  // .vff — virtual formula reference
}
```

## Values

| Value | Name | Extension | Description |
|------|------|------|------|
| `0` | `Formula` | `.ff` | Formula bytecode — product of `FluxFormula<TData, TDef>.ToBytes()` |
| `1` | `Virtual` | `.vff` | Virtual formula reference — product of `VffFormat.ToBytes()` |

## Usage

Passed as the `kind` argument to `IFluxFileFormatter.Save()` so external savers can differentiate file types:

```csharp
byte[] data = formula.ToBytes();
builder.Save(data, FluxArtifactKind.Formula, "Damage.ff");

byte[] vffData = VffFormat.ToBytes(links, overrides);
builder.Save(vffData, FluxArtifactKind.Virtual, "DamageChain.vff");
```

## See Also

- [IFluxFileFormatter](./iflux-file-formatter) — persistence interface
- [FluxFormula](./flux-formula) — formula bytecode serialization
- [VffFormat](./vff-format) — VFF format serialization and parsing
