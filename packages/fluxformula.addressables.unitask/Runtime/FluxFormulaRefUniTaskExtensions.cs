using System;
using Cysharp.Threading.Tasks;

namespace FluxFormula.Core
{
    /// <summary>
    /// FluxFormulaRef 的 UniTask 异步加载扩展。
    /// 仅当项目安装了 com.cysharp.unitask 时可用（此程序集 autoReferenced: false）。
    /// </summary>
    public static class FluxFormulaRefUniTaskExtensions
    {
        /// <summary>
        /// UniTask 版本的 LoadFormulaAsync。
        /// 内部委托到 ValueTask 版本后转为 UniTask，零额外分配。
        /// </summary>
        public static async UniTask<FluxFormula<TData, TOper>> LoadFormulaUniTaskAsync<TData, TOper, TDef>(
            this FluxFormulaRef<TData, TOper, TDef> reference)
            where TData : unmanaged
            where TOper : unmanaged, Enum
            where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
        {
            // await ValueTask → 解包为 T，async UniTask 自动包装为 UniTask<T>
            return await reference.LoadFormulaAsync();
        }

        /// <summary>
        /// UniTask 版本的 LoadAssetTypedAsync。
        /// </summary>
        public static async UniTask<FluxAsset> LoadAssetTypedUniTaskAsync<TData, TOper, TDef>(
            this FluxFormulaRef<TData, TOper, TDef> reference)
            where TData : unmanaged
            where TOper : unmanaged, Enum
            where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
        {
            return await reference.LoadAssetTypedAsync();
        }
    }
}
