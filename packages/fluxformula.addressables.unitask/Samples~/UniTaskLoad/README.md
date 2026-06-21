# UniTask Load

Demonstrates formula loading via `FluxFormulaRef` with UniTask async extensions.

## When to use this sample

Use this when your project has `com.cysharp.unitask` installed. The base `ValueTask` API in
`FluxFormula.Addressables` is sufficient for most cases — UniTask is only needed if you
need to `await` within UniTask-based coroutines or integrate with UniTask's async pipeline.

## File Structure

```
UniTaskLoad/
├── README.md
├── DamageCalculatorUniTask.cs    # MonoBehaviour: UniTask-based async load
└── DamageCalculatorUniTask.cs.meta
```

## Setup

Same as the AddressablesLoad sample. The key difference is using `LoadFormulaUniTaskAsync()` instead of `LoadFormulaAsync()`.

## Key Points

- `FluxFormulaRefUniTaskExtensions.LoadFormulaUniTaskAsync()` delegates to the ValueTask version internally
- `FluxFormulaRefUniTaskExtensions.LoadAssetTypedUniTaskAsync()` loads the asset without formula deserialization
- Zero additional allocation — the UniTask wrapper just awaits the ValueTask
