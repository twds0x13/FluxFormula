# 示例：UniTask 异步加载

> **依赖包：** `com.twds0x13.fluxformula.addressables.unitask`（3.2+）
> 此包依赖 `com.twds0x13.fluxformula.addressables` 和 `com.cysharp.unitask`，UPM 安装时自动拉入。仅当项目已安装 UniTask 时激活（`autoReferenced: false`）。

以下示例演示在 UniTask 驱动的项目中使用 FluxFormula Addressables 加载。

## 基本用法

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

        // 在 UniTask 上下文中多次求值
        for (int i = 0; i < 10; i++)
        {
            float result = instance.Set("atk", 100f + i * 10).Run();
            Debug.Log($"Round {i}: {result}");
            await UniTask.Yield();
        }
    }
}
```

## UniTask 特有的取消支持

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

## 与原生 ValueTask 的差异

`fluxformula.addressables` 默认返回 `ValueTask<T>`，无需依赖 UniTask。仅在需要 UniTask 特性（取消令牌、`UniTask.WhenAll` 组合、`PlayerLoopTiming` 调度）时安装此包。

```csharp
// 原生 — 无需 UniTask
var formula = await FormulaRef.LoadFormulaAsync();

// UniTask — 需要 fluxformula.addressables.unitask
var formula = await FormulaRef.LoadFormulaAsync(cancellationToken);
var (f1, f2) = await UniTask.WhenAll(
    FormulaRef1.LoadFormulaAsync(),
    FormulaRef2.LoadFormulaAsync());
```

## 注意事项

- 此包 `autoReferenced: false`。需在 Unity 项目中有 `com.cysharp.unitask` 才会被激活。
- `LoadFormulaAsync` 重载接受 `CancellationToken`，不修改底层 Addressables 操作。
- 类型指纹校验在加载时自动执行：TDef 不匹配抛出 `InvalidOperationException`。

## 参见

- [Addressables 加载示例](./addressables-load) — 原生 ValueTask 用法
- [FluxFormula / FluxModifier API](../api/flux-formula)
- [UniTask 官方文档](https://github.com/Cysharp/UniTask)
