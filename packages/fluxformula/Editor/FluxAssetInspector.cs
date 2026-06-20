using FluxFormula.Core;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// FluxAsset 自定义 Inspector。
/// 从字节码实时读取元数据，不做额外反序列化。
/// </summary>
[CustomEditor(typeof(FluxAsset))]
public class FluxAssetInspector : Editor
{
    [OnOpenAsset(1)]
    private static bool OnOpenAsset(int instanceID, int line)
    {
        var asset = EditorUtility.InstanceIDToObject(instanceID) as FluxAsset;
        if (asset == null) return false;
        return FluxEditorRegistry.TryOpen(asset.TypeId, asset);
    }
    private bool _showBytecode;

    public override void OnInspectorGUI()
    {
        var asset = (FluxAsset)target;

        EditorGUILayout.Space(4);

        // ── 身份 ──
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("Type ID", asset.TypeId ?? "(null)");
        EditorGUILayout.LabelField("Format Version", asset.FormatVersion ?? "(null)");
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);

        // ── 字节码 ──
        EditorGUILayout.LabelField("Bytecode", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("Data Size", $"{asset.RawDataLength:N0} bytes");
        EditorGUILayout.LabelField("Instruction Count", asset.InstructionCount.ToString());
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);

        // ── 变量 ──
        var varNames = asset.VariableNames;
        EditorGUILayout.LabelField($"Variables ({varNames.Length})", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        if (varNames.Length == 0)
        {
            EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var name in varNames)
                EditorGUILayout.LabelField(name, EditorStyles.miniLabel);
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);

        // ── 语法规则 ──
        var patterns = asset.VariablePatterns;
        EditorGUILayout.LabelField($"Grammar Rules ({patterns.Length})", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        if (patterns.Length == 0)
        {
            EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var p in patterns)
            {
                var suffixStr = string.IsNullOrEmpty(p.Suffix) ? "" : p.Suffix;
                EditorGUILayout.LabelField($"→ {p.Prefix}name{suffixStr}", EditorStyles.miniLabel);
            }
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);

        // ── 源码 ──
        if (!string.IsNullOrEmpty(asset.Source))
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.TextArea(asset.Source, GUILayout.Height(40));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);

        // ── 字节码展开 ──
        _showBytecode = EditorGUILayout.BeginFoldoutHeaderGroup(_showBytecode, "Raw Bytecode (hex)");
        if (_showBytecode)
        {
            if (asset.RawData != null && asset.RawData.Length > 0)
                DrawHexDump(asset.RawData);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);

        // ── 操作按钮 ──
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open in Formula Editor"))
        {
            if (!FluxEditorRegistry.TryOpen(asset.TypeId, asset))
                EditorUtility.DisplayDialog("FluxAsset",
                    $"No formula editor window is open for type:\n{asset.TypeId}\n\nOpen your editor first (Window menu).",
                    "OK");
        }
        if (GUILayout.Button("Copy Type ID"))
            GUIUtility.systemCopyBuffer = asset.TypeId;
        if (GUILayout.Button("Copy Source"))
            GUIUtility.systemCopyBuffer = asset.Source ?? "";
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawHexDump(byte[] data)
    {
        int perRow = 16;
        int rows = (data.Length + perRow - 1) / perRow;
        for (int r = 0; r < rows; r++)
        {
            int offset = r * perRow;
            int count = System.Math.Min(perRow, data.Length - offset);

            var hex = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
                hex.AppendFormat("{0:X2} ", data[offset + i]);

            EditorGUILayout.LabelField(
                $"{offset:X4}:  {hex}",
                EditorStyles.miniLabel);
        }
    }
}
