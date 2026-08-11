#nullable enable

using System;
using System.Text;

namespace SourceSerializer
{
    /// <summary>
    /// 内置类型 <see cref="ISerializerBlock{T}"/> 实现。
    /// 13 个内置 C# 值类型各有一个 readonly struct，代理到
    /// <see cref="SerializerRegistry"/> 的静态 Scan_*/Emit_* 方法。
    /// 通过 <see cref="SerializerBlocks"/> 的 EnsureInitialized 统一注册。
    /// </summary>
    /// <remarks>
    /// BuiltinBlock_* 均为 2 行委托包装器——实际逻辑在 SerializerRegistry 中已全覆盖。
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class BuiltinBlocks
    {
        internal readonly struct BuiltinBlock_Float : ISerializerBlock<float>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out float value) =>
                SerializerRegistry.Scan_Float(text, pos, out value);
            public void Emit(StringBuilder sb, float value) =>
                SerializerRegistry.Emit_Float(sb, value);
        }

        internal readonly struct BuiltinBlock_Double : ISerializerBlock<double>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out double value) =>
                SerializerRegistry.Scan_Double(text, pos, out value);
            public void Emit(StringBuilder sb, double value) =>
                SerializerRegistry.Emit_Double(sb, value);
        }

        internal readonly struct BuiltinBlock_Int : ISerializerBlock<int>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out int value) =>
                SerializerRegistry.Scan_Int(text, pos, out value);
            public void Emit(StringBuilder sb, int value) =>
                SerializerRegistry.Emit_Int(sb, value);
        }

        internal readonly struct BuiltinBlock_Uint : ISerializerBlock<uint>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out uint value) =>
                SerializerRegistry.Scan_Uint(text, pos, out value);
            public void Emit(StringBuilder sb, uint value) =>
                SerializerRegistry.Emit_Uint(sb, value);
        }

        internal readonly struct BuiltinBlock_Long : ISerializerBlock<long>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out long value) =>
                SerializerRegistry.Scan_Long(text, pos, out value);
            public void Emit(StringBuilder sb, long value) =>
                SerializerRegistry.Emit_Long(sb, value);
        }

        internal readonly struct BuiltinBlock_Ulong : ISerializerBlock<ulong>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out ulong value) =>
                SerializerRegistry.Scan_Ulong(text, pos, out value);
            public void Emit(StringBuilder sb, ulong value) =>
                SerializerRegistry.Emit_Ulong(sb, value);
        }

        internal readonly struct BuiltinBlock_Short : ISerializerBlock<short>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out short value) =>
                SerializerRegistry.Scan_Short(text, pos, out value);
            public void Emit(StringBuilder sb, short value) =>
                SerializerRegistry.Emit_Short(sb, value);
        }

        internal readonly struct BuiltinBlock_Ushort : ISerializerBlock<ushort>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out ushort value) =>
                SerializerRegistry.Scan_Ushort(text, pos, out value);
            public void Emit(StringBuilder sb, ushort value) =>
                SerializerRegistry.Emit_Ushort(sb, value);
        }

        internal readonly struct BuiltinBlock_Byte : ISerializerBlock<byte>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out byte value) =>
                SerializerRegistry.Scan_Byte(text, pos, out value);
            public void Emit(StringBuilder sb, byte value) =>
                SerializerRegistry.Emit_Byte(sb, value);
        }

        internal readonly struct BuiltinBlock_Sbyte : ISerializerBlock<sbyte>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out sbyte value) =>
                SerializerRegistry.Scan_Sbyte(text, pos, out value);
            public void Emit(StringBuilder sb, sbyte value) =>
                SerializerRegistry.Emit_Sbyte(sb, value);
        }

        internal readonly struct BuiltinBlock_Bool : ISerializerBlock<bool>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out bool value) =>
                SerializerRegistry.Scan_Bool(text, pos, out value);
            public void Emit(StringBuilder sb, bool value) =>
                SerializerRegistry.Emit_Bool(sb, value);
        }

        internal readonly struct BuiltinBlock_Char : ISerializerBlock<char>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out char value) =>
                SerializerRegistry.Scan_Char(text, pos, out value);
            public void Emit(StringBuilder sb, char value) =>
                SerializerRegistry.Emit_Char(sb, value);
        }

        internal readonly struct BuiltinBlock_String : ISerializerBlock<string>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out string value) =>
                SerializerRegistry.Scan_String(text, pos, out value);
            public void Emit(StringBuilder sb, string value) =>
                SerializerRegistry.Emit_String(sb, value);
        }

        internal readonly struct BuiltinBlock_IntPtr : ISerializerBlock<IntPtr>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out IntPtr value) =>
                SerializerRegistry.Scan_IntPtr(text, pos, out value);
            public void Emit(StringBuilder sb, IntPtr value) =>
                SerializerRegistry.Emit_IntPtr(sb, value);
        }

        internal readonly struct BuiltinBlock_UIntPtr : ISerializerBlock<UIntPtr>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out UIntPtr value) =>
                SerializerRegistry.Scan_UIntPtr(text, pos, out value);
            public void Emit(StringBuilder sb, UIntPtr value) =>
                SerializerRegistry.Emit_UIntPtr(sb, value);
        }

        internal readonly struct BuiltinBlock_Guid : ISerializerBlock<Guid>
        {
            public int Scan(ReadOnlySpan<char> text, int pos, out Guid value) =>
                SerializerRegistry.Scan_Guid(text, pos, out value);
            public void Emit(StringBuilder sb, Guid value) =>
                SerializerRegistry.Emit_Guid(sb, value);
        }
    }
}
