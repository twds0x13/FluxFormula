using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// Blob 二进制文件格式定义与解析——Core 层，零 UnityEngine 依赖。
    /// </summary>
    /// <remarks>
    /// <para>文件布局：
    /// <code>
    /// Header (20B):
    ///   Magic "FLXB" (4) + Version(1) + Flags(1) + Reserved(2) +
    ///   EntryCount(4 LE) + BlobDataSize(4 LE) + BlobDataOffset(4 LE)
    ///
    /// Entry Table (EntryCount × 24B, 紧接 header):
    ///   XxHash64(8 LE) + FnvHash64(8 LE) + Offset(4 LE) + Length(4 LE)
    ///
    /// Blob Data (BlobDataSize bytes, 起始于 BlobDataOffset):
    ///   拼接后的公式字节码
    /// </code>
    /// </para>
    ///
    /// <para>Offset 字段相对于 Blob Data 段起始（即 <c>BlobDataOffset</c> 在文件中的位置）。
    /// 运行时 <c>ExtractBlobData</c> 将 data 段拷出后，entry offset 直接索引新数组。</para>
    /// </remarks>
    public static class BlobFormat
    {
        /// <summary>Magic: 'F' 'L' 'X' 'B' (LE: 0x42584C46)</summary>
        public const uint Magic = 0x42584C46;

        /// <summary>当前格式版本</summary>
        public const byte Version = 1;

        /// <summary>Header 固定大小（字节）</summary>
        public const int HeaderSize = 20;

        /// <summary>单条 entry 大小（字节）：DualHash64(16) + Offset(4) + Length(4)</summary>
        public const int EntrySize = 24;

        /// <summary>Flags: bit 0 = 压缩启用（Brotli）</summary>
        public const byte FlagCompressed = 1 << 0;

        // ═══════════════════════════════════════════════════════
        // 解析
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 解析 blob 文件 header。
        /// </summary>
        /// <returns>true 表示 header 有效；false 表示 magic 不匹配或数据不足</returns>
        public static bool TryParseHeader(
            ReadOnlySpan<byte> data,
            out int entryCount,
            out int blobDataOffset,
            out int blobDataSize,
            out bool compressed)
        {
            entryCount     = 0;
            blobDataOffset = 0;
            blobDataSize   = 0;
            compressed     = false;

            if (data.Length < HeaderSize)
                return false;

            if (data[0] != 'F' || data[1] != 'L' || data[2] != 'X' || data[3] != 'B')
                return false;

            byte flags    = data[5];
            entryCount     = (int)BinaryFormat.ReadUInt32LE(data, 8);
            blobDataSize   = (int)BinaryFormat.ReadUInt32LE(data, 12);
            blobDataOffset = (int)BinaryFormat.ReadUInt32LE(data, 16);
            compressed     = (flags & FlagCompressed) != 0;

            return true;
        }

        /// <summary>
        /// 从完整的 blob 文件字节中提取纯 blob data 段（去除 header 和 entry table）。
        /// 返回的新 byte[] 可直接传给 <c>FluxBlob.Load</c>——entry offset 索引此数组。
        /// </summary>
        public static byte[] ExtractBlobData(byte[] fileBytes)
        {
            if (!TryParseHeader(fileBytes, out int entryCount, out int blobDataOffset,
                    out int blobDataSize, out _))
                throw new InvalidOperationException("Invalid blob file: bad magic or truncated header.");

            if (blobDataOffset + blobDataSize > fileBytes.Length)
                throw new InvalidOperationException(
                    $"Truncated blob file: data section ({blobDataOffset}+{blobDataSize}) exceeds file size ({fileBytes.Length}).");

            byte[] blobData = new byte[blobDataSize];
            Buffer.BlockCopy(fileBytes, blobDataOffset, blobData, 0, blobDataSize);
            return blobData;
        }

        /// <summary>
        /// 从 blob header 中读取 entry table——返回 EntryCount 条 <see cref="BlobEntry"/>。
        /// </summary>
        public static BlobEntry[] ReadEntryTable(ReadOnlySpan<byte> data, int entryCount)
        {
            var entries = new BlobEntry[entryCount];
            int offset = HeaderSize;
            for (int i = 0; i < entryCount; i++)
            {
                ulong xxHash  = BinaryFormat.ReadUInt64LE(data, offset);
                ulong fnvHash = BinaryFormat.ReadUInt64LE(data, offset + 8);
                int entryOff  = (int)BinaryFormat.ReadUInt32LE(data, offset + 16);
                int entryLen  = (int)BinaryFormat.ReadUInt32LE(data, offset + 20);
                entries[i]    = new BlobEntry(new DualHash64(xxHash, fnvHash), entryOff, entryLen);
                offset += EntrySize;
            }
            return entries;
        }

        // ═══════════════════════════════════════════════════════
        // 写入（供 FluxBlobBuilder 使用）
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 将 header 写入 span 前 20 字节。
        /// </summary>
        public static void WriteHeader(
            Span<byte> dest,
            int entryCount,
            int blobDataSize,
            bool compressed)
        {
            dest[0] = (byte)'F';
            dest[1] = (byte)'L';
            dest[2] = (byte)'X';
            dest[3] = (byte)'B';
            dest[4] = Version;
            dest[5] = compressed ? FlagCompressed : (byte)0;
            dest[6] = 0; // reserved
            dest[7] = 0; // reserved

            int blobDataOffset = HeaderSize + entryCount * EntrySize;
            WriteInt32LE(dest, 8,  entryCount);
            WriteInt32LE(dest, 12, blobDataSize);
            WriteInt32LE(dest, 16, blobDataOffset);
        }

        /// <summary>
        /// 将单条 entry 写入 span 的 24 字节。
        /// </summary>
        public static void WriteEntry(
            Span<byte> dest,
            ulong xxHash64,
            ulong fnvHash64,
            int entryOffset,
            int length)
        {
            WriteInt64LE(dest, 0,  (long)xxHash64);
            WriteInt64LE(dest, 8,  (long)fnvHash64);
            WriteInt32LE(dest, 16, entryOffset);
            WriteInt32LE(dest, 20, length);
        }

        // ═══════════════════════════════════════════════════════
        // 内联 LE 写入（BinaryFormat 的 write 使用 byte[] + ref
        // offset，Span 版本在此内联）
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt32LE(Span<byte> dest, int offset, int value)
        {
            dest[offset]     = (byte)value;
            dest[offset + 1] = (byte)(value >> 8);
            dest[offset + 2] = (byte)(value >> 16);
            dest[offset + 3] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt64LE(Span<byte> dest, int offset, long value)
        {
            dest[offset]     = (byte)value;
            dest[offset + 1] = (byte)(value >> 8);
            dest[offset + 2] = (byte)(value >> 16);
            dest[offset + 3] = (byte)(value >> 24);
            dest[offset + 4] = (byte)(value >> 32);
            dest[offset + 5] = (byte)(value >> 40);
            dest[offset + 6] = (byte)(value >> 48);
            dest[offset + 7] = (byte)(value >> 56);
        }
    }
}
