using System;
using UnityEngine;

namespace FluxFormula.Core
{
    /// <summary>
    /// 公式库入口。泛型参数中 TDef 决定公式系统身份。
    /// </summary>
    public static class FormulaLibrary
    {
        public static FormulaLibrary<TData, TDef> Create<TData, TDef>()
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            return new FormulaLibrary<TData, TDef>();
        }
    }

    /// <summary>
    /// 泛型公式库。类型 ID 自动从 TDef 派生——TDef 即公式系统身份。
    /// 负责创建 FluxAsset。Addressables 加载功能由 FluxFormula.Addressables 程序集提供。
    /// </summary>
    public class FormulaLibrary<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        private readonly string _typeId;

        public FormulaLibrary()
        {
            _typeId = typeof(TDef).AssemblyQualifiedName;
        }

        // ═══════════════════════════════════════════════
        // 创建资产（编辑器）
        // ═══════════════════════════════════════════════

        public FluxAsset CreateAsset(
            FluxFormula<TData, TDef> formula,
            string source = null,
            VariablePatternRule[] variablePatterns = null)
        {
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData(formula, _typeId, source, variablePatterns);
            return asset;
        }

    }
}
