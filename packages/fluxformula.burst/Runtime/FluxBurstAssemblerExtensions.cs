using FluxFormula.Core;
using Unity.Jobs;

namespace FluxFormula.Burst
{
    /// <summary>
    /// <see cref="FluxAssembler{TData, TDef}"/> 扩展——便捷创建 Burst 求值器。
    /// </summary>
    public static class FluxBurstAssemblerExtensions
    {
        /// <summary>
        /// 为公式创建 Burst 兼容的求值器（独立字节码）。
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
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            return new FluxBurstInstance<TData, TDef>(formula);
        }

        /// <summary>
        /// 为公式创建 Burst 兼容的求值器（共享字节码缓存）。
        /// 同公式的多个实例复用 <paramref name="cache"/> 中的同一块 <see cref="NativeArray{Byte}"/>。
        /// </summary>
        /// <param name="assembler">汇编器（扩展目标）</param>
        /// <param name="formula">公式</param>
        /// <param name="cache">共享字节码缓存</param>
        /// <example>
        /// <code>
        /// var cache = new NativeBytecodeCache();
        /// for (int i = 0; i &lt; 100; i++)
        /// {
        ///     var job = assembler.CreateBurstInstance(formula, cache)
        ///         .SetIndex(0, inputs[i]);
        ///     handle = job.Schedule(handle);
        ///     // ... job.Dispose() 自动调 cache.Release()
        /// }
        /// cache.Dispose(); // 应用退出时
        /// </code>
        /// </example>
        public static FluxBurstInstance<TData, TDef> CreateBurstInstance<TData, TDef>(
            this FluxAssembler<TData, TDef> assembler,
            FluxFormula<TData, TDef> formula,
            INativeBytecodeCache cache)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            return new FluxBurstInstance<TData, TDef>(formula, cache);
        }

        /// <summary>
        /// 一步完成：创建 Burst 实例 → 设置变量 → 调度到 Job 系统。
        /// 返回的实例可通过 <see cref="FluxBurstInstance{TData, TDef}.Complete()"/> 等待完成，
        /// 通过 <see cref="FluxBurstInstance{TData, TDef}.Result"/> 读取结果。
        /// </summary>
        /// <param name="assembler">汇编器（扩展目标）</param>
        /// <param name="formula">公式</param>
        /// <param name="variables">变量名-值对（可选）</param>
        /// <example>
        /// <code>
        /// using var instance = assembler.ScheduleBurst(formula, ("atk", 100f), ("bonus", 50f));
        /// instance.Complete();
        /// float result = instance.Result;
        /// </code>
        /// </example>
        public static FluxBurstInstance<TData, TDef> ScheduleBurst<TData, TDef>(
            this FluxAssembler<TData, TDef> assembler,
            FluxFormula<TData, TDef> formula,
            params (string name, TData value)[] variables)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            var instance = new FluxBurstInstance<TData, TDef>(formula);
            if (variables != null)
            {
                foreach (var (name, value) in variables)
                    instance.Set(name, value);
            }
            instance.Schedule();
            return instance;
        }
    }
}
