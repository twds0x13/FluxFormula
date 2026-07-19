using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 解析后的 .wal 文件头。
    /// </summary>
    public readonly struct WALHeader
    {
        public readonly byte Version;
        public readonly uint FrameCount;
        public readonly uint FrameTableOffset;
        public readonly uint CheckpointOffset;
        public readonly uint CheckpointLength;

        public WALHeader(byte version, uint frameCount, uint frameTableOffset,
                         uint checkpointOffset, uint checkpointLength)
            => (Version, FrameCount, FrameTableOffset, CheckpointOffset, CheckpointLength)
            = (version, frameCount, frameTableOffset, checkpointOffset, checkpointLength);

        public bool HasCheckpoint => CheckpointLength > 0;
    }

    /// <summary>
    /// .wal 二进制格式常量与读写方法。
    /// </summary>
    public static class WALFormat
    {
        /// <summary>Magic: 'F' 'L' 'X' 'W' = 0x57584C46 LE</summary>
        public const uint Magic = 0x57584C46;

        public const byte Version = 1;
        public const byte FlagNone = 0;

        /// <summary>固定头部大小: Magic(4) + Version(1) + Flags(1) + Reserved(2) +
        /// FrameCount(4) + FrameTableOff(4) + CheckpointOff(4) + CheckpointLen(4) = 24</summary>
        public const int HeaderSize = 24;

        public const int FramePtrSize = 8;

        /// <summary>
        /// 尝试验证并解析 .wal 文件头。magic 不匹配或数据不足返回 false。
        /// </summary>
        public static bool TryParseHeader(ReadOnlySpan<byte> data, out WALHeader header)
        {
            header = default;
            if (data.Length < HeaderSize) return false;

            if (data[0] != 'F' || data[1] != 'L' || data[2] != 'X' || data[3] != 'W')
                return false;

            byte version = data[4];
            // byte flags = data[5]; (reserved)
            uint frameCount = BinaryFormat.ReadUInt32LE(data, 8);
            uint frameTableOff = BinaryFormat.ReadUInt32LE(data, 12);
            uint checkpointOff = BinaryFormat.ReadUInt32LE(data, 16);
            uint checkpointLen = BinaryFormat.ReadUInt32LE(data, 20);

            header = new WALHeader(version, frameCount, frameTableOff, checkpointOff, checkpointLen);
            return true;
        }

        public static FramePtr ReadFramePtr(ReadOnlySpan<byte> data, int frameTableOffset, int index)
        {
            int off = frameTableOffset + index * FramePtrSize;
            return new FramePtr(BinaryFormat.ReadUInt64LE(data, off));
        }

        public static void WriteHeader(byte[] buf, ref int offset, uint frameCount,
                                        uint frameTableOffset, uint checkpointOffset, uint checkpointLength)
        {
            BinaryFormat.WriteInt32LE(buf, ref offset, (int)Magic);
            buf[offset] = Version;  offset++;
            buf[offset] = FlagNone; offset++;
            BinaryFormat.WriteUInt16LE(buf, ref offset, 0); // reserved
            BinaryFormat.WriteInt32LE(buf, ref offset, (int)frameCount);
            BinaryFormat.WriteInt32LE(buf, ref offset, (int)frameTableOffset);
            BinaryFormat.WriteInt32LE(buf, ref offset, (int)checkpointOffset);
            BinaryFormat.WriteInt32LE(buf, ref offset, (int)checkpointLength);
        }

        public static void WriteFramePtr(byte[] buf, ref int offset, FramePtr ptr)
        {
            BinaryFormat.WriteUInt64LE(buf, ref offset, ptr.Lsn);
        }

        public static bool IsWal(ReadOnlySpan<byte> bytes)
            => bytes.Length >= 4
            && bytes[0] == 'F' && bytes[1] == 'L' && bytes[2] == 'X' && bytes[3] == 'W';
    }
}
