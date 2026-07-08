using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// Blob 公式数据库——管理预编译公式字节码的 pinned 内存块和偏移表注册。
    /// 支持多个 blob 共存（游戏本体 + mod），每次 Load 返回独立的 <see cref="FluxBlobHandle"/>。
    /// </summary>
    /// <remarks>
    /// <para>预编译公式的字节码直接来自 blob 的 fixed 指针，零拷贝存入 <see cref="FormulaCache"/>。
    /// 运行时新建公式通过 <see cref="FormulaCache.Instance"/> 缓存 JIT delegate。</para>
    ///
    /// <para>使用方式：
    /// <code>
    /// var handle = FluxBlob.Load(blobData, BlobRegistry.GetEntries());
    /// // ... evaluate formulas ...
    /// FluxBlob.Unload(handle);  // mod 卸载时
    /// </code>
    /// </para>
    /// </remarks>
    public static unsafe class FluxBlob
    {
        // ═══════════════════════════════════════════════════════
        // Entry — 类型别名
        // ═══════════════════════════════════════════════════════
        //
        // FluxBlob.Entry 已移至 FluxFormula.Core.BlobEntry。
        // 使用 BlobEntry 替代 FluxBlob.Entry。

        // ═══════════════════════════════════════════════════════
        // 状态
        // ═══════════════════════════════════════════════════════

        private static readonly List<FluxBlobHandle> _loadedBlobs = new();

        /// <summary>当前已加载的 blob 总数</summary>
        public static int LoadedBlobCount
        {
            get { lock (_loadedBlobs) return _loadedBlobs.Count; }
        }

        /// <summary>所有 blob 中的公式条目总数</summary>
        public static int TotalEntryCount { get; private set; }

        /// <summary>是否有任何 blob 已加载</summary>
        public static bool IsInitialized => LoadedBlobCount > 0;

        /// <summary>所有已加载 blob 的总字节数</summary>
        public static int TotalBlobSize { get; private set; }

        // ═══════════════════════════════════════════════════════
        // 公开 API（推荐使用）
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 加载一个 blob 数据块并将其所有条目注册到 <see cref="FormulaCache"/>。
        /// 可多次调用——每次调用创建独立的 <see cref="FluxBlobHandle"/>，互不干扰。
        /// </summary>
        /// <param name="blobData">拼接后的公式字节码（纯 data 段，不含 header 和 entry table）</param>
        /// <param name="entries">偏移表——每条公式的哈希→(offset, length) 映射</param>
        /// <returns>blob 句柄——用于后续 <see cref="Unload"/> 释放</returns>
        /// <exception cref="ArgumentNullException">blobData 为 null</exception>
        public static FluxBlobHandle Load(byte[] blobData, ReadOnlySpan<BlobEntry> entries)
        {
            if (blobData == null)
                throw new ArgumentNullException(nameof(blobData));

            if (blobData.Length == 0 || entries.Length == 0)
                return FluxBlobHandle.Empty;

            // 固定 blob——自此获得跨整个运行时的稳定 byte* 指针
            var blobHandle = GCHandle.Alloc(blobData, GCHandleType.Pinned);
            byte* blobPtr = (byte*)blobHandle.AddrOfPinnedObject();
            int blobLength = blobData.Length;

            var decompressedHandles = new List<GCHandle>();
            var entryKeys = new DualHash64[entries.Length];

            // 将每条公式的字节码指针注册到 FormulaCache
            var cache = FormulaCache.Instance;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];

                if (e.Offset < 0 || e.Length <= 0 || e.Offset + e.Length > blobLength)
                    throw new ArgumentException(
                        $"Blob entry [{i}] out of bounds: offset={e.Offset}, length={e.Length}, blobSize={blobLength}");

                byte* entryPtr = blobPtr + e.Offset;
                var storedSpan = new ReadOnlySpan<byte>(entryPtr, e.Length);

                if (FluxCompression.IsCompressed(storedSpan))
                {
                    byte[] decompressed = FluxCompression.Decompress(storedSpan);
                    var handle = GCHandle.Alloc(decompressed, GCHandleType.Pinned);
                    decompressedHandles.Add(handle);
                    cache.Put(e.Hash, handle.AddrOfPinnedObject(), decompressed.Length);
                }
                else
                {
                    IntPtr ptr = (IntPtr)(blobPtr + e.Offset);
                    cache.Put(e.Hash, ptr, e.Length);
                }

                entryKeys[i] = e.Hash;
            }

            var result = new FluxBlobHandle(
                blobData, blobHandle, blobPtr, blobLength,
                decompressedHandles, entries.Length, entryKeys);

            lock (_loadedBlobs)
                _loadedBlobs.Add(result);

            TotalEntryCount += entries.Length;
            TotalBlobSize += blobLength;

            return result;
        }

        /// <summary>
        /// 卸载指定 blob handle 对应的所有条目。
        /// 释放 pinned 内存和解压后的临时数组，从 FormulaCache 逐条移除注册。
        /// </summary>
        public static void Unload(FluxBlobHandle handle)
        {
            if (handle == null || !handle.IsLoaded)
                return;

            lock (_loadedBlobs)
                _loadedBlobs.Remove(handle);

            // 从 FormulaCache 移除该 blob 的所有条目
            var cache = FormulaCache.Instance;
            foreach (var key in handle.EntryKeys)
                cache.Remove(key);

            // 释放解压后的独立 pinned 数组
            foreach (var h in handle.DecompressedHandles)
            {
                if (h.IsAllocated)
                    h.Free();
            }

            // 释放 blob pinned handle
            if (handle.BlobHandle.IsAllocated)
                handle.BlobHandle.Free();

            TotalEntryCount -= handle.EntryCount;
            TotalBlobSize -= handle.BlobLength;
            handle.MarkUnloaded();
        }

        /// <summary>
        /// 卸载全部已加载的 blob，释放所有资源。等价于对每个 handle 调用 Unload。
        /// </summary>
        public static void Shutdown()
        {
            FluxBlobHandle[] handles;
            lock (_loadedBlobs)
            {
                handles = _loadedBlobs.ToArray();
                _loadedBlobs.Clear();
            }

            foreach (var handle in handles)
            {
                foreach (var h in handle.DecompressedHandles)
                {
                    if (h.IsAllocated) h.Free();
                }
                if (handle.BlobHandle.IsAllocated)
                    handle.BlobHandle.Free();
                handle.MarkUnloaded();
            }

            TotalEntryCount = 0;
            TotalBlobSize = 0;
        }

        // ═══════════════════════════════════════════════════════
        // 向后兼容（已废弃）
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 向后兼容：旧的单 blob 初始化——内部先 Shutdown 再 Load。
        /// 新代码应使用 <see cref="Load"/> + <see cref="FluxBlobHandle"/>。
        /// </summary>
        public static void Initialize(byte[] blob, ReadOnlySpan<BlobEntry> entries)
        {
            if (IsInitialized)
                Shutdown();
            Load(blob, entries);
        }

        /// <summary>所有 blob 中的公式条目总数（向后兼容别名，等价于 <see cref="TotalEntryCount"/>）。</summary>
        public static int EntryCount => TotalEntryCount;

        // ═══════════════════════════════════════════════════════
        // 诊断
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 验证指定哈希的公式在缓存中的完整性——实际计算字节码哈希并比对。
        /// </summary>
        public static bool VerifyIntegrity(DualHash64 expectedHash)
        {
            if (!IsInitialized)
                return false;

            if (!FormulaCache.Instance.TryGet(expectedHash, out IntPtr ptr, out int length))
                return false;

            ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>((void*)ptr, length);
            var actual = DualHash64.Compute(bytes);
            return actual.Equals(expectedHash);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // FluxBlobHandle
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 单个 blob 加载的句柄——持有 pinned 内存、解压临时数组和条目追踪。
    /// 通过 <see cref="FluxBlob.Load"/> 获取，通过 <see cref="FluxBlob.Unload"/> 或
    /// <see cref="Dispose"/> 释放。
    /// </summary>
    public unsafe sealed class FluxBlobHandle : IDisposable
    {
        /// <summary>空句柄——表示加载了空 blob（无条目）</summary>
        internal static readonly FluxBlobHandle Empty = new(null, default, null, 0,
            new List<GCHandle>(), 0, Array.Empty<DualHash64>()) { IsLoaded = false };

        internal readonly byte[] BlobData;
        internal readonly GCHandle BlobHandle;
        internal readonly unsafe byte* BlobPtr;
        internal readonly int BlobLength;
        internal readonly List<GCHandle> DecompressedHandles;
        internal readonly DualHash64[] EntryKeys;

        /// <summary>此 blob 中包含的公式条目数</summary>
        public int EntryCount { get; }

        /// <summary>此 blob 是否仍处于已加载状态</summary>
        public bool IsLoaded { get; private set; }

        internal unsafe FluxBlobHandle(
            byte[] blobData,
            GCHandle blobHandle,
            byte* blobPtr,
            int blobLength,
            List<GCHandle> decompressedHandles,
            int entryCount,
            DualHash64[] entryKeys)
        {
            BlobData = blobData;
            BlobHandle = blobHandle;
            BlobPtr = blobPtr;
            BlobLength = blobLength;
            DecompressedHandles = decompressedHandles;
            EntryCount = entryCount;
            EntryKeys = entryKeys;
            IsLoaded = true;
        }

        internal void MarkUnloaded()
        {
            IsLoaded = false;
        }

        /// <summary>释放此 blob 及其所有注册条目。等价于 <c>FluxBlob.Unload(this)</c>。</summary>
        public void Dispose() => FluxBlob.Unload(this);
    }
}
