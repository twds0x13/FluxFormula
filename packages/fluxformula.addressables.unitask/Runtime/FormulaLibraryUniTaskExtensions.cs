using System;
using Cysharp.Threading.Tasks;

// 此文件物理位于 fluxformula.addressables.unitask 包，namespace 声明为 FluxFormula.Core。
// C# 扩展方法发现依赖 using 指令: 目标类型在 Core 命名空间，同 namespace 保证自动可发现。
// 程序集引用由 asmdef (autoReferenced: false) 控制。
namespace FluxFormula.Core
{
    /// <summary>
    /// FormulaLibrary 的 UniTask 异步加载扩展。
    /// 仅当项目安装了 com.cysharp.unitask 时可用（此程序集 autoReferenced: false）。
    /// </summary>
    public static class FormulaLibraryUniTaskExtensions
    {
        /// <summary>
        /// UniTask 版本的 LoadAsync。
        /// 内部委托到 ValueTask 版本后转为 UniTask。
        /// </summary>
        public static async UniTask<FluxFormula<TData, TDef>> LoadAsyncUniTask<TData, TDef>(
            this FormulaLibrary<TData, TDef> library,
            string key)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            return await library.LoadAsync<TData, TDef>(key);
        }
    }
}
