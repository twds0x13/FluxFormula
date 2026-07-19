using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 帧边界指针：包装一个 ulong LSN（Log Sequence Number），标记 WAL 中两帧之间的边界。
    /// </summary>
    /// <remarks>
    /// <para>FramePtr 本质是 WAL entry 流中的一个字节偏移量。帧 N 的 entry 集合
    /// 是 LSN 区间 [FramePtr[N-1], FramePtr[N]) 内的全部 entry。</para>
    /// <para>类似 CPU 的 PC 指针：FramePtr 不携带帧内 entry 的元数据，
    /// 帧结构从指针位置中涌现。</para>
    /// </remarks>
    public readonly struct FramePtr : IEquatable<FramePtr>, IComparable<FramePtr>
    {
        /// <summary>绝对 LSN 值。LSN 0 = 帧 0 边界（WAL 原点）。</summary>
        public readonly ulong Lsn;

        public FramePtr(ulong lsn) => Lsn = lsn;

        // ── 算术 ──

        public static FramePtr operator +(FramePtr ptr, ulong offset) => new(ptr.Lsn + offset);
        public static FramePtr operator -(FramePtr ptr, ulong offset) => new(ptr.Lsn - offset);

        // ── 相等性 ──

        public bool Equals(FramePtr other) => Lsn == other.Lsn;
        public override bool Equals(object obj) => obj is FramePtr other && Equals(other);
        public override int GetHashCode() => Lsn.GetHashCode();

        public static bool operator ==(FramePtr left, FramePtr right) => left.Equals(right);
        public static bool operator !=(FramePtr left, FramePtr right) => !left.Equals(right);

        // ── 比较 ──

        public int CompareTo(FramePtr other) => Lsn.CompareTo(other.Lsn);

        public static bool operator <(FramePtr left, FramePtr right) => left.Lsn < right.Lsn;
        public static bool operator >(FramePtr left, FramePtr right) => left.Lsn > right.Lsn;
        public static bool operator <=(FramePtr left, FramePtr right) => left.Lsn <= right.Lsn;
        public static bool operator >=(FramePtr left, FramePtr right) => left.Lsn >= right.Lsn;

        // ── 区间判断 ──

        /// <summary>判断给定 LSN 是否落在 [start, end) 区间内。</summary>
        public static bool Contains(FramePtr start, FramePtr end, ulong lsn)
            => lsn >= start.Lsn && lsn < end.Lsn;

        public override string ToString() => $"FramePtr({Lsn})";
    }
}
