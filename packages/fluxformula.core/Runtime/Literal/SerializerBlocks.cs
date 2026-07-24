#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    /// <summary>
    /// 序列化器块注册表。跨程序集的中心注册点——SG 和外部代码均可通过
    /// <see cref="AddBlock{T}"/> / <see cref="AddBlocks"/> / <see cref="RemoveBlock{T}"/>
    /// 显式注册/移除 <see cref="ISerializerBlock{TData}"/> 实现。
    /// </summary>
    public static class SerializerBlocks
    {
        private static readonly Dictionary<Type, object> _blocks = new();
        private static readonly object _syncRoot = new();
        private static bool _initialized;

        /// <summary>
        /// 触发 <see cref="SerializerRegistry"/> 的类型初始化器，
        /// 使 SG 生成的静态构造函数执行注册，随后注册内置类型的 block。
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;
            RuntimeHelpers.RunClassConstructor(typeof(SerializerRegistry).TypeHandle);
            // 内置类型与用户类型走同一 AddBlock 路径
            AddBlock<float>(new SerializerRegistry.BuiltinBlock_Float());
            AddBlock<double>(new SerializerRegistry.BuiltinBlock_Double());
            AddBlock<int>(new SerializerRegistry.BuiltinBlock_Int());
            AddBlock<uint>(new SerializerRegistry.BuiltinBlock_Uint());
            AddBlock<long>(new SerializerRegistry.BuiltinBlock_Long());
            AddBlock<ulong>(new SerializerRegistry.BuiltinBlock_Ulong());
            AddBlock<short>(new SerializerRegistry.BuiltinBlock_Short());
            AddBlock<ushort>(new SerializerRegistry.BuiltinBlock_Ushort());
            AddBlock<byte>(new SerializerRegistry.BuiltinBlock_Byte());
            AddBlock<sbyte>(new SerializerRegistry.BuiltinBlock_Sbyte());
            AddBlock<bool>(new SerializerRegistry.BuiltinBlock_Bool());
            AddBlock<char>(new SerializerRegistry.BuiltinBlock_Char());
            AddBlock<string>(new SerializerRegistry.BuiltinBlock_String());
            _initialized = true;
        }

        /// <summary>
        /// 注册一个 block。返回 <see cref="Builder"/> 以支持流式链式调用。
        /// 对于 T 的多次注册，后注册覆盖先注册。
        /// </summary>
        public static Builder AddBlock<T>(ISerializerBlock<T> block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            lock (_syncRoot)
            {
                _blocks[typeof(T)] = block;
            }
            return new Builder();
        }

        /// <summary>
        /// 批量注册异构 block。每个 block 的泛型参数通过反射推导。
        /// </summary>
        public static void AddBlocks(params ISerializerBlock[] blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            lock (_syncRoot)
            {
                foreach (var block in blocks)
                {
                    if (block == null) continue;
                    foreach (var iface in block.GetType().GetInterfaces())
                    {
                        if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISerializerBlock<>))
                        {
                            _blocks[iface.GetGenericArguments()[0]] = block;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 移除指定类型的 block。类型未注册时静默成功。
        /// </summary>
        public static void RemoveBlock<T>()
        {
            lock (_syncRoot)
            {
                _blocks.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 查找指定类型的 <see cref="ISerializerBlock{TData}"/>。
        /// 未注册时返回 false 且 block 为 null。
        /// </summary>
        public static bool TryGet<TData>([NotNullWhen(true)] out ISerializerBlock<TData>? block)
        {
            EnsureInitialized();
            lock (_syncRoot)
            {
                if (_blocks.TryGetValue(typeof(TData), out var obj) && obj is ISerializerBlock<TData> b)
                {
                    block = b;
                    return true;
                }
            }
            block = null;
            return false;
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

        /// <summary>
        /// 流式注册构建器。由 <see cref="SerializerBlocks.AddBlock{T}"/> 返回，支持链式调用。
        /// </summary>
        public sealed class Builder
        {
            internal Builder() { }

            /// <inheritdoc cref="SerializerBlocks.AddBlock{T}"/>
            public Builder AddBlock<T>(ISerializerBlock<T> block)
            {
                SerializerBlocks.AddBlock<T>(block);
                return this;
            }

            /// <inheritdoc cref="SerializerBlocks.AddBlocks"/>
            public Builder AddBlocks(params ISerializerBlock[] blocks)
            {
                SerializerBlocks.AddBlocks(blocks);
                return this;
            }

            /// <inheritdoc cref="SerializerBlocks.RemoveBlock{T}"/>
            public Builder RemoveBlock<T>()
            {
                SerializerBlocks.RemoveBlock<T>();
                return this;
            }
        }
    }
}
