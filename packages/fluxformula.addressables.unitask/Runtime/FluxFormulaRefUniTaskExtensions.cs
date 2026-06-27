using System;
using Cysharp.Threading.Tasks;

// 此文件物理位于 fluxformula.addressables.unitask 包，namespace 声明为 FluxFormula.Core。
// C# 扩展方法发现依赖 using 指令: 目标类型在 Core 命名空间，同 namespace 保证自动可发现。
// 程序集引用由 asmdef (autoReferenced: false) 控制。
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
        public static async UniTask<FluxFormula<TData, TDef>> LoadFormulaUniTaskAsync<TData, TDef>(
            this FluxFormulaRef<TData, TDef> reference)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            // await ValueTask → 解包为 T，async UniTask 自动包装为 UniTask<T>
            return await reference.LoadFormulaAsync();
        }

        /// <summary>
        /// UniTask 版本的 LoadAssetTypedAsync。
        /// </summary>
        public static async UniTask<FluxAsset> LoadAssetTypedUniTaskAsync<TData, TDef>(
            this FluxFormulaRef<TData, TDef> reference)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            return await reference.LoadAssetTypedAsync();
        }
    }
}
