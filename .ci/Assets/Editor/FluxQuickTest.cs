using FluxFormula.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 临时手动测试入口——菜单 Window > Flux Test > ...
/// 在此随意添加测试代码，不纳入正式 test suite。
/// </summary>
public static class FluxQuickTest
{
    private const string MenuRoot = "Window/Flux Test/";

    [MenuItem(MenuRoot + "Compile & Run (float)", false, 0)]
    public static void TestCompileAndRun()
    {
        var config = new LexerConfig<float>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f', 'F')),
        };
        config.Operators.Add(new OperatorRule("+", (byte)FloatOp.Add));
        config.Operators.Add(new OperatorRule("-", (byte)FloatOp.Sub));
        config.Operators.Add(new OperatorRule("*", (byte)FloatOp.Mul));
        config.Operators.Add(new OperatorRule("/", (byte)FloatOp.Div));
        config.Brackets.Add(new BracketRule("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen));
        config.VariablePatterns.Add(new VariablePatternRule("[", "]"));

        var lexer    = new FluxLexer<float>(config);
        var lr       = lexer.Lex("([atk] + [def]) * 0.5f");
        var runner   = new FluxAssembler<float, FloatMathDef>(default);
        var formula  = runner.Compile(lr);
        var instance = runner.Instantiate(formula).Set("atk", 10f).Set("def", 5f);
        float result = instance.Run();

        Debug.Log($"QuickTest: formula = ([atk] + [def]) * 0.5f, atk=10, def=5 → {result}");
        EditorUtility.DisplayDialog("Flux Quick Test",
            $"([atk] + [def]) * 0.5f\natk=10, def=5\nResult: {result}\nExpected: 7.5",
            "OK");
    }

    [MenuItem(MenuRoot + "Load Formula Asset", false, 1)]
    public static void TestLoadAsset()
    {
        var guids = AssetDatabase.FindAssets("t:FluxAsset");
        if (guids.Length == 0)
        {
            Debug.LogWarning("No FluxAsset found in project. Create one with FluxAssetEditor first.");
            return;
        }

        foreach (var guid in guids)
        {
            var path  = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<FluxAsset>(path);
            var names = asset.VariableNames;
            Debug.Log($"FluxAsset: {asset.name} | Type: {asset.TypeId} | Instr: {asset.InstructionCount} | Size: {asset.RawDataLength}B | Vars: [{string.Join(", ", names)}] | Source: {asset.Source}");
        }
    }
}
