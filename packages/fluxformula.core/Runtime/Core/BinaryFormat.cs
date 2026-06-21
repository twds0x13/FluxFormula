using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 小端序二进制读写原语——项目中所有字节级 I/O 的唯一实现点。
    /// </summary>
    /// <remarks>
    /// <para>替代此前散落在 <see cref="FluxFormula{TData, TOper}"/>、<see cref="DualHash64"/>、
    /// <see cref="FormulaFormat"/>、<see cref="VffFormat"/> 中的各套独立读写方法。</para>
    /// </remarks>
    public static class BinaryFormat
    {
        // ═══════════════════════════════════════════════════════
        // Read — ReadOnlySpan<byte> + ref offset
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32LE(ReadOnlySpan<byte> data, ref int offset)
        {
            int v = data[offset] | (data[offset + 1] << 8)
                  | (data[offset + 2] << 16) | (data[offset + 3] << 24);
            offset += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadInt64LE(ReadOnlySpan<byte> data, ref int offset)
        {
            long v = (long)data[offset]
                   | ((long)data[offset + 1] << 8)
                   | ((long)data[offset + 2] << 16)
                   | ((long)data[offset + 3] << 24)
                   | ((long)data[offset + 4] << 32)
                   | ((long)data[offset + 5] << 40)
                   | ((long)data[offset + 6] << 48)
                   | ((long)data[offset + 7] << 56);
            offset += 8;
            return v;
        }

        /// <summary>读取 16-bit 小端序无符号整数。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16LE(ReadOnlySpan<byte> data, ref int offset)
        {
            ushort v = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;
            return v;
        }

        // ═══════════════════════════════════════════════════════
        // Read — ReadOnlySpan<byte> + absolute offset (for DualHash64 style)
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64LE(ReadOnlySpan<byte> data, int offset)
        {
            return (ulong)data[offset]
                 | ((ulong)data[offset + 1] << 8)
                 | ((ulong)data[offset + 2] << 16)
                 | ((ulong)data[offset + 3] << 24)
                 | ((ulong)data[offset + 4] << 32)
                 | ((ulong)data[offset + 5] << 40)
                 | ((ulong)data[offset + 6] << 48)
                 | ((ulong)data[offset + 7] << 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32LE(ReadOnlySpan<byte> data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8)
                        | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        // ═══════════════════════════════════════════════════════
        // Write — byte[] + ref offset (for FluxFormula serialization)
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt32LE(byte[] buf, ref int offset, int value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt64LE(byte[] buf, ref int offset, long value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            buf[offset + 4] = (byte)(value >> 32);
            buf[offset + 5] = (byte)(value >> 40);
            buf[offset + 6] = (byte)(value >> 48);
            buf[offset + 7] = (byte)(value >> 56);
            offset += 8;
        }
    }
}
