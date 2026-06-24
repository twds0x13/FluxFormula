namespace FluxFormula.Core
{
    /// <summary>
    /// 最小持久化契约。Core 不执行文件 I/O——消费者注入外部保存器实现
    /// （如 <c>System.IO.File.WriteAllBytes</c> 或 Unity <c>AssetDatabase</c>）。
    /// </summary>
    /// <remarks>
    /// <para>使用模式：</para>
    /// <code>
    /// byte[] data = formula.ToBytes();                              // .ff
    /// byte[] data = VffFormat.ToBytes(links, overrides);            // .vff
    /// builder.Save(data, FluxArtifactKind.Virtual, "path/to/chain.vff");
    /// </code>
    /// <para>接口故意非泛型——调用方在传 <c>byte[]</c> 之前已完成序列化，
    /// 接口只需关心"把字节存到哪"。</para>
    /// </remarks>
    public interface IFluxBinaryBuilder
    {
        /// <summary>
        /// 将二进制产物持久化到指定路径。
        /// </summary>
        /// <param name="data">序列化后的字节码</param>
        /// <param name="kind">产物类型（.ff 或 .vff）</param>
        /// <param name="path">目标路径（文件系统路径或 Unity 项目相对路径）</param>
        void Save(byte[] data, FluxArtifactKind kind, string path);
    }
}
