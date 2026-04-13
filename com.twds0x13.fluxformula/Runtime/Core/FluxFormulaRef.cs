#if FLUX_ADDRESSABLES
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FluxFormula.Core
{
    /// <summary>
    /// 类型安全的公式资产引用，基于 Addressables。
    /// 在 MonoBehaviour / ScriptableObject 上序列化，运行时异步加载。
    ///
    /// <code>
    /// public class DamageCalc : MonoBehaviour
    /// {
    ///     public FluxFormulaRef&lt;float, FloatOp, FloatMathDef&gt; formula;
    ///
    ///     async void Start()
    ///     {
    ///         var f = await formula.LoadFormulaAsync();
    ///         float result = new FluxAssembler&lt;float, FloatOp, FloatMathDef&gt;(default)
    ///             .Instantiate(f).Set("atk", 10).Run();
    ///     }
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public class FluxFormulaRef<TData, TOper, TDef> : AssetReferenceT<FluxAsset>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        public FluxFormulaRef(string guid) : base(guid) { }

        /// <summary>
        /// 一步完成：Addressables 加载 → 类型校验 → FromBytes 反序列化。
        /// 类型不匹配时抛 InvalidOperationException。
        /// </summary>
        public async Task<FluxFormula<TData, TOper>> LoadFormulaAsync()
        {
            var asset = await LoadAssetTypedAsync();
            if (asset == null)
                return FluxFormula<TData, TOper>.Empty;

            if (asset.TypeId != typeof(TDef).AssemblyQualifiedName)
                throw new InvalidOperationException(
                    $"Formula type mismatch: asset '{asset.name}' is {asset.TypeId ?? "null"}, expected {typeof(TDef).Name}.");

            return asset.Load<TData, TOper>();
        }

        /// <summary>
        /// 仅加载 FluxAsset 并校验类型，不反序列化公式。
        /// 适用于想访问 Source / VariableNames 等元数据的场景。
        /// </summary>
        public async Task<FluxAsset> LoadAssetTypedAsync()
        {
            var asset = await base.LoadAssetAsync().Task;
            if (asset != null && asset.TypeId != typeof(TDef).AssemblyQualifiedName)
                throw new InvalidOperationException(
                    $"Formula type mismatch: asset '{asset.name}' is {asset.TypeId ?? "null"}, expected {typeof(TDef).Name}.");
            return asset;
        }
    }
}
#endif
