# Example: Addressables Async Load

> **Required package:** `com.twds0x13.fluxformula.addressables` (3.2+)
> Also requires `com.unity.addressables`. Pulled automatically by UPM.

The following examples demonstrate loading formulas asynchronously via `FluxFormulaRef<TData, TDef>` from Unity Addressables, injecting variables, and evaluating.

## Basic Load Flow

```csharp
using FluxFormula.Core;
using FluxFormula.Addressables;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class DamageCalculator : MonoBehaviour
{
    // Drag an Addressable FluxAsset into this field in the Inspector
    public FluxFormulaRef<float, MathDef> FormulaRef;

    private FluxFormula<float, MathDef> _formula;

    async void Start()
    {
        _formula = await FormulaRef.LoadFormulaAsync();
        Debug.Log($"Loaded: {_formula}");

        // Compile once, evaluate many times — hits FormulaCache
        var assembler = new FluxAssembler<float, MathDef>(new MathDef());
        var instance = assembler.Instantiate(_formula);
        float result = instance.Set("atk", 100f).Set("def", 50f).Run();
        Debug.Log($"Result: {result}");
    }
}
```

## Error Handling

`LoadFormulaAsync` throws in these cases:

```csharp
try
{
    _formula = await FormulaRef.LoadFormulaAsync();
}
catch (InvalidOperationException ex)
{
    // Asset exists but TDef type mismatch
    Debug.LogError($"Type mismatch: {ex.Message}");
}
catch (Exception ex)
{
    // Load failure (missing key, network timeout, etc.)
    Debug.LogError($"Load failed: {ex.Message}");
}
```

## Check Load Status

`FluxFormulaRef<TData, TDef>` inherits `AssetReferenceT<FluxAsset>`. Use Addressables native APIs to check status:

```csharp
if (FormulaRef.RuntimeKeyIsValid())
{
    var handle = Addressables.LoadAssetAsync<FluxAsset>(FormulaRef.RuntimeKey);
    handle.Completed += h =>
    {
        if (h.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
        {
            byte[] raw = h.Result.Bytes;
            _formula = FluxFormula<float, MathDef>.FromBytes(raw);
        }
    };
}
```

## Complete Setup Steps

1. Compile a formula into a `FluxAsset`: right-click in Project → FluxFormula → Create Asset
2. Mark it Addressable in the Inspector, assign a key
3. Declare `public FluxFormulaRef<float, MathDef> FormulaRef` in a `MonoBehaviour`
4. Drag the `FluxAsset` into the reference slot in the Inspector
5. Call `await FormulaRef.LoadFormulaAsync()` at runtime

## Notes

- `FluxFormulaRef<TData, TDef>` is `[Serializable]` and visible in the Inspector
- After loading, the formula is cached in `FormulaCache` — the same formula is only deserialized once
- Type mismatch throws `InvalidOperationException` at load time, not silently
- The Addressables Samples directory contains a complete runnable example

## See Also

- [ChainLink Example](./chain-connect) — chaining formulas
- [FluxFormula / FluxModifier API](../api/flux-formula) — bytecode containers
- [Unity Addressables Documentation](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
