// Partial class — the source generator emits scanner methods
// and registration into this class.
namespace FluxFormula.Core
{
    internal static partial class LiteralScanners
    {
        /// <summary>
        /// 尝试获取 TData 的生成式字面量扫描器。
        /// 若 struct 上存在 [LiteralTemplate] 且 source generator 已生成对应扫描器则返回 true；
        /// 否则返回 false，调用方应回退到 <see cref="LexerConfig{TData}.LiteralScanner"/>。
        /// </summary>
        internal static bool TryGetScanner<TData>(out LiteralScanner<TData> scanner)
            where TData : unmanaged
        {
            if (ScannerRegistry<TData>.Scanner != null)
            {
                scanner = ScannerRegistry<TData>.Scanner;
                return true;
            }
            scanner = null;
            return false;
        }

        /// <summary>每个 TData 类型一个注册项。source generator 在静态构造中填充。</summary>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private static class ScannerRegistry<TData>
            where TData : unmanaged
        {
            // Assigned by source-generated static constructor when [LiteralTemplate] is present
#pragma warning disable CS0649
            public static LiteralScanner<TData> Scanner;
#pragma warning restore CS0649
        }
    }
}
