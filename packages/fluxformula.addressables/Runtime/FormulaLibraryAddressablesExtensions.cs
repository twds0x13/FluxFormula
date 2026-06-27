using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

// 此文件物理位于 fluxformula.addressables 包，namespace 声明为 FluxFormula.Core。
// C# 扩展方法发现依赖 using 指令: 目标类型在 Core 命名空间，同 namespace 保证自动可发现。
// 程序集引用由 asmdef (autoReferenced: false) 控制。
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
        public static async ValueTask<FluxFormula<TData, TDef>> LoadAsync<TData, TDef>(
            this FormulaLibrary<TData, TDef> library,
            string key)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            string typeId = typeof(TDef).AssemblyQualifiedName;

            var asset = await Addressables.LoadAssetAsync<FluxAsset>(key).Task;

            if (asset.TypeId != typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {typeId}.");
            }

            return asset.Load<TData, TDef>();
        }

        /// <summary>
        /// 通过 Addressables key 同步加载公式。
        /// 仅建议在 Editor 或确实需要同步等待的场景使用。
        /// </summary>
        public static FluxFormula<TData, TDef> Load<TData, TDef>(
            this FormulaLibrary<TData, TDef> library,
            string key)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            string typeId = typeof(TDef).AssemblyQualifiedName;

            var asset = Addressables.LoadAssetAsync<FluxAsset>(key).WaitForCompletion();
            if (asset.TypeId != typeId)
            {
                Addressables.Release(asset);
                throw new InvalidOperationException(
                    $"Formula '{key}': type mismatch. Asset: {asset.TypeId}, Expected: {typeId}.");
            }

            return asset.Load<TData, TDef>();
        }
    }
}
