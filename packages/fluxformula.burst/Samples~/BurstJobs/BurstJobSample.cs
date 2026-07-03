using System.Globalization;
using FluxFormula.Core;
using FluxFormula.Burst;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Burst Job 异步调度示例：将公式求值提交到 Unity Job 系统，不阻塞主线程。
/// </summary>
public class BurstJobSample : MonoBehaviour
{
    [Tooltip("伤害公式表达式")]
    public string damageFormula = "[atk] * (1 + [critDmg]) - [def]";

    public float attack = 150f;
    public float critDamage = 0.5f;
    public float defense = 30f;

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
        _formula = _assembler.Compile(new FluxLexer<float>(config).Lex(damageFormula));
    }

    /// <summary>使用 ScheduleBurst 一步完成：创建实例 → 设置变量 → 调度 Job</summary>
    [ContextMenu("Evaluate Async (Job)")]
    public void EvaluateAsync()
    {
        using var instance = _assembler.ScheduleBurst(
            _formula,
            ("atk", attack),
            ("critDmg", critDamage),
            ("def", defense));

        instance.Complete();
        float result = instance.Result;

        Debug.Log($"[BurstJob] {damageFormula} = {result}");
    }

    /// <summary>手动控制模式：CreateBurstInstance → Set → Schedule → Complete</summary>
    [ContextMenu("Evaluate Async (Manual)")]
    public void EvaluateAsyncManual()
    {
        using var job = _assembler.CreateBurstInstance(_formula);
        job.Set("atk", attack)
           .Set("critDmg", critDamage)
           .Set("def", defense);

        var handle = job.Schedule();
        handle.Complete();
        float result = job.Result;

        Debug.Log($"[BurstJob Manual] {damageFormula} = {result}");
    }
}
