namespace FluxFormula.Core
{
    /// <summary>
    /// 二进制产物类型。
    /// 映射到文件扩展名：<c>.ff</c>（<see cref="Formula"/>）和 <c>.vff</c>（<see cref="Virtual"/>）。
    /// </summary>
    /// <remarks>
    /// 用于 <see cref="IFluxFileFormatter"/> 的读写接口，使外部 saver 能够区分文件类型。
    /// </remarks>
    public enum FluxArtifactKind : byte
    {
        /// <summary>.ff — 公式字节码（<see cref="FluxFormula{TData, TOper}.ToBytes"/> 的产物）</summary>
        Formula = 0,

        /// <summary>.vff — 虚拟公式引用（<see cref="VffFormat.ToBytes{TData}"/> 的产物）</summary>
        Virtual = 1,
    }
}
