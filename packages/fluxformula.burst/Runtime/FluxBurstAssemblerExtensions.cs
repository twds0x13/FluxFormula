using FluxFormula.Core;

namespace FluxFormula.Burst
{
    /// <summary>
    /// <see cref="FluxAssembler{TData, TDef}"/> 扩展——便捷创建 Burst 求值器。
    /// </summary>
    public static class FluxBurstAssemblerExtensions
    {
        /// <summary>
        /// 为公式创建 Burst 兼容的求值器。
        /// </summary>
        /// <example>
        /// <code>
        /// var job = assembler.CreateBurstInstance(formula)
        ///     .Set("atk", 100f)
        ///     .Set("def", 50f);
        /// float damage = job.Run();
        /// job.Dispose();
        /// </code>
        /// </example>
        public static FluxBurstInstance<TData, TDef> CreateBurstInstance<TData, TDef>(
            this FluxAssembler<TData, TDef> assembler,
            FluxFormula<TData, TDef> formula)
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            return new FluxBurstInstance<TData, TDef>(formula);
        }
    }
}
