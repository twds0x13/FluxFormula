using FluxFormula.Core;
using Unity.Collections;

namespace FluxFormula.Burst
{
    /// <summary>
    /// Jobs 路径字节码缓存抽象——与 <see cref="FormulaCache"/> 的 <c>IFluxCacheProvider</c> 对位。
    /// 允许替换缓存策略（LRU 变体、测试 mock 等），同时保持 <see cref="FluxBurstInstance{TData, TDef}"/>
    /// 对具体实现无感知。
    /// </summary>
    public interface INativeBytecodeCache
    {
        /// <summary>
        /// 获取或创建 <paramref name="hash"/> 对应的共享 <see cref="NativeArray{Byte}"/>。
        /// </summary>
        /// <param name="hash">公式字节码的 DualHash64</param>
        /// <param name="source">字节码源数据（来自 <c>formula.ToBytes()</c>）。</param>
        /// <param name="isCached">true 表示返回的 NativeArray 由缓存管理——调用方 Dispose 时必须调用
        /// <see cref="Release"/>；false 表示缓存无法容纳（优雅溢出），调用方须自行 <c>Dispose</c>。</param>
        NativeArray<byte> Acquire(DualHash64 hash, byte[] source, out bool isCached);

        /// <summary>
        /// 释放一个引用。引用计数归零后该条目变为可驱逐状态。
        /// </summary>
        void Release(DualHash64 hash);
    }
}
