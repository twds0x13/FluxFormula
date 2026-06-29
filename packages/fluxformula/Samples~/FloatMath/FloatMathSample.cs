using System.Globalization;
using FluxFormula.Core;
using UnityEngine;

/// <summary>
/// FluxFormula 入门示例：演示浮点公式的编译、变量绑定和求值。
/// 右键组件标题 → 选择 ContextMenu 项执行。
/// </summary>
public class FloatMathSample : MonoBehaviour
{
    [Tooltip("浮点四则运算表达式，支持 + - * / 和变量 [varName]")]
    public string expression = "[atk] * (1 + [critDmg]) - [def]";

    public float atk = 150f;
    public float critDmg = 0.5f;
    public float defense = 30f;

    private FluxAssembler<float, FloatMathDef> _assembler;

    private void Awake()
    {
        _assembler = new FluxAssembler<float, FloatMathDef>(default);
        Debug.Log($"[FluxFormula] Assembler ready. Try: {expression}");
    }

    /// <summary>
    /// 解释器求值：全平台兼容（含 IL2CPP/AOT），~27ns 一次。
    /// </summary>
    [ContextMenu("Evaluate (Interpreter)")]
    public void EvaluateInterpreter()
    {
        float result = CompileAndEval(jit: false);
        Debug.Log($"[Interpreter] {expression} = {result}");
    }

    /// <summary>
    /// JIT 求值：编译为委托，~2ns 一次，不支持 IL2CPP/AOT。
    /// JIT 不可用时自动降级为解释器。
    /// </summary>
    [ContextMenu("Evaluate (JIT)")]
    public void EvaluateJit()
    {
        float result = CompileAndEval(jit: true);
        Debug.Log($"[JIT] {expression} = {result}");
    }

    private float CompileAndEval(bool jit)
    {
        var lexer = CreateLexer();
        var tokens = lexer.Lex(expression);
        var formula = _assembler.Compile(tokens);

        using var instance = _assembler.Instantiate(formula, jit);
        instance.Set("atk", atk);
        instance.Set("critDmg", critDmg);
        instance.Set("def", defense);
        return instance.Run();
    }

    /// <summary>
    /// 创建 Lexer 配置：四则运算符 + 括号 + [varName] 变量模式。
    /// </summary>
    private static FluxLexer<float> CreateLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f'), CultureInfo.InvariantCulture),
            LiteralOper    = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
            },
        });
    }
}
