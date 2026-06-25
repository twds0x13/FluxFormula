# FluxFormula Addressables

Optional Addressables integration for FluxFormula. Op-in — add `FluxFormula.Addressables` to your assembly references.

## What's in this package

- `FluxFormulaRef<TData,TDef>` — type-safe `AssetReferenceT<FluxAsset>` for serialization in MonoBehaviour/ScriptableObject
- `FormulaLibraryAddressablesExtensions` — `LoadAsync(string key)` / `Load(string key)` extension methods on `FormulaLibrary<TData,TDef>`

## Usage

```csharp
// 1. Add "FluxFormula.Addressables" to your asmdef references
// 2. Serialize a reference
public class DamageCalc : MonoBehaviour
{
    public FluxFormulaRef<float, FloatMathDef> formula;

    async void Start()
    {
        var f = await formula.LoadFormulaAsync();
        float result = new FluxAssembler<float, FloatMathDef>(default)
            .Instantiate(f).Set("atk", 10).Run();
    }
}
```

## Dependencies

- `com.twds0x13.fluxformula.core` ≥ 2.0.0
- `com.twds0x13.fluxformula` ≥ 2.0.0
- `com.unity.addressables` ≥ 1.0.0

## License

MIT
