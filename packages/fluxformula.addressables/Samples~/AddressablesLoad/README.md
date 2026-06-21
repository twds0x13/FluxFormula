# Addressables Load

Demonstrates asynchronous formula loading via `FluxFormulaRef` with Unity Addressables.

## File Structure

```
AddressablesLoad/
├── README.md
├── DamageCalculator.cs          # MonoBehaviour: load formula from Addressables, inject variables, run
└── DamageCalculator.cs.meta
```

## Setup

1. Create a `FluxAsset` from a compiled formula (right-click in Project → FluxFormula → Create Asset, or use `FluxAssembler` + `CreateAsset()`)
2. Mark the `FluxAsset` as Addressable and assign it a key
3. Add the `DamageCalculator` component to a GameObject
4. Drag the `FluxAsset` into the `Formula Reference` field
5. Press Play

## Key Points

- `FluxFormulaRef<TData, TOper, TDef>` is a serializable `AssetReferenceT<FluxAsset>`
- `LoadFormulaAsync()` is a single await: Addressables load → type check → `FromBytes` deserialization
- Type mismatch between the asset's `TypeId` and `TDef` throws `InvalidOperationException`
- On load failure, returns `FluxFormula<TData, TOper>.Empty`
