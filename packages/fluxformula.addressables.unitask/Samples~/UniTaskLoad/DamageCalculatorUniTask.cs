using System;
using Cysharp.Threading.Tasks;
using FluxFormula.Core;
using UnityEngine;

/// <summary>
/// UniTask-based formula loader. Use when your project has com.cysharp.unitask installed.
/// Attach to a GameObject, assign a FluxAsset, and call CalculateAsync().
/// </summary>
public class DamageCalculatorUniTask : MonoBehaviour
{
    [Tooltip("Drag a FluxAsset here (must be marked Addressable)")]
    public FluxFormulaRef<float, FloatOp, FloatMathDef> formula;

    [Tooltip("Variable 'atk' value")]
    public float attack = 10f;

    [Tooltip("Variable 'rate' value")]
    public float rate = 1.5f;

    private FluxAssembler<float, FloatOp, FloatMathDef> _assembler;

    private void Awake()
    {
        _assembler = new FluxAssembler<float, FloatOp, FloatMathDef>(default);
    }

    /// <summary>
    /// Load the formula via UniTask, inject variables, evaluate.
    /// </summary>
    public async UniTask<float> CalculateAsync()
    {
        // Uses LoadFormulaUniTaskAsync extension from FluxFormula.Addressables.UniTask
        var formula = await this.formula.LoadFormulaUniTaskAsync<float, FloatOp, FloatMathDef>();

        float result = _assembler.Instantiate(formula, jit: true)
            .Set("atk", attack)
            .Set("rate", rate)
            .Run();

        Debug.Log($"Damage (UniTask): {result}");
        return result;
    }
}
