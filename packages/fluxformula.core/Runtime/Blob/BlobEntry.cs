using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// Blob 偏移表条目——将一条公式的 <see cref="DualHash64"/> 映射到其在 blob 二进制块中的位置。
    /// </summary>
    /// <remarks>
    /// <para>此类型位于 Core 层（无 UnityEngine 依赖），供 source generator、
    /// <see cref="IFluxBlobRegistry"/> 和 <c>FluxBlob</c> 共用。</para>
    /// <para>每条公式 24 字节：DualHash64(16) + offset(4) + length(4)。</para>
    /// </remarks>
    [Serializable]
    public readonly struct BlobEntry : IEquatable<BlobEntry>
    {
        /// <summary>公式字节码的 <see cref="DualHash64"/> 标识</summary>
        public readonly DualHash64 Hash;

        /// <summary>在 blob 数据段中的起始偏移（字节）——相对于 blob data 段起点，而非文件起点</summary>
        public readonly int Offset;

        /// <summary>字节码长度（字节）</summary>
        public readonly int Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BlobEntry(DualHash64 hash, int offset, int length)
        {
            Hash   = hash;
            Offset = offset;
            Length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(BlobEntry other) =>
            Hash.Equals(other.Hash) && Offset == other.Offset && Length == other.Length;

        public override readonly bool Equals(object obj) =>
            obj is BlobEntry other && Equals(other);

        public override readonly int GetHashCode() =>
            Hash.GetHashCode() ^ (Offset * 397) ^ (Length * 7919);

        public override readonly string ToString() =>
            $"[{Hash}] @{Offset} len={Length}";
    }
}
