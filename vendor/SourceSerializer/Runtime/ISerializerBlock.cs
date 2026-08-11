#nullable enable

using System;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 非泛型标记接口：使 <see cref="ISerializerBlock{TData}"/> 可被 <c>params ISerializerBlock[]</c> 接收。
    /// </summary>
    public interface ISerializerBlock { }

    /// <summary>
    /// 序列化器块接口：将 scan（反序列化）和 emit（序列化）合并为一个双向能力。
    /// 每个标记了 [Template] 的类型在编译期由 SG 生成实现此接口的 struct。
    /// </summary>
    public interface ISerializerBlock<TData> : ISerializerBlock
    {
        /// <summary>从 text 的 pos 位置开始扫描，写入 out value，返回新的位置。返回 pos 表示失败。</summary>
        int Scan(ReadOnlySpan<char> text, int pos, out TData value);

        /// <summary>将 value 序列化到 sb。</summary>
        void Emit(StringBuilder sb, TData value);
    }
}
