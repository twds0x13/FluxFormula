#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 接口分发的链式合并块。将多个 <see cref="ISerializerBlock{T}"/> 合并为一个：
    /// Scan 依次尝试所有链节，首个推进者胜出；Emit 通过 switch 匹配实际类型后仅对匹配的链节输出。
    /// </summary>
    /// <remarks>
    /// <para><b>不对称设计：</b>Scan 遍历 links 按注册顺序尝试（first-match-wins），
    /// 而 Emit 对 value 做 switch 匹配后仅调用对应链节。
    /// 两种 dispatch 策略不同是设计选择——Scan 需要探测未知类型（哪个 link 能成功），
    /// Emit 已知具体类型（直接匹配）。</para>
    /// <para>线程安全：链节列表的写入必须在持有 <see cref="SerializerBlocks"/> 锁的情况下进行，
    /// 且所有 AddBlock 调用应在任何 Serialize/Deserialize 调用前完成。</para>
    /// </remarks>
    internal sealed class ChainBlock<T> : ISerializerBlock<T>
    {
        private readonly List<ISerializerBlock<T>> _links = new();

        public void AddLink(ISerializerBlock<T> block) => _links.Add(block);

        public int Scan(ReadOnlySpan<char> text, int pos, out T value)
        {
            foreach (var link in _links)
            {
                int r = link.Scan(text, pos, out value);
                if (r > pos) return r;
            }
            value = default!;
            return pos;
        }

        public void Emit(StringBuilder sb, T value)
        {
            int before = sb.Length;
            foreach (var link in _links)
            {
                link.Emit(sb, value);
                if (sb.Length > before) return;
            }
        }
    }
}
