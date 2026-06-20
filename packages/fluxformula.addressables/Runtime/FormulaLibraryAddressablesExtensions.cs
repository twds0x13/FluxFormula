using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FluxFormula.Core
{
    /// <summary>
    /// FormulaLibrary 的 Addressables + ValueTask 加载扩展。
    /// 由 FluxFormula.Addressables 程序集提供——仅当项目安装了
    /// com.unity.addressables 时才可用。
    /// </summary>
    public static class FormulaLibraryAddressablesExtensions
    {
        /// <summary>
        /// 通过 Addressables key 异步加载公式（ValueTask，零 Task 分配）。
        /// 自动校验类型 ID——不匹配时释放资产并抛 InvalidOperationException。
        /// </summary>
        public static async ValueTask<FluxFormula<TData, TOper>> LoadAsync<TData, TOper, TDef>(
            this FormulaLibrary<TData, TOper, TDef> library,
            string key)
            where TData : unmanaged
            where TOper : unmanaged, Enum
            where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
        {
            string typeId = typeof(TDef).AssemblyQualifiedName;

            var asset = await Addressables.LoadAssetAsync<FluxAsset>(key).Task;

            if (asset.TypeId != typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {typeId}.");
            }

            return asset.Load<TData, TOper>();
        }

        /// <summary>
        /// 通过 Addressables key 同步加载公式。
        /// 仅建议在 Editor 或确实需要同步等待的场景使用。
        /// </summary>
        public static FluxFormula<TData, TOper> Load<TData, TOper, TDef>(
            this FormulaLibrary<TData, TOper, TDef> library,
            string key)
            where TData : unmanaged
            where TOper : unmanaged, Enum
            where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
        {
            string typeId = typeof(TDef).AssemblyQualifiedName;

            var asset = Addressables.LoadAssetAsync<FluxAsset>(key).WaitForCompletion();
            if (asset.TypeId != typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {typeId}.");
            }

            return asset.Load<TData, TOper>();
        }
    }
}
