#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using System.Reflection;
using System.Text;

namespace SourceSerializer
{
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
            _initialized = true;

            // 1. Discover GeneratedSerializers.Init() in all loaded assemblies.
            //    Works for same-assembly (NuGet) and cross-assembly (Unity UPM) identically.
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType("SourceSerializer.GeneratedSerializers");
                    type?.GetMethod("Init",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                         ?.Invoke(null, null);
                }
                catch { }
            }

            // 2. Register built-in types (always)
            AddBlock<float>(new BuiltinBlocks.BuiltinBlock_Float());
            AddBlock<double>(new BuiltinBlocks.BuiltinBlock_Double());
            AddBlock<int>(new BuiltinBlocks.BuiltinBlock_Int());
            AddBlock<uint>(new BuiltinBlocks.BuiltinBlock_Uint());
            AddBlock<long>(new BuiltinBlocks.BuiltinBlock_Long());
            AddBlock<ulong>(new BuiltinBlocks.BuiltinBlock_Ulong());
            AddBlock<short>(new BuiltinBlocks.BuiltinBlock_Short());
            AddBlock<ushort>(new BuiltinBlocks.BuiltinBlock_Ushort());
            AddBlock<byte>(new BuiltinBlocks.BuiltinBlock_Byte());
            AddBlock<sbyte>(new BuiltinBlocks.BuiltinBlock_Sbyte());
            AddBlock<bool>(new BuiltinBlocks.BuiltinBlock_Bool());
            AddBlock<char>(new BuiltinBlocks.BuiltinBlock_Char());
            AddBlock<string>(new BuiltinBlocks.BuiltinBlock_String());
            AddBlock<IntPtr>(new BuiltinBlocks.BuiltinBlock_IntPtr());
            AddBlock<UIntPtr>(new BuiltinBlocks.BuiltinBlock_UIntPtr());
            AddBlock<Guid>(new BuiltinBlocks.BuiltinBlock_Guid());
        }

        /// <summary>
        /// 核心注册逻辑：接口类型做链式合并（后注册追加到分发链），非接口类型覆盖。
        /// </summary>
        private static void RegisterBlock<T>(ISerializerBlock<T> block)
        {
            lock (_syncRoot)
            {
                var key = typeof(T);
                if (key.IsInterface && _blocks.TryGetValue(key, out var existing))
                {
                    if (existing is ChainBlock<T> chain)
                    {
                        chain.AddLink(block);
                    }
                    else
                    {
                        var newChain = new ChainBlock<T>();
                        newChain.AddLink((ISerializerBlock<T>)existing);
                        newChain.AddLink(block);
                        _blocks[key] = newChain;
                    }
                }
                else
                {
                    _blocks[key] = block;
                }
            }
        }

        /// <summary>
        /// 注册一个 block。返回 <see cref="Builder"/> 以支持流式链式调用。
        /// 接口类型的多次注册做链式合并，非接口类型的后注册覆盖先注册。
        /// </summary>
        public static Builder AddBlock<T>(ISerializerBlock<T> block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            RegisterBlock(block);
            return new Builder();
        }

        /// <summary>
        /// 非泛型重载：通过 Type 和 block 实例注册。
        /// 用于动态加载的程序集——调用方在编译期不持有类型。
        /// 从 block 的 <see cref="ISerializerBlock{TData}"/> 接口提取泛型参数，
        /// 委托到 <see cref="RegisterBlock{T}"/> 以复用接口链式合并逻辑。
        /// </summary>
        public static Builder AddBlock(Type dataType, ISerializerBlock block)
        {
            if (dataType == null) throw new ArgumentNullException(nameof(dataType));
            if (block == null) throw new ArgumentNullException(nameof(block));

            foreach (var iface in block.GetType().GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISerializerBlock<>))
                {
                    var t = iface.GetGenericArguments()[0];
                    typeof(SerializerBlocks)
                        .GetMethod(nameof(RegisterBlock), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(t)
                        .Invoke(null, new object[] { block });
                    return new Builder();
                }
            }
            throw new ArgumentException("block does not implement ISerializerBlock<T>", nameof(block));
        }

        /// <summary>
        /// 批量注册异构 block。每个 block 的泛型参数通过反射推导，
        /// 委托到 <see cref="RegisterBlock{T}"/> 以复用接口链式合并逻辑。
        /// </summary>
        public static void AddBlocks(params ISerializerBlock[] blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            foreach (var block in blocks)
            {
                if (block == null) continue;
                foreach (var iface in block.GetType().GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISerializerBlock<>))
                    {
                        var t = iface.GetGenericArguments()[0];
                        typeof(SerializerBlocks)
                            .GetMethod(nameof(RegisterBlock), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(t)
                            .Invoke(null, new object[] { block });
                        break;
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
        /// 非泛型重载：通过 Type 移除 block。类型未注册时静默成功。
        /// </summary>
        public static void RemoveBlock(Type dataType)
        {
            if (dataType == null) throw new ArgumentNullException(nameof(dataType));
            lock (_syncRoot)
            {
                _blocks.Remove(dataType);
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
                using var compact = new WhitespaceStripper(text);
                int r = block.Scan(compact.Span, 0, out var value);
                if (r > 0) return value;
                throw new FormatException($"Failed to deserialize '{text}' as {typeof(TData).Name}.");
            }
            throw new InvalidOperationException($"No SerializerBlock registered for {typeof(TData).Name}. Add [Template] to the type.");
        }

        /// <summary>
        /// 扫描字符串文本为 TData。等价于 <see cref="Deserialize{TData}"/> 但不抛异常——
        /// 失败时返回 false。
        /// </summary>
        public static bool TryScan<TData>(string text, out TData value)
        {
            if (TryGet<TData>(out var block))
            {
                using var compact = new WhitespaceStripper(text);
                int r = block.Scan(compact.Span, 0, out value);
                if (r > 0) return true;
            }
            value = default!;
            return false;
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

            /// <inheritdoc cref="SerializerBlocks.AddBlock(Type, ISerializerBlock)"/>
            public Builder AddBlock(Type dataType, ISerializerBlock block)
            {
                SerializerBlocks.AddBlock(dataType, block);
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

            /// <inheritdoc cref="SerializerBlocks.RemoveBlock(Type)"/>
            public Builder RemoveBlock(Type dataType)
            {
                SerializerBlocks.RemoveBlock(dataType);
                return this;
            }
        }
    }
}
