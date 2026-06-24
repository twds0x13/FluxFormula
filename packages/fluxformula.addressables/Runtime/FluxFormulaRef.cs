using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FluxFormula.Core
{
    /// <summary>
    /// 类型安全的公式资产引用，基于 Addressables + ValueTask。
    /// 在 MonoBehaviour / ScriptableObject 上序列化，运行时零 GC 异步加载。
    ///
    /// <code>
    /// public class DamageCalc : MonoBehaviour
    /// {
    ///     public FluxFormulaRef&lt;float, FloatMathDef&gt; formula;
    ///
    ///     async ValueTask Start()
    ///     {
    ///         var f = await formula.LoadFormulaAsync();
    ///         float result = new FluxAssembler&lt;float, FloatMathDef&gt;(default)
    ///             .Instantiate(f).Set("atk", 10).Run();
    ///     }
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public class FluxFormulaRef<TData, TDef> : AssetReferenceT<FluxAsset>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        public FluxFormulaRef(string guid) : base(guid) { }

        /// <summary>
        /// 一步完成：Addressables 加载 → 类型校验 → FromBytes 反序列化。
        /// 类型不匹配时抛 InvalidOperationException。
        /// </summary>
        public async ValueTask<FluxFormula<TData, TDef>> LoadFormulaAsync()
        {
            var asset = await LoadAssetTypedAsync();
            if (asset == null)
                return FluxFormula<TData, TDef>.Empty;

            if (asset.TypeId != typeof(TDef).AssemblyQualifiedName)
                throw new InvalidOperationException(
                    $"Formula type mismatch: asset '{asset.name}' is {asset.TypeId ?? "null"}, expected {typeof(TDef).Name}.");

            return asset.Load<TData, TDef>();
        }

        /// <summary>
        /// 仅加载 FluxAsset 并校验类型，不反序列化公式。
        /// 适用于想访问 Source / VariableNames 等元数据的场景。
        /// </summary>
        public async ValueTask<FluxAsset> LoadAssetTypedAsync()
        {
            var asset = await LoadAssetAsync().Task;
            if (asset != null && asset.TypeId != typeof(TDef).AssemblyQualifiedName)
                throw new InvalidOperationException(
                    $"Formula type mismatch: asset '{asset.name}' is {asset.TypeId ?? "null"}, expected {typeof(TDef).Name}.");
            return asset;
        }
    }
}
