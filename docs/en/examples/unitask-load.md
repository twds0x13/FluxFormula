# Example: UniTask Load

> **Required package:** `com.twds0x13.fluxformula.addressables.unitask` (3.2+)
> This package depends on `com.twds0x13.fluxformula.addressables` and `com.cysharp.unitask`, pulled automatically by UPM. Only active when the project already has UniTask installed (`autoReferenced: false`).

The following example demonstrates FluxFormula Addressables loading in a UniTask-driven project.

## Basic Usage

```csharp
using FluxFormula.Core;
using FluxFormula.Addressables;
using FluxFormula.Addressables.UniTask;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class DamageCalculator : MonoBehaviour
{
    public FluxFormulaRef<float, MathDef> FormulaRef;

    private FluxFormula<float, MathDef> _formula;

    async UniTask Start()
    {
        _formula = await FormulaRef.LoadFormulaAsync();
        Debug.Log($"Loaded: {_formula}");

        var assembler = new FluxAssembler<float, MathDef>(new MathDef());
        var instance = assembler.Instantiate(_formula);

        // Evaluate multiple times in a UniTask context
        for (int i = 0; i < 10; i++)
        {
            float result = instance.Set("atk", 100f + i * 10).Run();
            Debug.Log($"Round {i}: {result}");
            await UniTask.Yield();
        }
    }
}
```

## Cancellation Support

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfterSlim(TimeSpan.FromSeconds(5), PlayerLoopTiming.Update);

try
{
    _formula = await FormulaRef.LoadFormulaAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Debug.Log("Load cancelled after timeout");
}
```

## Difference from Native ValueTask

`fluxformula.addressables` returns `ValueTask<T>` by default and requires no UniTask. Install this package only when UniTask features (cancellation tokens, `UniTask.WhenAll` composition, `PlayerLoopTiming` scheduling) are needed.

```csharp
// Native — no UniTask required
var formula = await FormulaRef.LoadFormulaAsync();

// UniTask — requires fluxformula.addressables.unitask
var formula = await FormulaRef.LoadFormulaAsync(cancellationToken);
var (f1, f2) = await UniTask.WhenAll(
    FormulaRef1.LoadFormulaAsync(),
    FormulaRef2.LoadFormulaAsync());
```

## Notes

- This package is `autoReferenced: false`. It activates only when `com.cysharp.unitask` is present in the Unity project.
- `LoadFormulaAsync` overloads accept `CancellationToken` without modifying the underlying Addressables operation.
- Type fingerprint validation runs automatically at load time — TDef mismatch throws `InvalidOperationException`.

## See Also

- [Addressables Load Example](./addressables-load) — native ValueTask usage
- [FluxFormula / FluxModifier API](../api/flux-formula)
- [UniTask Documentation](https://github.com/Cysharp/UniTask)
