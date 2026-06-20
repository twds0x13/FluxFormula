using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 128 位双重哈希：xxHash64（高 64 位）|| FNV-1a 64（低 64 位）。
    /// 两个内部结构正交的非密码学哈希组合，用于预防结构性碰撞注入。
    /// 单独攻破任一哈希的已知方法无法同时作用于另一个——攻击者需要求解联立碰撞方程。
    /// </summary>
    /// <remarks>
    /// 在 FluxFormula 中，此类型是公式字节码完整性验证的核心。
    /// 偏移表（编译进 assembly）存储每个公式的期望 DualHash64；
    /// Blob 仅提供原始字节码。加载时计算字节码的 DualHash64 与期望值比对。
    /// </remarks>
    public readonly struct DualHash64 : IEquatable<DualHash64>
    {
        // ═══════════════════════════════════════════════════════
        // xxHash64 常量
        // ═══════════════════════════════════════════════════════

        private const ulong XXH_PRIME_1 = 0x9E3779B185EBCA87UL;
        private const ulong XXH_PRIME_2 = 0xC2B2AE3D27D4EB4FUL;
        private const ulong XXH_PRIME_3 = 0x165667B19E3779F9UL;
        private const ulong XXH_PRIME_4 = 0x85EBCA77C2B2AE63UL;
        private const ulong XXH_PRIME_5 = 0x27D4EB2F165667C5UL;

        // ═══════════════════════════════════════════════════════
        // FNV-1a 64 常量
        // ═══════════════════════════════════════════════════════

        private const ulong FNV_BASIS = 0xCBF29CE484222325UL;
        private const ulong FNV_PRIME = 0x100000001B3UL;

        // ═══════════════════════════════════════════════════════
        // 字段
        // ═══════════════════════════════════════════════════════

        /// <summary>xxHash64 结果（高 64 位）</summary>
        public readonly ulong XxHash64;

        /// <summary>FNV-1a 64 结果（低 64 位）</summary>
        public readonly ulong FnvHash64;

        // ═══════════════════════════════════════════════════════
        // 构造
        // ═══════════════════════════════════════════════════════

        public DualHash64(ulong xxHash64, ulong fnvHash64)
        {
            XxHash64  = xxHash64;
            FnvHash64 = fnvHash64;
        }

        // ═══════════════════════════════════════════════════════
        // 计算
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 对数据计算双重哈希。
        /// 返回的 DualHash64 可用于与偏移表中的期望值做完整性比对。
        /// </summary>
        public static DualHash64 Compute(ReadOnlySpan<byte> data)
        {
            return new DualHash64(
                xxHash64(data, seed: 0),
                Fnv1a64(data)
            );
        }

        /// <summary>
        /// 带种子的 xxHash64 计算。种子不同则相同数据产生不同哈希。
        /// 用于需要独立哈希空间的场景（如连接链路的递进 key 计算）。
        /// </summary>
        public static DualHash64 ComputeSeeded(ReadOnlySpan<byte> data, ulong xxhSeed)
        {
            return new DualHash64(
                xxHash64(data, xxhSeed),
                Fnv1a64(data) // FNV 无种子概念，保持正交
            );
        }

        // ═══════════════════════════════════════════════════════
        // xxHash64 实现
        // ═══════════════════════════════════════════════════════

        /// <summary>xxHash64 算法核心。32 字节分条处理，余数逐级消解。</summary>
        private static ulong xxHash64(ReadOnlySpan<byte> data, ulong seed)
        {
            int len = data.Length;

            if (len >= 32)
            {
                // 四条累加器并行处理 32 字节分条
                ulong v1 = seed + XXH_PRIME_1 + XXH_PRIME_2;
                ulong v2 = seed + XXH_PRIME_2;
                ulong v3 = seed;
                ulong v4 = seed - XXH_PRIME_1;

                int limit = len - 32;
                int p = 0;

                do
                {
                    v1 = xxhRound(v1, ReadU64Le(data, p));      p += 8;
                    v2 = xxhRound(v2, ReadU64Le(data, p));      p += 8;
                    v3 = xxhRound(v3, ReadU64Le(data, p));      p += 8;
                    v4 = xxhRound(v4, ReadU64Le(data, p));      p += 8;
                }
                while (p <= limit);

                ulong h = RotL64(v1, 1) + RotL64(v2, 7) + RotL64(v3, 12) + RotL64(v4, 18);

                // 合并四条累加器
                h = xxhMergeAccumulators(h, v1);
                h = xxhMergeAccumulators(h, v2);
                h = xxhMergeAccumulators(h, v3);
                h = xxhMergeAccumulators(h, v4);

                h += (ulong)len;

                // 处理剩余不足 32 字节的部分
                return xxhRemainder(data, p, len - p, h);
            }
            else
            {
                // 短输入：直接从 seed + P5 开始
                ulong h = seed + XXH_PRIME_5 + (ulong)len;
                return xxhRemainder(data, 0, len, h);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong xxhRound(ulong acc, ulong input)
        {
            acc += input * XXH_PRIME_2;
            acc  = RotL64(acc, 31);
            acc *= XXH_PRIME_1;
            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong xxhMergeAccumulators(ulong acc, ulong lane)
        {
            lane  = lane * XXH_PRIME_2;
            lane  = RotL64(lane, 31);
            lane *= XXH_PRIME_1;
            acc  ^= lane;
            acc   = acc * XXH_PRIME_1 + XXH_PRIME_4;
            return acc;
        }

        /// <summary>余数处理：8 字节 → 4 字节 → 1 字节逐级消解，最后雪崩混合</summary>
        private static ulong xxhRemainder(ReadOnlySpan<byte> data, int offset, int remaining, ulong h)
        {
            int end = offset + remaining;
            int p   = offset;

            // 8 字节块
            while (p + 8 <= end)
            {
                ulong k1  = xxhRound(0, ReadU64Le(data, p));
                h        ^= k1;
                h         = RotL64(h, 27) * XXH_PRIME_1 + XXH_PRIME_4;
                p        += 8;
            }

            // 4 字节块
            if (p + 4 <= end)
            {
                h ^= ReadU32Le(data, p) * XXH_PRIME_1;
                h  = RotL64(h, 23) * XXH_PRIME_2 + XXH_PRIME_3;
                p += 4;
            }

            // 逐字节
            while (p < end)
            {
                h ^= data[p] * XXH_PRIME_5;
                h  = RotL64(h, 11) * XXH_PRIME_1;
                p++;
            }

            // 最终雪崩混合——确保输入中的每个位都影响输出的每个位
            h ^= h >> 33;
            h *= XXH_PRIME_2;
            h ^= h >> 29;
            h *= XXH_PRIME_3;
            h ^= h >> 32;

            return h;
        }

        // ═══════════════════════════════════════════════════════
        // FNV-1a 64 实现
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// FNV-1a 64：逐字节 XOR 后乘质数。
        /// 极简实现，无内部状态，~10 行代码。对短输入的雪崩效应优秀。
        /// </summary>
        private static ulong Fnv1a64(ReadOnlySpan<byte> data)
        {
            ulong hash = FNV_BASIS;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }
            return hash;
        }

        // ═══════════════════════════════════════════════════════
        // 基元
        // ═══════════════════════════════════════════════════════

        /// <summary>循环左移</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotL64(ulong value, int bits)
        {
            return (value << bits) | (value >> (64 - bits));
        }

        /// <summary>从 span 偏移处读取 8 字节 little-endian</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadU64Le(ReadOnlySpan<byte> data, int offset)
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

        /// <summary>从 span 偏移处读取 4 字节 little-endian</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadU32Le(ReadOnlySpan<byte> data, int offset)
        {
            return (uint)data[offset]
                | ((uint)data[offset + 1] << 8)
                | ((uint)data[offset + 2] << 16)
                | ((uint)data[offset + 3] << 24);
        }

        // ═══════════════════════════════════════════════════════
        // 相等性
        // ═══════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(DualHash64 other)
        {
            return XxHash64 == other.XxHash64 && FnvHash64 == other.FnvHash64;
        }

        public override bool Equals(object obj)
        {
            return obj is DualHash64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            // 两个 64-bit hash 折叠为单个 32-bit
            return unchecked((int)(XxHash64 ^ (XxHash64 >> 32) ^ FnvHash64 ^ (FnvHash64 >> 32)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(DualHash64 left, DualHash64 right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(DualHash64 left, DualHash64 right) => !left.Equals(right);

        // ═══════════════════════════════════════════════════════
        // 格式化
        // ═══════════════════════════════════════════════════════

        /// <summary>32 字符十六进制：xxHash64(上 16 字符) + FNV-1a 64(下 16 字符)</summary>
        public override string ToString()
        {
            return $"{XxHash64:X16}{FnvHash64:X16}";
        }

        /// <summary>
        /// 从 32 字符十六进制字符串解析。大写/小写兼容。
        /// 格式：前 16 字符 = xxHash64，后 16 字符 = FNV-1a 64。
        /// </summary>
        public static DualHash64 Parse(ReadOnlySpan<char> hex)
        {
            if (hex.Length != 32)
                throw new FormatException($"DualHash64 要求 32 字符十六进制字符串，实际长度: {hex.Length}");

            ulong xxh = ParseHexU64(hex.Slice(0, 16));
            ulong fnv = ParseHexU64(hex.Slice(16, 16));
            return new DualHash64(xxh, fnv);
        }

        private static ulong ParseHexU64(ReadOnlySpan<char> hex)
        {
            ulong v = 0;
            for (int i = 0; i < 16; i++)
            {
                char c = hex[i];
                uint d = c switch
                {
                    >= '0' and <= '9' => (uint)(c - '0'),
                    >= 'A' and <= 'F' => (uint)(c - 'A' + 10),
                    >= 'a' and <= 'f' => (uint)(c - 'a' + 10),
                    _ => throw new FormatException($"非法十六进制字符: '{c}'")
                };
                v = (v << 4) | d;
            }
            return v;
        }

        // ═══════════════════════════════════════════════════════
        // 为 FormulaCache hashmap 提供的组合 key 计算
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 累进组合 hash——为 Connect 链路 key 计算设计。
        /// 用法：key = seed; key = DualHash64.Combine(key, hashA); key = DualHash64.Combine(key, hashB); ...
        /// 顺序敏感：Combine(a, Combine(b, c)) ≠ Combine(b, Combine(a, c))。
        /// </summary>
        /// <remarks>
        /// 这里选择混合而非嵌套哈希的原因：嵌套会在每次 Connect 时重新扫描整个字节序列。
        /// Combine 只对已有 hash 做代数混合，O(1) 时间。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DualHash64 Combine(DualHash64 accumulated, DualHash64 next)
        {
            // 利用 xxHash 的种子机制：将累积哈希的下一个片段"喂入"下一次哈希流
            // 对 FNV 做逐分量混合——FNV 不具备种子语义，用代数混合模拟
            return new DualHash64(
                xxHashCombine(accumulated.XxHash64, next.XxHash64),
                fnvCombine(accumulated.FnvHash64, next.FnvHash64)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong xxHashCombine(ulong acc, ulong next)
        {
            acc += next * XXH_PRIME_2;
            acc  = RotL64(acc, 31);
            acc *= XXH_PRIME_1;
            return acc ^ (acc >> 33);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong fnvCombine(ulong acc, ulong next)
        {
            // FNV 本身的逐字节处理不可逆推为"逐 hash 组合"。
            // 用 XOR-multiply 结构保持 FNV 的正交性。
            acc ^= next;
            acc *= FNV_PRIME;
            return acc;
        }
    }
}
