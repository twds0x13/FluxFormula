using System;
using System.Threading.Tasks;
using FluxFormula.Core;
using UnityEngine;

/// <summary>
/// Loads a formula from Addressables, injects variables, and evaluates the result.
/// Attach to a GameObject, assign a FluxAsset in the inspector, and call Calculate().
/// </summary>
public class DamageCalculator : MonoBehaviour
{
    [Tooltip("Drag a FluxAsset here (must be marked Addressable)")]
    public FluxFormulaRef<float, FloatMathDef> formula;

    [Tooltip("Variable 'atk' value")]
    public float attack = 10f;

    [Tooltip("Variable 'rate' value")]
    public float rate = 1.5f;

    private FluxAssembler<float, FloatMathDef> _assembler;

    private void Awake()
    {
        _assembler = new FluxAssembler<float, FloatMathDef>(default);
    }

    /// <summary>
    /// Load the formula from Addressables, set variables, evaluate.
    /// Call this from a button or Start().
    /// </summary>
    public async ValueTask<float> CalculateAsync()
    {
        var formula = await this.formula.LoadFormulaAsync();

        float result = _assembler.Instantiate(formula, jit: true)
            .Set("atk", attack)
            .Set("rate", rate)
            .Run();

        Debug.Log($"Damage: {result}");
        return result;
    }
}
