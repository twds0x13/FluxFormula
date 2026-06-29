using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 公式字节码压缩原语：blob 传输时减小体积，运行时不参与热路径。
    /// </summary>
    /// <remarks>
    /// <para>格式（6 字节头部 + 载荷）：</para>
    /// <code>
    /// [0xBF] [Algorithm:1] [UncompressedLen:4 LE] [Payload...]
    /// Algorithm: 0 = None（直接存储，未压缩）, 1 = Brotli
    /// </code>
    ///
    /// <para>使用点：</para>
    /// <list type="bullet">
    ///   <item>构建时：<see cref="Compress"/> 在 <c>FluxBlobBuilder</c> 中调用</item>
    ///   <item>加载时：<see cref="Decompress"/> 在 <c>FluxBlob.Initialize</c> 中调用</item>
    /// </list>
    ///
    /// <para>设计参考：<see cref="BinaryFormat"/>（静态方法、零状态、AggressiveInlining）</para>
    /// </remarks>
    internal static class FluxCompression
    {
        /// <summary>压缩魔数：用于区分压缩与未压缩字节码（0xBF = "Binary Flux"）</summary>
        public const byte Magic = 0xBF;

        /// <summary>头部总字节数：Magic(1) + Algorithm(1) + UncompressedLen(4)</summary>
        public const int HeaderSize = 6;

        private const byte AlgoNone   = 0;
        private const byte AlgoBrotli = 1;

        // ═══════════════════════════════════════════════════════
        // 压缩 / 解压
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 压缩字节码。若压缩后体积 ≥ 原始体积，自动回退为 None（直接存储），
        /// 保证不会比不压缩更差。
        /// </summary>
        /// <param name="raw">原始 .ff / .vff 字节码</param>
        /// <returns>带 6 字节头部的压缩（或回退）数据</returns>
        public static byte[] Compress(ReadOnlySpan<byte> raw)
        {
            if (raw.IsEmpty)
                return Array.Empty<byte>();

            // 尝试 Brotli 压缩
            byte[] compressed;
            using (var ms = new MemoryStream(raw.Length))
            {
                // 写入头部占位（先写 dummy，最后回填）
                ms.WriteByte(Magic);
                ms.WriteByte(AlgoBrotli); // 临时
                ms.WriteByte(0);          // UncompressedLen placeholder
                ms.WriteByte(0);
                ms.WriteByte(0);
                ms.WriteByte(0);

                using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    brotli.Write(raw);
                }

                compressed = ms.ToArray();
            }

            // 回退检查：压缩后体积 ≥ 原始体积时，用 None 算法存储原始数据
            if (compressed.Length - HeaderSize >= raw.Length)
            {
                var result = new byte[HeaderSize + raw.Length];
                result[0] = Magic;
                result[1] = AlgoNone;
                WriteInt32LE(result, 2, raw.Length);
                raw.CopyTo(result.AsSpan(HeaderSize));
                return result;
            }

            // 回填实际长度
            WriteInt32LE(compressed, 2, raw.Length);
            return compressed;
        }

        /// <summary>
        /// 解压字节码。自动识别头部中的算法字段并分发。
        /// </summary>
        /// <param name="data">带 6 字节头部的压缩数据（或未压缩的 None 格式）</param>
        /// <returns>原始 .ff / .vff 字节码</returns>
        /// <exception cref="InvalidDataException">魔数不匹配或数据损坏</exception>
        public static byte[] Decompress(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return Array.Empty<byte>();

            if (!IsCompressed(data))
                throw new InvalidDataException(
                    $"FluxCompression: invalid magic byte 0x{data[0]:X2}, expected 0x{Magic:X2}");

            int uncompressedLen = ReadInt32LE(data, 2);
            byte algo = data[1];

            return algo switch
            {
                AlgoNone => NoneDecompress(data.Slice(HeaderSize), uncompressedLen),
                AlgoBrotli => BrotliDecompress(data.Slice(HeaderSize), uncompressedLen),
                _ => throw new InvalidDataException(
                    $"FluxCompression: unknown algorithm {algo}")
            };
        }

        // ═══════════════════════════════════════════════════════
        // 诊断
        // ═══════════════════════════════════════════════════════

        /// <summary>检查数据是否带压缩头部。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompressed(ReadOnlySpan<byte> data)
        {
            return data.Length >= HeaderSize && data[0] == Magic;
        }

        /// <summary>从头部读取解压后的原始长度（用于预分配缓冲区）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PeekUncompressedLength(ReadOnlySpan<byte> data)
        {
            return IsCompressed(data) ? ReadInt32LE(data, 2) : data.Length;
        }

        /// <summary>获取载荷的压缩算法名称（诊断用）。</summary>
        public static string GetAlgorithmName(ReadOnlySpan<byte> data)
        {
            if (!IsCompressed(data)) return "Unknown";
            return data[1] switch
            {
                AlgoNone   => "None",
                AlgoBrotli => "Brotli",
                _          => "Unknown"
            };
        }

        // ═══════════════════════════════════════════════════════
        // 内部
        // ═══════════════════════════════════════════════════════

        private static byte[] NoneDecompress(ReadOnlySpan<byte> payload, int expectedLen)
        {
            var result = new byte[expectedLen];
            payload.CopyTo(result);
            return result;
        }

        private static byte[] BrotliDecompress(ReadOnlySpan<byte> payload, int expectedLen)
        {
            var result = new byte[expectedLen];
            using (var ms = new MemoryStream(payload.ToArray(), writable: false))
            using (var brotli = new BrotliStream(ms, CompressionMode.Decompress))
            {
                int totalRead = 0;
                while (totalRead < expectedLen)
                {
                    int read = brotli.Read(result, totalRead, expectedLen - totalRead);
                    if (read == 0)
                        throw new InvalidDataException(
                            $"FluxCompression: Brotli decompression ended early ({totalRead}/{expectedLen} bytes)");
                    totalRead += read;
                }
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════
        // 内部小端序读写（与 BinaryFormat 风格一致，但 Span-only）
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadInt32LE(ReadOnlySpan<byte> data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt32LE(byte[] buf, int offset, int value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }
    }
}
