using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// Blob 公式数据库——管理预编译公式字节码的 pinned 内存块和偏移表注册。
    /// </summary>
    /// <remarks>
    /// <para>设计目标：替代 <see cref="ConnectCache"/> 的 1 MB native buffer 中间复制层。
    /// 预编译公式的字节码直接来自 blob 的 fixed 指针，零拷贝存入 <see cref="FormulaCache"/>。
    /// ConnectCache 仅保留为运行时新建公式的 fallback 暂存区。</para>
    ///
    /// <para>使用方式（由生成代码调用，用户一般不直接接触）：
    /// <code>
    /// FluxBlob.Initialize(BlobData.Blob, BlobData.Entries);
    /// </code>
    /// </para>
    /// </remarks>
    public static unsafe class FluxBlob
    {
        // ═══════════════════════════════════════════════════════
        // Entry — 偏移表条目
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Blob 偏移表条目——将一条公式的 <see cref="DualHash64"/> 映射到其在 blob 二进制块中的位置。
        /// </summary>
        /// <remarks>每条公式 24 字节：offset(4) + length(4) + DualHash64(16)。</remarks>
        [Serializable]
        public readonly struct Entry : IEquatable<Entry>
        {
            /// <summary>公式字节码的 <see cref="DualHash64"/> 标识</summary>
            public readonly DualHash64 Hash;

            /// <summary>在 blob 中的起始偏移（字节）</summary>
            public readonly int Offset;

            /// <summary>字节码长度（字节）</summary>
            public readonly int Length;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Entry(DualHash64 hash, int offset, int length)
            {
                Hash   = hash;
                Offset = offset;
                Length = length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool Equals(Entry other) =>
                Hash.Equals(other.Hash) && Offset == other.Offset && Length == other.Length;

            public override readonly bool Equals(object obj) =>
                obj is Entry other && Equals(other);

            public override readonly int GetHashCode() =>
                Hash.GetHashCode() ^ (Offset * 397) ^ (Length * 7919);

            public override readonly string ToString() =>
                $"[{Hash}] @{Offset} len={Length}";
        }

        // ═══════════════════════════════════════════════════════
        // 状态
        // ═══════════════════════════════════════════════════════

        /// <summary>blob 是否已初始化并可用</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>当前已注册的公式条目数</summary>
        public static int EntryCount { get; private set; }

        private static byte* _blobPtr;
        private static GCHandle _blobHandle;
        private static int _blobLength;

        // ═══════════════════════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 初始化 blob 数据库：固定内存块，将偏移表中的每条公式注册到 <see cref="FormulaCache"/>。
        /// </summary>
        /// <param name="blob">拼接后的公式字节码内存块</param>
        /// <param name="entries">偏移表——每条公式的哈希→(offset, length) 映射</param>
        /// <exception cref="ArgumentNullException">blob 为 null</exception>
        public static void Initialize(byte[] blob, ReadOnlySpan<Entry> entries)
        {
            if (blob == null)
                throw new ArgumentNullException(nameof(blob));

            if (IsInitialized)
                Shutdown();

            if (blob.Length == 0 || entries.Length == 0)
                return;

            // 固定 blob——自此获得跨整个运行时的稳定 byte* 指针
            _blobHandle = GCHandle.Alloc(blob, GCHandleType.Pinned);
            _blobPtr    = (byte*)_blobHandle.AddrOfPinnedObject();
            _blobLength = blob.Length;

            // 将每条公式的字节码指针注册到 FormulaCache
            // FormulaCache 以 (key → IntPtr, length) 存储，不关心指针来源
            var cache = ConnectCache.Cache;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];

                if (e.Offset < 0 || e.Length <= 0 || e.Offset + e.Length > _blobLength)
                    throw new ArgumentException(
                        $"Blob entry [{i}] out of bounds: offset={e.Offset}, length={e.Length}, blobSize={_blobLength}");

                IntPtr ptr = (IntPtr)(_blobPtr + e.Offset);
                cache.Put(e.Hash, ptr, e.Length);
            }

            EntryCount    = entries.Length;
            IsInitialized = true;
        }

        /// <summary>
        /// 关闭 blob 数据库：释放 fixed 指针，清空相关缓存条目。
        /// </summary>
        public static void Shutdown()
        {
            if (!IsInitialized)
                return;

            if (_blobHandle.IsAllocated)
                _blobHandle.Free();

            _blobPtr    = null;
            _blobLength = 0;
            EntryCount  = 0;

            // 清空 FormulaCache（旧指针指向已释放的 blob）
            ConnectCache.Reset();

            IsInitialized = false;
        }

        // ═══════════════════════════════════════════════════════
        // 诊断
        // ═══════════════════════════════════════════════════════

        /// <summary>blob 内存块的总字节数</summary>
        public static int BlobSize => IsInitialized ? _blobLength : 0;

        /// <summary>
        /// 验证 blob 中指定公式的完整性——从偏移表取期望哈希，实际计算字节码哈希并比对。
        /// </summary>
        public static bool VerifyIntegrity(DualHash64 expectedHash)
        {
            if (!IsInitialized)
                return false;

            if (!ConnectCache.Cache.TryGet(expectedHash, out IntPtr ptr, out int length))
                return false;

            ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>((void*)ptr, length);
            var actual = DualHash64.Compute(bytes);
            return actual.Equals(expectedHash);
        }
    }
}
