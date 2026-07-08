using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 项目级配置：所有硬编码常量的单一注入点。
    /// </summary>
    /// <remarks>
    /// <para>使用方式：应用启动时设置 <c>FluxConfig.Current = new FluxConfig { ... }</c>，
    /// 或通过 <c>FluxConfig.Set(new FluxConfig { ... })</c> 替换。</para>
    ///
    /// <para>未设置时使用 <see cref="Default"/>，各项值与 v1.5 硬编码常量完全一致。</para>
    /// </remarks>
    public class FluxConfig
    {
        /// <summary>出厂默认，各项与 v1.5 硬编码值一致。</summary>
        public static readonly FluxConfig Default = new()
        {
            FormulaCacheCapacity          = 256,
            NativeBytecodeCacheCapacity   = 256,
            MergeThreshold                = 8,
            BlobFilePath                  = null,
            DiskCacheDirectory            = null,
            CompressBlob                  = false,
        };

        private static FluxConfig _current;

        /// <summary>当前生效的全局配置。未显式设置时返回 <see cref="Default"/>。</summary>
        public static FluxConfig Current
        {
            get => _current ?? Default;
            set => _current = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>替换当前配置，等价于 <c>Current = config</c>。</summary>
        public static void Set(FluxConfig config) => Current = config;

        // ═══════════════════════════════════════════════════════
        // 配置项
        // ═══════════════════════════════════════════════════════

        /// <summary><see cref="FormulaCache"/> 哈希表槽位数。默认 256。</summary>
        public int FormulaCacheCapacity { get; init; }

        /// <summary><see cref="NativeBytecodeCache"/> 哈希表槽位数。默认 256。</summary>
        /// <remarks>
        /// Jobs 路径中唯一公式种类数通常远小于实例数（~10 种公式 × N 实例）。
        /// 64 槽位覆盖绝大多数使用场景；极端情况可通过 <see cref="Set"/> 调整。
        /// </remarks>
        public int NativeBytecodeCacheCapacity { get; init; }

        /// <summary>
        /// 链式公式合并阈值：链长超过此值时 <see cref="FluxChain{TData, TDef}.ToAtomic"/>
        /// 合并为原子公式。默认 8。
        /// </summary>
        public int MergeThreshold { get; init; }

        /// <summary>
        /// Blob 构建时是否启用 Brotli 压缩。默认 false（向后兼容）。
        /// 压缩在构建时发生（<c>FluxBlobBuilder</c>），加载时自动解压（<c>FluxBlob.Initialize</c>）。
        /// 不影响运行时执行性能，解压仅在 blob 初始化时发生一次。
        /// </summary>
        public bool CompressBlob { get; init; }

        // ═══════════════════════════════════════════════════════
        // 文件与路径
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Blob 二进制文件路径。null 或空字符串使用默认路径
        /// (<c>StreamingAssets/flux.bytes</c>)。
        /// </summary>
        public string BlobFilePath { get; init; }

        /// <summary>
        /// 磁盘缓存目录：编译产物 / 中间文件的持久化路径。
        /// null 或空字符串使用 <c>Application.persistentDataPath</c>。
        /// </summary>
        public string DiskCacheDirectory { get; init; }

    }
}
