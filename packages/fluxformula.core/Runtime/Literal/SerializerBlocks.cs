using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 序列化器块接口：将 scan（反序列化）和 emit（序列化）合并为一个双向能力。
    /// 每个标记了 [Template] 的类型在编译期由 SG 生成实现此接口的 struct。
    /// </summary>
    public interface ISerializerBlock<TData>
    {
        /// <summary>从 text 的 pos 位置开始扫描，写入 out value，返回新的位置。返回 pos 表示失败。</summary>
        int Scan(ReadOnlySpan<char> text, int pos, out TData value);

        /// <summary>将 value 序列化到 sb。</summary>
        void Emit(StringBuilder sb, TData value);
    }

    /// <summary>
    /// 序列化器块注册表。SG 在编译期为每个 [Template] 类型生成实现 <see cref="ISerializerBlock{TData}"/>
    /// 的 struct 并在此注册。
    /// </summary>
    internal static partial class SerializerBlocks
    {
        internal static bool TryGet<TData>([NotNullWhen(true)] out ISerializerBlock<TData>? block)
        {
            if (BlockRegistry<TData>.Instance != null)
            {
                block = BlockRegistry<TData>.Instance;
                return true;
            }
            block = null;
            return false;
        }

        internal static void Store<TData>(ISerializerBlock<TData> instance)
        {
            BlockRegistry<TData>.Instance = instance;
        }

        /// <summary>一行式序列化：将 value 转为字符串。</summary>
        public static string Serialize<TData>(TData value)
        {
            if (TryGet<TData>(out var block))
            {
                var sb = new StringBuilder();
                block.Emit(sb, value);
                return sb.ToString();
            }
            throw new InvalidOperationException($"No SerializerBlock registered for {typeof(TData).Name}. Add [Template] to the type.");
        }

        /// <summary>一行式反序列化：从字符串解析 TData。</summary>
        public static TData Deserialize<TData>(string text)
        {
            if (TryGet<TData>(out var block))
            {
                int r = block.Scan(text.AsSpan(), 0, out var value);
                if (r > 0) return value;
                throw new FormatException($"Failed to deserialize '{text}' as {typeof(TData).Name}.");
            }
            throw new InvalidOperationException($"No SerializerBlock registered for {typeof(TData).Name}. Add [Template] to the type.");
        }

        private static class BlockRegistry<TData>
        {
            public static ISerializerBlock<TData>? Instance;
        }
    }
}
