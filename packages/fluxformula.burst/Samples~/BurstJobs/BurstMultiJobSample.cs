using System.Globalization;
using FluxFormula.Core;
using FluxFormula.Burst;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 多 Job 并发示例：多个公式并发调度，各自持有独立的 NativeArray。
/// 演示 Job 依赖链和 NativeBytecodeCache 共享字节码。
/// </summary>
public class BurstMultiJobSample : MonoBehaviour
{
    [Header("Damage Formula")]
    public string damageFormula = "[atk] * 2 - [def]";
    public float attack = 120f;
    public float defense = 40f;

    [Header("Heal Formula")]
    public string healFormula = "[wis] * 1.5 + [lvl]";
    public float wisdom = 60f;
    public float level = 10f;

    private FluxAssembler<float, FloatMathDef> _assembler;
    private FluxFormula<float, FloatMathDef> _damageFormula;
    private FluxFormula<float, FloatMathDef> _healFormula;
    private NativeBytecodeCache _cache;

    private void Awake()
    {
        _assembler = new FluxAssembler<float, FloatMathDef>(default);

        var config = new LexerConfig<float>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f'), CultureInfo.InvariantCulture),
            LiteralOper    = (byte)FloatOp.Const,
            Operators      = { new("+", FloatOp.Add), new("-", FloatOp.Sub),
                               new("*", FloatOp.Mul), new("/", FloatOp.Div) },
            Brackets       = { new("(", ")", FloatOp.LParen, FloatOp.RParen) },
            VariablePatterns = { new("[", "]") },
        };
        var lexer = new FluxLexer<float>(config);
        _damageFormula = _assembler.Compile(lexer.Lex(damageFormula));
        _healFormula = _assembler.Compile(lexer.Lex(healFormula));

        // 共享字节码缓存——同公式的多个实例复用同一块 NativeArray
        _cache = new NativeBytecodeCache();
    }

    /// <summary>两个公式并发执行，使用 JobHandle.CompleteAll 等待</summary>
    [ContextMenu("Evaluate Concurrent")]
    public void EvaluateConcurrent()
    {
        var job1 = _assembler.CreateBurstInstance(_damageFormula, _cache)
            .Set("atk", attack)
            .Set("def", defense);

        var job2 = _assembler.CreateBurstInstance(_healFormula, _cache)
            .Set("wis", wisdom)
            .Set("lvl", level);

        var h1 = job1.Schedule();
        var h2 = job2.Schedule();
        JobHandle.CompleteAll(h1, h2);

        float damage = job1.Result;
        float heal = job2.Result;

        Debug.Log($"[BurstMultiJob] Damage: {damage}, Heal: {heal}");

        job1.Dispose();
        job2.Dispose();
    }

    private void OnDestroy()
    {
        _cache.Dispose();
    }
}
