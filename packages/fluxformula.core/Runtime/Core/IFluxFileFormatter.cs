using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 最小文件持久化契约。Core 提供读写方法，消费者注入外部实现
    /// （如 <c>System.IO.File</c> 或 Unity <c>AssetDatabase</c>），
    /// 或直接使用内置的 <see cref="FileFluxFileFormatter"/>。
    /// </summary>
    /// <remarks>
    /// <para>使用模式：</para>
    /// <code>
    /// byte[] data = formula.ToBytes();                              // .ff
    /// byte[] data = VffFormat.ToBytes(links, overrides);            // .vff
    /// formatter.Save(data, FluxArtifactKind.Virtual, "path/to/chain");
    ///
    /// var loaded = formatter.Load("path/to/chain", out var kind);  // kind = Virtual
    /// var result = VffFormat.FromBytes&lt;float, FloatOp&gt;(loaded);  // 解析
    /// </code>
    /// <para>接口故意非泛型：调用方在传 <c>byte[]</c> 之前已完成序列化（或收到 <c>byte[]</c> 后自行反序列化），
    /// 接口只需关心"把字节存到哪"和"从哪读字节"。</para>
    /// </remarks>
    public interface IFluxFileFormatter
    {
        /// <summary>
        /// 将二进制产物持久化到指定路径。
        /// </summary>
        /// <param name="data">序列化后的字节码</param>
        /// <param name="kind">产物类型（.ff 或 .vff）</param>
        /// <param name="path">目标路径（文件系统路径或 Unity 项目相对路径）</param>
        void Save(byte[] data, FluxArtifactKind kind, string path);

        /// <summary>
        /// 从指定路径加载二进制产物的字节数据。
        /// </summary>
        /// <param name="path">源路径</param>
        /// <param name="kind">产物类型（通过 magic bytes 自动检测）</param>
        /// <returns>原始字节数据</returns>
        byte[] Load(string path, out FluxArtifactKind kind);
    }

    /// <summary>
    /// <see cref="IFluxFileFormatter"/> 的内置实现，基于 <c>System.IO.File</c>。
    /// 自动根据 <see cref="FluxArtifactKind"/> 附加 <c>.ff</c> / <c>.vff</c> 扩展名。
    /// </summary>
    /// <remarks>
    /// <para>路径处理：<c>Save("Damage", Formula)</c> → <c>Damage.ff</c>。
    /// 若路径已含对应扩展名则不重复。</para>
    /// <para>加载时通过 <c>VffFormat.IsVff()</c> 检测 magic bytes 自动推断 <see cref="FluxArtifactKind"/>。</para>
    /// </remarks>
    public sealed class FileFluxFileFormatter : IFluxFileFormatter
    {
        /// <inheritdoc />
        public void Save(byte[] data, FluxArtifactKind kind, string path)
        {
            string ext = kind == FluxArtifactKind.Formula ? ".ff" : ".vff";
            string fullPath = path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? path : path + ext;
            System.IO.File.WriteAllBytes(fullPath, data);
        }

        /// <inheritdoc />
        public byte[] Load(string path, out FluxArtifactKind kind)
        {
            byte[] data = System.IO.File.ReadAllBytes(path);
            kind = VffFormat.IsVff(new ReadOnlySpan<byte>(data))
                ? FluxArtifactKind.Virtual
                : FluxArtifactKind.Formula;
            return data;
        }
    }
}
