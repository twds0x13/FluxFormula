# 示例：Addressables 异步加载

> **依赖包：** `com.twds0x13.fluxformula.addressables`（3.2+）
> 需额外安装 `com.unity.addressables`。UPM 安装时自动拉入。

以下示例演示通过 `FluxFormulaRef<TData, TDef>` 从 Addressables 异步加载公式，注入变量后求值。

## 基本加载流程

```csharp
using FluxFormula.Core;
using FluxFormula.Addressables;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class DamageCalculator : MonoBehaviour
{
    // Inspector 中拖入标记为 Addressable 的 FluxAsset
    public FluxFormulaRef<float, MathDef> FormulaRef;

    private FluxFormula<float, MathDef> _formula;

    async void Start()
    {
        _formula = await FormulaRef.LoadFormulaAsync();
        Debug.Log($"Loaded: {_formula}");

        // 编译一次，多次求值：走 FormulaCache
        var assembler = new FluxAssembler<float, MathDef>(new MathDef());
        var instance = assembler.Instantiate(_formula);
        float result = instance.Set("atk", 100f).Set("def", 50f).Run();
        Debug.Log($"Result: {result}");
    }
}
```

## 错误处理

`LoadFormulaAsync` 在以下情况抛出异常：

```csharp
try
{
    _formula = await FormulaRef.LoadFormulaAsync();
}
catch (InvalidOperationException ex)
{
    // 资产存在但 TDef 类型不匹配
    Debug.LogError($"Type mismatch: {ex.Message}");
}
catch (Exception ex)
{
    // 资产加载失败（key 不存在、网络超时等）
    Debug.LogError($"Load failed: {ex.Message}");
}
```

## 检查加载状态

`FluxFormulaRef<TData, TDef>` 继承 `AssetReferenceT<FluxAsset>`，可使用 Addressables 原生 API 检查状态：

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

## 完整设置步骤

1. 编译公式为 `FluxAsset`：右键 Project → FluxFormula → Create Asset
2. 在 Inspector 中勾选 Addressable，分配 key
3. 在 `MonoBehaviour` 中声明 `public FluxFormulaRef<float, MathDef> FormulaRef`
4. Inspector 中将 `FluxAsset` 拖入引用槽
5. 运行时调用 `await FormulaRef.LoadFormulaAsync()` 加载

## 注意事项

- `FluxFormulaRef<TData, TDef>` 是 `[Serializable]`，Inspector 可见
- 加载后公式缓存在 `FormulaCache` 中，同一公式多次加载只反序列化一次
- 类型不匹配在加载时抛出 `InvalidOperationException`，非静默失败
- Addressables Samples 目录包含完整可运行示例

## 参见

- [ChainLink 示例](./chain-connect) — 链式公式组合
- [FluxFormula / FluxModifier API](../api/flux-formula) — 字节码容器
- [Unity Addressables 文档](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
