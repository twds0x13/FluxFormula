using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 项目级配置——所有硬编码常量的单一注入点。
    /// </summary>
    /// <remarks>
    /// <para>使用方式：应用启动时设置 <c>FluxConfig.Current = new FluxConfig { ... }</c>，
    /// 或通过 <c>FluxConfig.Set(new FluxConfig { ... })</c> 替换。</para>
    ///
    /// <para>未设置时使用 <see cref="Default"/>——各项值与 v1.5 硬编码常量完全一致。</para>
    /// </remarks>
    public class FluxConfig
    {
        /// <summary>出厂默认——各项与 v1.5 硬编码值一致。</summary>
        public static readonly FluxConfig Default = new()
        {
            FormulaCacheCapacity = 2048,
            MergeThreshold       = 8,
        };

        private static FluxConfig _current;

        /// <summary>当前生效的全局配置。未显式设置时返回 <see cref="Default"/>。</summary>
        public static FluxConfig Current
        {
            get => _current ?? Default;
            set => _current = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>替换当前配置——等价于 <c>Current = config</c>。</summary>
        public static void Set(FluxConfig config) => Current = config;

        // ═══════════════════════════════════════════════════════
        // 配置项
        // ═══════════════════════════════════════════════════════

        /// <summary><see cref="FormulaCache"/> 哈希表槽位数。默认 2048。</summary>
        public int FormulaCacheCapacity { get; init; }

        /// <summary>
        /// 链式公式合并阈值——链长超过此值时 <see cref="FluxFormula{TData, TOper}.ToAtomic"/>
        /// 合并为原子公式。默认 8。
        /// </summary>
        public int MergeThreshold { get; init; }

    }
}
}
