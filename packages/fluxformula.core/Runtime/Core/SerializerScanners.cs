// Source Generator 注入目标，partial class 的生成部分位于 SerializerScanners.g.cs。
// 每个标记了 [Template] 的 struct 在编译期生成对应的 Scan_Xxx 方法并注册委托。
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 用户定义 struct 的生成式字面量扫描器注册表。
    /// source generator 在编译期为每个标记 <see cref="TemplateAttribute"/> 的
    /// unmanaged struct 生成专用 span 扫描方法并注册到 <see cref="ScannerRegistry{TData}"/>。
    /// </summary>
    internal static partial class SerializerScanners
    {
        /// <summary>
        /// 尝试获取 TData 的生成式字面量扫描器。
        /// 若 struct 上存在 [Template] 且 source generator 已生成对应扫描器则返回 true；
        /// 否则返回 false。
        /// </summary>
        internal static bool TryGetScanner<TData>(out ScannerDelegate<TData> scanner)
        {
            if (ScannerRegistry<TData>.Scanner != null)
            {
                scanner = ScannerRegistry<TData>.Scanner;
                return true;
            }
            scanner = null;
            return false;
        }

        // ═══════════════════════════════════════════════════════
        // 委托注册
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 每个 TData 类型一个注册项。source generator 生成的静态构造器在编译期填充此字段。
        /// </summary>
        // SG 生成代码通过静态构造器赋值，运行时从不直接调用
        [ExcludeFromCodeCoverage]
        private static class ScannerRegistry<TData>
        {
            // Assigned by source-generated static constructor when [Template] is present
#pragma warning disable CS0649
            public static ScannerDelegate<TData> Scanner;
#pragma warning restore CS0649
        }
    }

    /// <summary>
    /// Span 扫描器委托签名。成功返回大于 pos 的新位置，失败返回 pos 不变。
    /// </summary>
    public delegate int ScannerDelegate<T>(ReadOnlySpan<char> src, int pos, out T value);

    /// <summary>
    /// 序列化委托签名。将值写入 StringBuilder。序列化永不失败。
    /// </summary>
    public delegate void EmitterDelegate<T>(StringBuilder sb, T value);
}
