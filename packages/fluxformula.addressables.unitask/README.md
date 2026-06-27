# FluxFormula Addressables UniTask

UniTask integration for FluxFormula Addressables loading. Opt-in — add `Unity.Addressables`, `UniTask`, and `FluxFormula.Addressables.UniTask` to your assembly references.

## What's in this package

- `FluxFormulaRefUniTaskExtensions` — `LoadFormulaUniTaskAsync()` and `LoadAssetTypedUniTaskAsync()` extension methods on `FluxFormulaRef<TData, TDef>`
- `FormulaLibraryUniTaskExtensions` — `LoadUniTaskAsync()` extension method on `FormulaLibrary<TData, TDef>`

All methods delegate to the underlying `ValueTask` API and convert to `UniTask<T>` with zero additional allocation.

## Usage

```csharp
using FluxFormula.Core;
using Cysharp.Threading.Tasks;

public class DamageCalc : MonoBehaviour
{
    public FluxFormulaRef<float, FloatMathDef> formula;

    async UniTaskVoid Start()
    {
        var f = await formula.LoadFormulaUniTaskAsync();
        float result = new FluxAssembler<float, FloatMathDef>(default)
            .Instantiate(f).Set("atk", 10).Run();
    }
}
```

## Dependencies

- `com.twds0x13.fluxformula.core` ≥ 3.3.0
- `com.twds0x13.fluxformula` ≥ 3.3.0
- `com.twds0x13.fluxformula.addressables` ≥ 3.3.0
- `com.cysharp.unitask` ≥ 2.0.0

## License

MIT
