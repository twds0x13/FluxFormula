namespace FluxFormula.Core
{
    /// <summary>
    /// 公式 blob 注册表——由 source generator 生成的 <c>BlobRegistry</c> 实现。
    /// </summary>
    /// <remarks>
    /// <para>每个 mod 程序集中有一个 internal 实现。不同程序集的实现互不冲突。</para>
    ///
    /// <para>运行时通过 <c>FluxBlobScanner</c> 反射扫描实现此接口的类型，
    /// 自动发现已加载 mod 的公式注册表并加载对应 blob 数据。</para>
    ///
    /// <para>Source generator 产出示例：
    /// <code>
    /// internal sealed class BlobRegistry : IFluxBlobRegistry
    /// {
    ///     public int EntryCount => 42;
    ///     public string BlobKey => "base_game_blob";
    ///     public BlobEntry[] GetEntries() => _entries;
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public interface IFluxBlobRegistry
    {
        /// <summary>注册表中的公式条目数。0 表示空 mod（无公式）。</summary>
        int EntryCount { get; }

        /// <summary>
        /// 对应 blob 二进制文件的加载 key。
        /// 优先作为 Addressables key 使用；若 Addressables 不可用则作为文件路径回退。
        /// </summary>
        string BlobKey { get; }

        /// <summary>获取编译期固化的偏移表条目（DualHash64 → offset, length）。</summary>
        BlobEntry[] GetEntries();
    }
}
