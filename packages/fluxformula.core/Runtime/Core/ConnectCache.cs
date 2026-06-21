using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FluxFormula")]

namespace FluxFormula.Core
{
    /// <summary>
    /// Connect 缓存桥接层：持有 native buffer，为 Connect 产物提供稳定指针存入 FormulaCache。
    /// </summary>
    /// <remarks>
    /// <para>为什么需要这一层：
    /// Connect 的产物是托管 Instruction[]，无法直接获得持久稳定的 byte* 指针。
    /// ConnectCache 维护一个大的固定 native buffer，将每个 Connect 结果复制进来后，
    /// 向 FormulaCache 存入指向 buffer 内部的有效指针。</para>
    ///
    /// <para>之后当 blob 集成完毕时，ConnectCache 被替代——公式字节码直接来自 blob 的 fixed 指针，
    /// 不再需要这个中间复制层。FormulaCache 的 key/value 语义不变，只替换"指针指向谁"。</para>
    ///
    /// <para>设计约束：
    ///   - Buffer 固定 1 MB，只增不缩。满时清空全缓存重新开始。
    ///   - 单例（static），无锁（主线程使用）。</para>
    /// </remarks>
    internal static unsafe class ConnectCache
    {
        // ═══════════════════════════════════════════════════════
        // 状态
        // ═══════════════════════════════════════════════════════

        /// <summary>buffer 容量（字节）——首次访问时从 <see cref="FluxConfig"/> 读取</summary>
        private static readonly int BufferSize;

        /// <summary>native buffer 的托管持有者（pin 住以获得稳定指针）</summary>
        private static readonly byte[] _bufferArray;

        /// <summary>GCHandle 防止 buffer 被 GC 移动</summary>
        private static readonly GCHandle _bufferHandle;

        /// <summary>buffer 的 native 指针</summary>
        private static readonly byte* _bufferPtr;

        /// <summary>buffer 当前已用字节数</summary>
        private static int _bufferUsed;

        /// <summary>底层缓存后端</summary>
        private static IFluxCacheProvider _cache;

        // ═══════════════════════════════════════════════════════
        // 构造
        // ═══════════════════════════════════════════════════════

        static ConnectCache()
        {
            BufferSize   = FluxConfig.Current.ConnectBufferSize;
            _bufferArray = new byte[BufferSize];
            _bufferHandle = GCHandle.Alloc(_bufferArray, GCHandleType.Pinned);
            _bufferPtr = (byte*)_bufferHandle.AddrOfPinnedObject();
            _cache = new FormulaCache();
        }

        // ═══════════════════════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════════════════════

        /// <summary>可替换的缓存后端（默认 FormulaCache）。用于注入自定义 IFluxCacheProvider。</summary>
        public static IFluxCacheProvider Cache
        {
            get => _cache;
            set => _cache = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>当前 buffer 已用字节数（诊断用）</summary>
        public static int BufferUsed => _bufferUsed;

        /// <summary>缓存命中数统计（诊断用）</summary>
        public static long HitCount { get; private set; }

        /// <summary>缓存未命中数统计（诊断用）</summary>
        public static long MissCount { get; private set; }

        /// <summary>
        /// 按哈希键查找 Connect 产物。
        /// 返回的指针指向内部 buffer，在下一次 buffer 满重置前有效。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(DualHash64 key, out byte* data, out int length)
        {
            if (_cache.TryGet(key, out IntPtr ptr, out length))
            {
                data = (byte*)ptr;
                HitCount++;
                return true;
            }

            data   = null;
            length = 0;
            MissCount++;
            return false;
        }

        /// <summary>
        /// 将 Connect 产物序列化字节存入缓存。
        /// 数据会被复制到内部 native buffer 中以获得稳定指针。
        /// </summary>
        public static void Put(DualHash64 key, ReadOnlySpan<byte> bytes)
        {
            int len = bytes.Length;

            // Buffer 满 → 全量重置（旧指针全部失效，缓存清空）
            if (_bufferUsed + len > BufferSize)
            {
                _bufferUsed = 0;
                _cache      = new FormulaCache();
            }

            // 复制字节码到 native buffer
            bytes.CopyTo(new Span<byte>(_bufferPtr + _bufferUsed, len));

            // 存入缓存——指针指向 native buffer 内部
            _cache.Put(key, (IntPtr)(_bufferPtr + _bufferUsed), len);

            _bufferUsed += len;
        }

        /// <summary>清空缓存和 buffer（一般用于测试）</summary>
        public static void Reset()
        {
            _bufferUsed = 0;
            _cache      = new FormulaCache();
            HitCount    = 0;
            MissCount   = 0;
        }
    }
}
