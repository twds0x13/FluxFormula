using System;
using System.Collections.Generic;
using System.Reflection;
using FluxFormula.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 非泛型窗口注册表——OnOpenAsset 与 Inspector 按钮共用入口。
/// </summary>
internal static class FluxEditorRegistry
{
    private static readonly Dictionary<string, (EditorWindow window, Action<FluxAsset> loader)> _openWindows = new();
    private static readonly Dictionary<string, Type> _windowTypes = new();

    // ═══════════════════════════════════════════════
    // 窗口生命周期注册（由 FluxAssetEditor.OnEnable/OnDisable 调用）
    // ═══════════════════════════════════════════════

    public static void Register(string typeId, EditorWindow window, Action<FluxAsset> loader)
    {
        _openWindows[typeId] = (window, loader);
        // 同时记录类型，以便后续自动打开
        if (!_windowTypes.ContainsKey(typeId))
            _windowTypes[typeId] = window.GetType();
    }

    public static void Unregister(string typeId, EditorWindow window)
    {
        if (_openWindows.TryGetValue(typeId, out var e) && e.window == window)
            _openWindows.Remove(typeId);
    }

    // ═══════════════════════════════════════════════
    // 打开资产（Inspector 按钮 / 双击文件）
    // ═══════════════════════════════════════════════

    public static bool TryOpen(string typeId, FluxAsset asset)
    {
        if (string.IsNullOrEmpty(typeId)) return false;

        // 已有窗口 → 直接加载
        if (_openWindows.TryGetValue(typeId, out var entry) && entry.window != null)
        {
            entry.window.Focus();
            entry.loader(asset);
            return true;
        }

        // 没有窗口 → 尝试自动创建
        if (TryCreateWindow(typeId, asset))
            return true;

        return false;
    }

    private static bool TryCreateWindow(string typeId, FluxAsset asset)
    {
        Type windowType = FindWindowType(typeId);
        if (windowType == null) return false;

        try
        {
            var window = EditorWindow.GetWindow(windowType);
            window.Show();
            // OnEnable 会调用 Register，然后我们加载资产
            if (_openWindows.TryGetValue(typeId, out var entry))
            {
                entry.loader(asset);
                return true;
            }
        }
        catch
        {
            Debug.LogWarning($"[FluxAsset] Failed to create editor window of type '{windowType.Name}'.");
        }

        return false;
    }

    private static Type FindWindowType(string typeId)
    {
        // 已知窗口类型（之前打开过）
        if (_windowTypes.TryGetValue(typeId, out var knownType))
            return knownType;

        // 扫描所有程序集中的 FluxAssetEditor 子类
        var baseType = typeof(FluxAssetEditor<,,>);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var t in types)
            {
                if (t.IsAbstract || t == baseType) continue;
                if (!IsSubclassOfRawGeneric(baseType, t)) continue;

                // 提取 TDef 类型参数
                var baseGeneric = GetGenericBaseType(baseType, t);
                if (baseGeneric == null) continue;

                var tDef = baseGeneric.GetGenericArguments()[2]; // TData, TOper, TDef
                if (tDef.AssemblyQualifiedName == typeId)
                {
                    _windowTypes[typeId] = t;
                    return t;
                }
            }
        }

        return null;
    }

    private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur) return true;
            toCheck = toCheck.BaseType;
        }
        return false;
    }

    private static Type GetGenericBaseType(Type generic, Type toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            if (toCheck.IsGenericType && toCheck.GetGenericTypeDefinition() == generic)
                return toCheck;
            toCheck = toCheck.BaseType;
        }
        return null;
    }
}
