using System;

namespace FluxFormula.BlobRegistry.Generator
{
    /// <summary>
    /// 零依赖 blob 二进制文件解析器——供 source generator 使用。
    /// 不能依赖 FluxFormula.Core 类型，仅使用原始字节读取。
    /// </summary>
    internal static class BinaryBlobReader
    {
        // Header layout (matches BlobFormat in Core)
        internal const int HeaderSize = 20;
        internal const int EntrySize = 24;

        internal static readonly byte[] ExpectedMagic = { (byte)'F', (byte)'L', (byte)'X', (byte)'B' };

        internal static (BlobHeader header, BlobEntry[] entries) Read(byte[] data)
        {
            if (data.Length < HeaderSize)
                throw new InvalidOperationException("Blob file too small for header.");

            for (int i = 0; i < 4; i++)
            {
                if (data[i] != ExpectedMagic[i])
                    throw new InvalidOperationException($"Invalid blob magic at offset {i}: expected 0x{ExpectedMagic[i]:X2}, got 0x{data[i]:X2}.");
            }

            var header = new BlobHeader
            {
                Version         = data[4],
                Compressed      = (data[5] & 1) != 0,
                EntryCount      = (int)ReadU32LE(data, 8),
                BlobDataSize    = (int)ReadU32LE(data, 12),
                BlobDataOffset  = (int)ReadU32LE(data, 16),
            };

            if (header.EntryCount == 0)
                return (header, Array.Empty<BlobEntry>());

            var entries = new BlobEntry[header.EntryCount];
            int pos = HeaderSize;
            for (int i = 0; i < header.EntryCount; i++)
            {
                entries[i] = new BlobEntry
                {
                    XxHash64 = ReadU64LE(data, pos),
                    FnvHash64 = ReadU64LE(data, pos + 8),
                    Offset = (int)ReadU32LE(data, pos + 16),
                    Length = (int)ReadU32LE(data, pos + 20),
                };
                pos += EntrySize;
            }

            return (header, entries);
        }

        // ═══════════════════════════════════════════════════════
        // Little-endian reads (zero allocation)
        // ═══════════════════════════════════════════════════════

        private static uint ReadU32LE(byte[] d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        private static ulong ReadU64LE(byte[] d, int o) =>
            (ulong)ReadU32LE(d, o) | ((ulong)ReadU32LE(d, o + 4) << 32);

        // ═══════════════════════════════════════════════════════
        // Data types (mirror Core types without dependency)
        // ═══════════════════════════════════════════════════════

        internal struct BlobHeader
        {
            public byte Version;
            public bool Compressed;
            public int EntryCount;
            public int BlobDataSize;
            public int BlobDataOffset;
        }

        internal struct BlobEntry
        {
            public ulong XxHash64;
            public ulong FnvHash64;
            public int Offset;
            public int Length;
        }
    }
}
