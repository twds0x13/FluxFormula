using System;
#if FLUX_ADDRESSABLES
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
#endif
using UnityEngine;

namespace FluxFormula.Core
{
    /// <summary>
    /// 公式库入口。泛型参数中 TDef 决定公式系统身份。
    /// </summary>
    public static class FormulaLibrary
    {
        public static FormulaLibrary<TData, TOper, TDef> Create<TData, TOper, TDef>()
            where TData : unmanaged
            where TOper : unmanaged, Enum
            where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
        {
            return new FormulaLibrary<TData, TOper, TDef>();
        }
    }

    /// <summary>
    /// 泛型公式库。类型 ID 自动从 TDef 派生——TDef 即公式系统身份。
    /// 负责创建 FluxAsset 和从 Addressables 加载。
    /// </summary>
    public class FormulaLibrary<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
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
            FluxFormula<TData, TOper> formula,
            string source = null,
            VariablePatternRule[] variablePatterns = null)
        {
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData(formula, _typeId, source, variablePatterns);
            return asset;
        }

        // ═══════════════════════════════════════════════
        // 加载（运行时，依赖 Addressables）
        // ═══════════════════════════════════════════════
#if FLUX_ADDRESSABLES

        public async Task<FluxFormula<TData, TOper>> LoadAsync(string key)
        {
            var asset = await Addressables.LoadAssetAsync<FluxAsset>(key).Task;
            if (asset.TypeId != _typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {_typeId}.");
            }
            return asset.Load<TData, TOper>();
        }

        public FluxFormula<TData, TOper> Load(string key)
        {
            var asset = Addressables.LoadAssetAsync<FluxAsset>(key).WaitForCompletion();
            if (asset.TypeId != _typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {_typeId}.");
            }
            return asset.Load<TData, TOper>();
        }
#endif
    }
}
