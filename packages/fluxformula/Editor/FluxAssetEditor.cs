using System;
using System.Collections.Generic;
using System.Linq;
using FluxFormula.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 公式资产编辑器基类——泛型驱动的文本编辑窗口。
/// 用户在项目中创建一行字的子类即可使用。
///
/// <code>
/// public class MyFormulaEditor : FluxAssetEditor&lt;float, FloatMathDef&gt;
/// {
///     [MenuItem("Window/My Game/Formula Editor")]
///     public static void Show() => GetWindow&lt;MyFormulaEditor&gt;("Formula Editor").Show();
/// }
/// </code>
/// </summary>
public class FluxAssetEditor<TData, TDef> : EditorWindow
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
{
    // ═══════════════════════════════════════════════
    // 窗口注册（通过 FluxEditorRegistry 非泛型类）
    // ═══════════════════════════════════════════════

    private void RegisterWindow()   => FluxEditorRegistry.Register(typeof(TDef).AssemblyQualifiedName, this, a => LoadAsset(a));
    private void UnregisterWindow() => FluxEditorRegistry.Unregister(typeof(TDef).AssemblyQualifiedName, this);

    // ═══════════════════════════════════════════════
    // 状态
    // ═══════════════════════════════════════════════

    private string                    _formulaText       = "";
    private string                    _currentAssetPath;                // 已加载资产的路径，null = 新公式
    private Vector2                   _scroll;
    private GUIStyle                  _textStyle;
    private byte[]                    _allOpers;                        // 扫描 GetOperatorName 得到的有效操作码
    private VariablePatternRule[]     _cachedPatterns    = Array.Empty<VariablePatternRule>();
    private int                       _patternsHash      = -1;

    // 语法解析规则（变量模式）
    private List<string>              _varPrefixes       = new();
    private List<string>              _varSuffixes       = new();

    // 运算符
    private List<string>              _opSymbols         = new();
    private List<byte>                _opOpers           = new();

    // 括号
    private List<string>              _brOpens           = new();
    private List<string>              _brCloses          = new();
    private List<byte>                _brLefts           = new();
    private List<byte>                _brRights          = new();

    // 隐式运算符
    private List<byte>                _implicitOpers     = new();

    // 编译结果
    private string                    _statusMessage     = "";
    private MessageType               _statusType        = MessageType.None;
    private int                       _lastInstructionCount;
    private int                       _lastDataLength;
    private string[]                  _lastVariableNames = Array.Empty<string>();

    // 折叠状态
    private bool                      _showGrammar       = true;
    private bool                      _showOperators;
    private bool                      _showBrackets;
    private bool                      _showImplicit;

    // 编辑缓冲：借壳临时 ScriptableObject，利用 Unity 原生 Undo 系统
    private FluxEditState             _editState;
    private SerializedObject          _editStateSO;
    private SerializedProperty        _editTextProp;
    private string                    _savedText         = ""; // 保存时的检查点文字

    // ═══════════════════════════════════════════════
    // 子类可覆写：提供 LexerConfig 初始默认值
    // ═══════════════════════════════════════════════

    /// <summary>在基类构建完默认 LexerConfig 后调用，子类可追加/修改。</summary>
    protected virtual void ConfigureLexerDefaults(LexerConfig<TData> config) { }

    // ═══════════════════════════════════════════════

    private void OnEnable()
    {
        RegisterWindow();
        minSize = new Vector2(720, 480);
        ScanOperators();
        LoadState();
        _patternsHash = -1;
        CreateEditState();
    }

    private void OnDisable()
    {
        UnregisterWindow();
        SaveState();
        DestroyEditState();
    }

    private void OnDestroy()
    {
        UnregisterWindow();
        SaveState();
        DestroyEditState();
    }

    private void CreateEditState()
    {
        if (_editState != null) return;
        _editState = CreateInstance<FluxEditState>();
        _editState.hideFlags = HideFlags.HideAndDontSave;
        _editState.formulaText = _formulaText ?? "";
        _editStateSO = new SerializedObject(_editState);
        _editTextProp = _editStateSO.FindProperty("formulaText");
    }

    private void DestroyEditState()
    {
        if (_editState != null)
        {
            DestroyImmediate(_editState);
            _editState = null;
        }
        _editStateSO = null;
        _editTextProp = null;
    }

    /// <summary>程序化修改文本后同步到编辑缓冲（New / Load / Format）</summary>
    private void SyncEditState(string text)
    {
        if (_editStateSO == null || _editTextProp == null) return;
        _editStateSO.Update();
        _editTextProp.stringValue = text ?? "";
        _editStateSO.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>扫描 TDef 中实现了 GetOperatorName 的有效操作码</summary>
    private void ScanOperators()
    {
        var def = default(TDef);
        var list = new List<byte>();
        for (int b = 0; b < 256; b++)
        {
            if (def.GetOperatorName((byte)b) != null)
                list.Add((byte)b);
        }
        _allOpers = list.ToArray();
    }

    /// <summary>操作码下拉选择器（若 TDef 未实现 GetOperatorName 则退化为 IntField）</summary>
    private byte DrawOpPopup(byte current, params GUILayoutOption[] options)
    {
        if (_allOpers.Length == 0)
            return (byte)EditorGUILayout.IntField((int)current, options);

        var def = default(TDef);
        int index = Array.IndexOf(_allOpers, current);
        if (index < 0) index = 0;

        var names = new string[_allOpers.Length];
        for (int i = 0; i < _allOpers.Length; i++)
            names[i] = def.GetOperatorName(_allOpers[i]) ?? _allOpers[i].ToString();

        int newIndex = EditorGUILayout.Popup(index, names, options);
        return _allOpers[newIndex];
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(4);
        DrawFormulaEditor();
        EditorGUILayout.Space(4);

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        DrawGrammarRules();
        DrawOperators();
        DrawBrackets();
        DrawImplicitOperators();
        EditorGUILayout.EndScrollView();

        DrawStatus();
    }


    // ═══════════════════════════════════════════════
    // 工具栏
    // ═══════════════════════════════════════════════

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(100)))
        {
            _formulaText = "";
            _currentAssetPath = null;
            _statusMessage = "";
            _statusType = MessageType.None;
            _lastInstructionCount = 0;
            _lastDataLength = 0;
            _lastVariableNames = Array.Empty<string>();
            _savedText  = "";
            SyncEditState("");
            GUI.FocusControl(null);
        }

        // 当前已加载资产名
        if (!string.IsNullOrEmpty(_currentAssetPath))
            EditorGUILayout.LabelField(System.IO.Path.GetFileName(_currentAssetPath), EditorStyles.miniLabel);

        GUILayout.FlexibleSpace();

        // Save（仅已加载资产时可用）
        if (!string.IsNullOrEmpty(_currentAssetPath))
        {
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(100)))
                CompileAndSave(_currentAssetPath);
        }

        // Save As
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Save As…", EditorStyles.toolbarButton, GUILayout.Width(100)))
            CompileAndSave();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
    }

    // ═══════════════════════════════════════════════
    // 公式文本
    // ═══════════════════════════════════════════════

    private void DrawFormulaEditor()
    {
        EditorGUILayout.LabelField("Formula Text", EditorStyles.boldLabel);

        var currentPatterns = GetCachedPatterns();
        if (currentPatterns.Length > 0)
        {
            EditorGUILayout.LabelField($"Variables: {PatternsPreview(currentPatterns)}", EditorStyles.miniLabel);
        }

        if (_textStyle == null)
            _textStyle = new GUIStyle(EditorStyles.textArea) { font = EditorStyles.standardFont, fontSize = 13, wordWrap = true };

        // 确保编辑缓冲已创建
        if (_editState == null) CreateEditState();

        var textAreaRect = EditorGUILayout.GetControlRect(
            GUILayout.Height(180), GUILayout.ExpandWidth(true));
        const string ctrlName = "formulaText";

        // 首次点击只转移焦点，阻止 Unity 默认全选
        bool wasFocused = GUI.GetNameOfFocusedControl() == ctrlName;
        if (!wasFocused
            && Event.current.type == EventType.MouseDown
            && textAreaRect.Contains(Event.current.mousePosition))
        {
            GUI.FocusControl(ctrlName);
            Event.current.Use();
        }

        GUI.SetNextControlName(ctrlName);

        // ══ 原生 Undo：通过 SerializedObject 借壳，Unity 自动管理 undo/redo ══
        _editStateSO.Update();
        EditorGUI.BeginChangeCheck();
        string newText = EditorGUI.TextArea(textAreaRect, _editTextProp.stringValue, _textStyle);
        if (EditorGUI.EndChangeCheck())
        {
            _editTextProp.stringValue = newText;
            _editStateSO.ApplyModifiedProperties(); // ← 记录 undo，自动隔离场景
            _formulaText = newText;
        }
        else
        {
            // 无变化时也同步（undo/redo 会通过 SerializedObject 回写）
            _formulaText = _editTextProp.stringValue;
        }
    }

    // ═══════════════════════════════════════════════
    // 语法解析规则
    // ═══════════════════════════════════════════════

    private void DrawGrammarRules()
    {
        _showGrammar = EditorGUILayout.BeginFoldoutHeaderGroup(_showGrammar,
            _varPrefixes.Count > 0
                ? $"Grammar Rules — Variable Patterns ({_varPrefixes.Count} defined)"
                : "Grammar Rules — Variable Patterns");

        if (!_showGrammar) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        EditorGUI.indentLevel++;

        if (_varPrefixes.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No variable patterns. Variables like [atk] won't be recognized.\n" +
                "Add a pattern: Prefix=[  Suffix=]  →  [atk] is captured as variable 'atk'.",
                MessageType.Info);
        }

        int removeAt = -1;
        for (int i = 0; i < _varPrefixes.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Prefix:", GUILayout.Width(100));
            _varPrefixes[i] = EditorGUILayout.TextField(_varPrefixes[i], GUILayout.Width(100));
            EditorGUILayout.LabelField("Suffix:", GUILayout.Width(100));
            _varSuffixes[i] = EditorGUILayout.TextField(_varSuffixes[i], GUILayout.Width(100));

            var suffixStr = string.IsNullOrEmpty(_varSuffixes[i]) ? "" : _varSuffixes[i];
            EditorGUILayout.LabelField($"→ {_varPrefixes[i]}name{suffixStr}", EditorStyles.miniLabel);

            if (GUILayout.Button("✕", GUILayout.Width(25)))
                removeAt = i;

            EditorGUILayout.EndHorizontal();
        }

        if (removeAt >= 0)
        {
            _varPrefixes.RemoveAt(removeAt);
            _varSuffixes.RemoveAt(removeAt);
        }

        if (GUILayout.Button("+ Add Pattern", GUILayout.Height(20)))
        {
            _varPrefixes.Add("");
            _varSuffixes.Add("");
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ═══════════════════════════════════════════════
    // 运算符
    // ═══════════════════════════════════════════════

    private void DrawOperators()
    {
        _showOperators = EditorGUILayout.BeginFoldoutHeaderGroup(_showOperators,
            $"Operators ({_opSymbols.Count} defined)");

        if (!_showOperators) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        EditorGUI.indentLevel++;

        int removeAt = -1;
        for (int i = 0; i < _opSymbols.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Symbol:", GUILayout.Width(100));
            _opSymbols[i] = EditorGUILayout.TextField(_opSymbols[i], GUILayout.Width(100));
            EditorGUILayout.LabelField("Operator:", GUILayout.Width(100));
            _opOpers[i] = DrawOpPopup(_opOpers[i], GUILayout.Width(100));

            if (GUILayout.Button("✕", GUILayout.Width(25)))
                removeAt = i;

            EditorGUILayout.EndHorizontal();
        }

        if (removeAt >= 0)
        {
            _opSymbols.RemoveAt(removeAt);
            _opOpers.RemoveAt(removeAt);
        }

        if (GUILayout.Button("+ Add Operator", GUILayout.Height(20)))
        {
            _opSymbols.Add("");
            _opOpers.Add(0);
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ═══════════════════════════════════════════════
    // 括号
    // ═══════════════════════════════════════════════

    private void DrawBrackets()
    {
        _showBrackets = EditorGUILayout.BeginFoldoutHeaderGroup(_showBrackets,
            $"Brackets ({_brOpens.Count} defined)");

        if (!_showBrackets) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        EditorGUI.indentLevel++;

        int removeAt = -1;
        for (int i = 0; i < _brOpens.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Open:", GUILayout.Width(100));
            _brOpens[i] = EditorGUILayout.TextField(_brOpens[i], GUILayout.Width(100));
            EditorGUILayout.LabelField("→ LeftOp:", GUILayout.Width(100));
            _brLefts[i] = DrawOpPopup(_brLefts[i], GUILayout.Width(100));

            EditorGUILayout.LabelField("Close:", GUILayout.Width(100));
            _brCloses[i] = EditorGUILayout.TextField(_brCloses[i], GUILayout.Width(100));
            EditorGUILayout.LabelField("→ RightOp:", GUILayout.Width(100));
            _brRights[i] = DrawOpPopup(_brRights[i], GUILayout.Width(100));

            if (GUILayout.Button("✕", GUILayout.Width(25)))
                removeAt = i;

            EditorGUILayout.EndHorizontal();
        }

        if (removeAt >= 0)
        {
            _brOpens.RemoveAt(removeAt);
            _brCloses.RemoveAt(removeAt);
            _brLefts.RemoveAt(removeAt);
            _brRights.RemoveAt(removeAt);
        }

        if (GUILayout.Button("+ Add Bracket", GUILayout.Height(20)))
        {
            _brOpens.Add("");
            _brCloses.Add("");
            _brLefts.Add(0);
            _brRights.Add(0);
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ═══════════════════════════════════════════════
    // 隐式运算符
    // ═══════════════════════════════════════════════

    private void DrawImplicitOperators()
    {
        var def = default(TDef);

        _showImplicit = EditorGUILayout.BeginFoldoutHeaderGroup(_showImplicit,
            _implicitOpers.Count > 0
                ? $"Implicit Operators ({string.Join(", ", _implicitOpers.Select(b => def.GetOperatorName(b) ?? b.ToString()))})"
                : "Implicit Operators (none)");

        if (!_showImplicit) { EditorGUILayout.EndFoldoutHeaderGroup(); return; }

        EditorGUI.indentLevel++;

        EditorGUILayout.HelpBox(
            "Implicit operators are auto-inserted at juxtaposition sites (e.g., 2(3) → 2*(3)).\n" +
            "Only ONE implicit operator is supported; more than one causes ambiguity errors.",
            MessageType.None);

        EditorGUILayout.LabelField("Check to auto-insert at juxtaposition sites:", EditorStyles.miniLabel);

        EditorGUI.indentLevel++;
        foreach (var op in _allOpers)
        {
            bool current = _implicitOpers.Contains(op);
            string name = def.GetOperatorName(op) ?? op.ToString();
            bool toggle = EditorGUILayout.Toggle(name, current);
            if (toggle && !current) _implicitOpers.Add(op);
            if (!toggle && current) _implicitOpers.Remove(op);
        }
        EditorGUI.indentLevel--;

        EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ═══════════════════════════════════════════════
    // 字面量配置
    // ═══════════════════════════════════════════════

    // ═══════════════════════════════════════════════
    // 状态栏
    // ═══════════════════════════════════════════════

    private void DrawStatus()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

        if (_lastInstructionCount > 0 || _lastVariableNames.Length > 0)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (_lastInstructionCount > 0)
            {
                EditorGUILayout.LabelField($"Instructions: {_lastInstructionCount}", GUILayout.Width(120));
                EditorGUILayout.LabelField($"Size: {_lastDataLength} B", GUILayout.Width(80));
            }
            EditorGUILayout.LabelField(
                _lastVariableNames.Length > 0
                    ? $"Variables: {string.Join(", ", _lastVariableNames)}"
                    : "(no variables)",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }

    // ═══════════════════════════════════════════════
    // 编译与保存
    // ═══════════════════════════════════════════════

    /// <summary>从 FluxAsset 加载配置到编辑器（双击资产或 Inspector 按钮调用）</summary>
    public void LoadAsset(FluxAsset asset)
    {
        _currentAssetPath = AssetDatabase.GetAssetPath(asset);
        _formulaText = asset.Source ?? "";

        var patterns = asset.VariablePatterns;
        _varPrefixes.Clear(); _varSuffixes.Clear();
        if (patterns.Length > 0)
        {
            foreach (var p in patterns)
            {
                _varPrefixes.Add(p.Prefix);
                _varSuffixes.Add(p.Suffix);
            }
        }
        _patternsHash = -1;

        _lastInstructionCount = asset.InstructionCount;
        _lastDataLength       = asset.RawDataLength;
        _lastVariableNames    = asset.VariableNames;
        _statusMessage = $"Loaded: {_lastInstructionCount} instr, {_lastVariableNames.Length} vars";
        _statusType    = MessageType.Info;

        _savedText  = _formulaText;
        SyncEditState(_formulaText);

        Repaint();
        EditorGUIUtility.PingObject(asset);
    }

    /// <summary>在运算符两侧添加空格，提升公式可读性。</summary>
    private void FormatFormulaText(LexerConfig<TData> config)
    {
        // 收集所有已知符号：运算符 + 括号开/闭（全部需要空格隔离）
        var symbols = new HashSet<string>();
        for (int i = 0; i < _opSymbols.Count; i++)
            if (!string.IsNullOrEmpty(_opSymbols[i]))
                symbols.Add(_opSymbols[i]);
        for (int i = 0; i < _brOpens.Count; i++)
            if (!string.IsNullOrEmpty(_brOpens[i]))
                symbols.Add(_brOpens[i]);
        for (int i = 0; i < _brCloses.Count; i++)
            if (!string.IsNullOrEmpty(_brCloses[i]))
                symbols.Add(_brCloses[i]);

        if (symbols.Count == 0) return;

        var text = _formulaText.Trim();
        var sb   = new System.Text.StringBuilder(text.Length * 2);
        int pos  = 0;

        while (pos < text.Length)
        {
            // 尝试匹配已知符号（长优先）
            string matched = null;
            foreach (var sym in symbols.OrderByDescending(s => s.Length))
            {
                if (pos + sym.Length <= text.Length
                    && string.CompareOrdinal(text, pos, sym, 0, sym.Length) == 0)
                {
                    matched = sym;
                    break;
                }
            }

            if (matched != null)
            {
                // 在符号前后加空格（如果前面不是空格/行首）
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');
                sb.Append(matched);
                sb.Append(' ');
                pos += matched.Length;
            }
            else
            {
                // 普通字符直接复制
                sb.Append(text[pos]);
                pos++;
            }
        }

        // 清理多余空白
        var formatted = System.Text.RegularExpressions.Regex.Replace(
            sb.ToString(), @"\s+", " ").Trim();

        // 如果没有启用隐式运算符，合并被空格隔开的相邻数字片段
        // 例如 "6 7" → "67", "3. 14" → "3.14"（循环处理 "6 7 8" → "67 8" → "678"）
        if (_implicitOpers.Count == 0)
        {
            string prevFmt;
            do
            {
                prevFmt = formatted;
                formatted = System.Text.RegularExpressions.Regex.Replace(
                    formatted, @"(\d[\d\.,]*)\s+([\d\.,]+)", "$1$2");
            } while (formatted != prevFmt);
        }

        if (formatted != _formulaText.Trim())
        {
            _formulaText = formatted;
            SyncEditState(formatted);
            GUI.FocusControl(null);
        }
    }

    private void CompileAndSave(string overwritePath = null)
    {
        _statusMessage     = "";
        _statusType        = MessageType.None;
        _lastInstructionCount = 0;
        _lastDataLength       = 0;
        _lastVariableNames    = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(_formulaText))
        {
            _statusMessage = "Formula text is empty.";
            _statusType    = MessageType.Warning;
            return;
        }

        try
        {
            var config = BuildLexerConfig();
            FormatFormulaText(config);
            var lexer  = new FluxLexer<TData>(config);
            var lr     = lexer.Lex(_formulaText.Trim());

            if (lr.Tokens.Length == 0)
            {
                _statusMessage = "Lexer produced no tokens. Check your formula and operator/bracket definitions.";
                _statusType    = MessageType.Error;
                return;
            }

            var def      = default(TDef);
            var runner   = new FluxAssembler<TData, TDef>(def);
            var formula  = runner.Compile(lr);

            var varPatterns = GetCachedPatterns();
            var lib   = FormulaLibrary.Create<TData, TDef>();
            var asset = lib.CreateAsset(formula, _formulaText.Trim(), varPatterns);

            // Save As 需要文件对话框
            string path = overwritePath;
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject(
                    "Save Flux Asset",
                    "NewFormula",
                    "asset",
                    "Save compiled formula as FluxAsset");
            }

            if (string.IsNullOrEmpty(path))
            {
                _statusMessage = "Save cancelled.";
                _statusType    = MessageType.Info;
                return;
            }

            // 覆写已有资产
            if (!string.IsNullOrEmpty(overwritePath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<FluxAsset>(path);
                if (existing != null)
                {
                    existing.SetRawData(formula, typeof(TDef).AssemblyQualifiedName, _formulaText.Trim(), varPatterns);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    asset = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(asset, path);
                }
                _currentAssetPath = path;
            }
            else
            {
                AssetDatabase.CreateAsset(asset, path);
                _currentAssetPath = path;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SaveState();

            _statusMessage        = $"Saved to {path}";
            _statusType           = MessageType.Info;
            _lastInstructionCount = formula.Count;
            _lastDataLength       = asset.RawDataLength;
            _lastVariableNames    = asset.VariableNames;
            _savedText  = _formulaText;

            EditorGUIUtility.PingObject(asset);
        }
        catch (Exception ex)
        {
            _statusMessage = ex is FormatException or InvalidOperationException
                ? ex.Message
                : $"Compilation error: {ex.Message}";
            _statusType = MessageType.Error;
            Debug.LogException(ex);
        }
    }

    // ═══════════════════════════════════════════════
    // LexerConfig 构建
    // ═══════════════════════════════════════════════

    private LexerConfig<TData> BuildLexerConfig()
    {
        var config = new LexerConfig<TData>
        {
            LiteralOper    = GetLiteralOper(),
            LiteralScanner = GetLiteralScanner(),
        };

        for (int i = 0; i < _opSymbols.Count; i++)
            config.Operators.Add(new OperatorRule(_opSymbols[i], _opOpers[i]));

        for (int i = 0; i < _brOpens.Count; i++)
            config.Brackets.Add(new BracketRule(_brOpens[i], _brCloses[i], _brLefts[i], _brRights[i]));

        for (int i = 0; i < _implicitOpers.Count; i++)
            config.ImplicitOperators.Add(_implicitOpers[i]);

        for (int i = 0; i < _varPrefixes.Count; i++)
            if (!string.IsNullOrEmpty(_varPrefixes[i]))
                config.VariablePatterns.Add(new VariablePatternRule(_varPrefixes[i], _varSuffixes[i]));

        ConfigureLexerDefaults(config);
        return config;
    }

    private byte GetLiteralOper()
    {
        // 扫描所有有效操作码，查找 Kind 为 Immediate 的
        var def = default(TDef);
        foreach (byte b in _allOpers)
        {
            if (def.GetKind(b) == OpType.Immediate)
                return b;
        }
        return 0;
    }

    private LiteralScanner<TData> GetLiteralScanner()
    {
        var t = typeof(TData);

        if (t == typeof(float))
            return LexerConfig<TData>.CreateDefaultNumberScanner(
                s => (TData)(object)float.Parse(((string)s).TrimEnd('f', 'F')));
        if (t == typeof(int))
            return LexerConfig<TData>.CreateDefaultNumberScanner(
                s => (TData)(object)int.Parse(s));
        if (t == typeof(double))
            return LexerConfig<TData>.CreateDefaultNumberScanner(
                s => (TData)(object)double.Parse(s));
        if (t == typeof(long))
            return LexerConfig<TData>.CreateDefaultNumberScanner(
                s => (TData)(object)long.Parse(s));

        return LexerConfig<TData>.CreateDefaultNumberScanner(s => default);
    }

    private VariablePatternRule[] GetCachedPatterns()
    {
        // 快速 hash：计算前缀/后缀字符串的哈希组合，跳过分配
        int h = _varPrefixes.Count;
        for (int i = 0; i < _varPrefixes.Count; i++)
            h = unchecked(h * 31 + (_varPrefixes[i]?.GetHashCode() ?? 0) + (_varSuffixes[i]?.GetHashCode() ?? 0));

        if (h == _patternsHash) return _cachedPatterns;
        _patternsHash = h;

        int count = 0;
        for (int i = 0; i < _varPrefixes.Count; i++)
            if (!string.IsNullOrEmpty(_varPrefixes[i])) count++;

        _cachedPatterns = new VariablePatternRule[count];
        int idx = 0;
        for (int i = 0; i < _varPrefixes.Count; i++)
            if (!string.IsNullOrEmpty(_varPrefixes[i]))
                _cachedPatterns[idx++] = new VariablePatternRule(_varPrefixes[i], _varSuffixes[i]);

        return _cachedPatterns;
    }

    private static string PatternsPreview(VariablePatternRule[] patterns)
    {
        if (patterns.Length == 0) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < patterns.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var s = patterns[i].Suffix;
            sb.Append(patterns[i].Prefix).Append("var").Append(string.IsNullOrEmpty(s) ? "" : s);
        }
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════
    // 状态持久化（EditorPrefs，按类型隔离）
    // ═══════════════════════════════════════════════

    private string PrefKey(string key) =>
        $"FluxAssetEditor.{typeof(TDef).Name}.{key}";

    private void SaveState()
    {
        SaveStringList("varPrefixes",   _varPrefixes);
        SaveStringList("varSuffixes",   _varSuffixes);
        SaveStringList("opSymbols",     _opSymbols);
        SaveOpList("opOpers",           _opOpers);
        SaveStringList("brOpens",       _brOpens);
        SaveStringList("brCloses",      _brCloses);
        SaveOpList("brLefts",           _brLefts);
        SaveOpList("brRights",          _brRights);
        SaveOpList("implicitOpers",     _implicitOpers);
    }

    private void LoadState()
    {
        // 从 EditorPrefs 恢复上次的配置
        _varPrefixes     = LoadStringList("varPrefixes");
        _varSuffixes     = LoadStringList("varSuffixes");
        _opSymbols       = LoadStringList("opSymbols");
        _opOpers         = LoadOpList("opOpers");
        _brOpens         = LoadStringList("brOpens");
        _brCloses        = LoadStringList("brCloses");
        _brLefts         = LoadOpList("brLefts");
        _brRights        = LoadOpList("brRights");
        _implicitOpers   = LoadOpList("implicitOpers");
    }

    private List<string> LoadStringList(string key)
    {
        var raw = EditorPrefs.GetString(PrefKey(key), "");
        return string.IsNullOrEmpty(raw)
            ? new List<string>()
            : new List<string>(raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private void SaveStringList(string key, List<string> list)
    {
        EditorPrefs.SetString(PrefKey(key), string.Join("\n", list));
    }

    private List<byte> LoadOpList(string key)
    {
        var raw = EditorPrefs.GetString(PrefKey(key), "");
        if (string.IsNullOrEmpty(raw)) return new List<byte>();
        var result = new List<byte>();
        foreach (var s in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (byte.TryParse(s, out var v))
                result.Add(v);
        }
        return result;
    }

    private void SaveOpList(string key, List<byte> list)
    {
        EditorPrefs.SetString(PrefKey(key),
            string.Join("\n", list.Select(b => b.ToString())));
    }
}

/// <summary>借壳 ScriptableObject，为 EditorWindow 提供原生 Undo 支持</summary>
internal class FluxEditState : ScriptableObject
{
    public string formulaText;
}
