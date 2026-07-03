using System.Globalization;
using FluxFormula.Core;
using FluxFormula.Burst;
using UnityEngine;

/// <summary>
/// Burst 同步求值示例：编译公式 → 创建 Burst 实例 → 注入变量 → 同步执行。
/// 适合单次求值或 Editor 脚本。
/// </summary>
public class BurstSyncSample : MonoBehaviour
{
    [Tooltip("公式表达式（变量用方括号括起，如 [atk] * 2 + [bonus]）")]
    public string formulaString = "[atk] * 2 + [bonus]";

    [Tooltip("变量 'atk' 的值")]
    public float attack = 100f;

    [Tooltip("变量 'bonus' 的值")]
    public float bonus = 50f;

    private FluxAssembler<float, FloatMathDef> _assembler;
    private FluxFormula<float, FloatMathDef> _formula;

    private void Awake()
    {
        _assembler = new FluxAssembler<float, FloatMathDef>(default);

        var config = new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'), CultureInfo.InvariantCulture)),
            LiteralOper    = (byte)FloatOp.Const,
            Operators      = { new("+", FloatOp.Add), new("-", FloatOp.Sub),
                               new("*", FloatOp.Mul), new("/", FloatOp.Div) },
            Brackets       = { new("(", ")", FloatOp.LParen, FloatOp.RParen) },
            VariablePatterns = { new("[", "]") },
        };
        _formula = _assembler.Compile(new FluxLexer<float>(config).Lex(formulaString));
    }

    /// <summary>同步执行公式求值</summary>
    [ContextMenu("Evaluate Sync")]
    public void EvaluateSync()
    {
        using var job = _assembler.CreateBurstInstance(_formula);
        job.Set("atk", attack).Set("bonus", bonus);
        float result = job.Run();

        Debug.Log($"[BurstSync] {formulaString} = {result} (atk={attack}, bonus={bonus})");
    }
}
