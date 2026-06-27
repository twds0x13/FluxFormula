using System;
using System.Threading;
using FluxFormula.Core;
using Unity.Collections;

namespace FluxFormula.Burst
{
    /// <summary>
    /// Jobs 路径专用字节码缓存：DualHash64 → NativeArray&lt;byte&gt; 的开放寻址 hashmap。
    /// 同公式的多个 <see cref="FluxBurstInstance{TData, TDef}"/> 共享同一块 NativeArray，
    /// 通过引用计数管理生命周期。
    /// </summary>
    /// <remarks>
    /// <para>设计要点：</para>
    /// <list type="bullet">
    ///   <item>开放寻址 + 线性探测，与 <see cref="FormulaCache"/> 同构异体</item>
    ///   <item>存储 <see cref="NativeArray{Byte}"/>（Allocator.Persistent），引用计数管理生命周期</item>
    ///   <item>默认 64 槽位（通过 <see cref="FluxConfig.NativeBytecodeCacheCapacity"/> 配置）——
    ///   Jobs 路径中唯一公式种类数通常远小于实例数</item>
    ///   <item>驱逐仅针对 refCount == 0 的条目；被引用的条目不可驱逐</item>
    ///   <item>线程安全：lock 保护全部表操作——访问频率低（仅实例构造/销毁时），无需读写锁</item>
    /// </list>
    /// <para>使用方式：创建单一 <see cref="NativeBytecodeCache"/> 实例，传给所有
    /// <see cref="FluxBurstInstance{TData, TDef}"/> 构造函数。应用退出时调用 <see cref="Dispose"/>。</para>
    /// </remarks>
    public class NativeBytecodeCache : IDisposable
    {
        // ═══════════════════════════════════════════════════════
        // 常量
        // ═══════════════════════════════════════════════════════

        /// <summary>空槽位——从未写入过</summary>
        private const int Empty = -1;

        /// <summary>墓碑——曾经有值但被驱逐</summary>
        private const int Tombstone = -3;

        // ═══════════════════════════════════════════════════════
        // 存储
        // ═══════════════════════════════════════════════════════

        /// <summary>当前槽位数（构造时从 <see cref="FluxConfig"/> 读取）</summary>
        internal readonly int Capacity;

        /// <summary>DualHash64.XxHash64 分量</summary>
        private readonly ulong[] _xxHashKeys;

        /// <summary>DualHash64.FnvHash64 分量</summary>
        private readonly ulong[] _fnvHashKeys;

        /// <summary>缓存的字节码 NativeArray（Allocator.Persistent）</summary>
        private readonly NativeArray<byte>[] _bytecodes;

        /// <summary>
        /// 槽位元数据：
        /// <br/>≥ 0 = 活跃条目，值为字节码 byte count
        /// <br/>Empty (-1) = 空槽位
        /// <br/>Tombstone (-3) = 已驱逐
        /// </summary>
        private readonly int[] _lengths;

        /// <summary>每个槽位的活跃引用计数（Acquire +1, Release -1）</summary>
        private readonly int[] _refCounts;

        /// <summary>当前存活条目数（不含墓碑）</summary>
        private int _count;

        /// <summary>环形写入头：下次驱逐时从此位置开始寻找可驱逐 slot</summary>
        private int _ringHead;

        /// <summary>墓碑计数器——超过阈值触发全表压缩</summary>
        private int _tombstoneCount;

        /// <summary>表操作锁——读/写全路径互斥</summary>
        private readonly object _lock = new();

        private bool _disposed;

        // ═══════════════════════════════════════════════════════
        // 构造 / 释放
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 创建字节码缓存。
        /// </summary>
        /// <param name="capacity">槽位数。0 或负数使用 <see cref="FluxConfig.NativeBytecodeCacheCapacity"/>（默认 64）</param>
        public NativeBytecodeCache(int capacity = 0)
        {
            Capacity       = capacity > 0 ? capacity : FluxConfig.Current.NativeBytecodeCacheCapacity;
            _xxHashKeys   = new ulong[Capacity];
            _fnvHashKeys  = new ulong[Capacity];
            _bytecodes    = new NativeArray<byte>[Capacity];
            _lengths      = new int[Capacity];
            _refCounts    = new int[Capacity];

            for (int i = 0; i < Capacity; i++)
                _lengths[i] = Empty;
        }

        /// <summary>
        /// 释放全部缓存的 <see cref="NativeArray{Byte}"/>。
        /// 调用前应确保所有消费者已调用 <see cref="Release"/>——
        /// 若仍有活跃引用，Dispose 会强制释放 NativeArray（安全句柄在 debug 模式下会报错）。
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                for (int i = 0; i < Capacity; i++)
                {
                    if (_lengths[i] >= 0 && _bytecodes[i].IsCreated)
                    {
                        _bytecodes[i].Dispose();
                        _lengths[i] = Empty;
                    }
                }

                _count = 0;
                _tombstoneCount = 0;
            }
        }

        // ═══════════════════════════════════════════════════════
        // 查询与获取
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 获取或创建 <paramref name="hash"/> 对应的共享 <see cref="NativeArray{Byte}"/>。
        /// </summary>
        /// <param name="hash">公式字节码的 DualHash64</param>
        /// <param name="source">字节码源数据（来自 <c>formula.ToBytes()</c>）。
        /// 缓存命中时此参数被忽略（调用方可依赖 GC 回收）；未命中时拷贝到 NativeArray。</param>
        /// <returns>共享的 NativeArray 副本。调用方通过 <see cref="Release"/> 归还引用。</returns>
        public NativeArray<byte> Acquire(DualHash64 hash, byte[] source)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                int slot = FindSlot(hash);

                if (slot >= 0)
                {
                    // ── 命中：引用计数 +1，返回已有 NativeArray ──
                    Interlocked.Increment(ref _refCounts[slot]);
                    return _bytecodes[slot];
                }

                // ── 未命中：分配 NativeArray 并写入 ──
                var nativeArray = new NativeArray<byte>(source, Allocator.Persistent);
                InsertEntry(hash, nativeArray, source.Length);
                return nativeArray;
            }
        }

        /// <summary>
        /// 释放一个引用。引用计数归零后，该条目变为可驱逐状态。
        /// </summary>
        /// <param name="hash">公式字节码的 DualHash64</param>
        public void Release(DualHash64 hash)
        {
            lock (_lock)
            {
                if (_disposed) return;

                int slot = FindSlot(hash);
                if (slot >= 0 && _refCounts[slot] > 0)
                {
                    Interlocked.Decrement(ref _refCounts[slot]);
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        // 属性
        // ═══════════════════════════════════════════════════════

        /// <summary>当前存活条目数（持锁读取）</summary>
        public int Count { get { lock (_lock) return _count; } }

        /// <summary>当前墓碑数（诊断用，持锁读取）</summary>
        public int TombstoneCount { get { lock (_lock) return _tombstoneCount; } }

        // ═══════════════════════════════════════════════════════
        // 表操作
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 在探测链中定位 hash 所在的槽位索引。
        /// 返回 ≥ 0 = 命中；返回 -1 = 未找到。
        /// </summary>
        private int FindSlot(DualHash64 key)
        {
            int start = HashToSlot(key.XxHash64);
            int slot  = start;

            do
            {
                int state = _lengths[slot];

                if (state == Empty)
                    return -1;               // 探测链断裂——从未写入过

                if (state != Tombstone)
                {
                    if (_xxHashKeys[slot]  == key.XxHash64 &&
                        _fnvHashKeys[slot] == key.FnvHash64)
                        return slot;         // 命中
                }
                // state == Tombstone: 继续探测

                slot = (slot + 1) % Capacity;
            }
            while (slot != start);

            return -1; // 全表扫描完仍未找到
        }

        /// <summary>
        /// 为新的写入找到合适槽位：优先空槽位，其次墓碑。
        /// 返回 -1 表示全表满（无空槽位、无墓碑）。
        /// </summary>
        private int FindInsertSlot(ulong xxHash)
        {
            int start          = HashToSlot(xxHash);
            int slot           = start;
            int firstTombstone = -1;

            do
            {
                int state = _lengths[slot];

                if (state == Empty)
                    return firstTombstone >= 0 ? firstTombstone : slot;

                if (state == Tombstone && firstTombstone < 0)
                    firstTombstone = slot;

                slot = (slot + 1) % Capacity;
            }
            while (slot != start);

            return firstTombstone;
        }

        private void WriteSlot(int slot, DualHash64 key, NativeArray<byte> bytecode, int length)
        {
            _xxHashKeys[slot]  = key.XxHash64;
            _fnvHashKeys[slot] = key.FnvHash64;
            _bytecodes[slot]   = bytecode;
            _lengths[slot]     = length;
        }

        /// <summary>
        /// 将新条目插入表中。优先空槽位/墓碑；全表满时驱逐一个 refCount==0 的条目。
        /// </summary>
        private void InsertEntry(DualHash64 key, NativeArray<byte> nativeArray, int length)
        {
            int insertSlot = FindInsertSlot(key.XxHash64);

            if (insertSlot >= 0)
            {
                bool wasTombstone = _lengths[insertSlot] == Tombstone;
                WriteSlot(insertSlot, key, nativeArray, length);
                _refCounts[insertSlot] = 1;
                if (!wasTombstone) _count++;
                else               _tombstoneCount--;
            }
            else
            {
                EvictAndWrite(key, nativeArray, length);
            }

            // 墓碑过多时全量压缩
            if (_tombstoneCount > Capacity / 4)
                Compact();
        }

        /// <summary>
        /// 环形驱逐写入：从 <see cref="_ringHead"/> 开始寻找 refCount==0 的可驱逐槽位，
        /// 释放其 NativeArray 后写入新值。若完整环形扫描未找到可驱逐槽位（全表被引用），
        /// 抛出 <see cref="InvalidOperationException"/>——应通过
        /// <see cref="FluxConfig.NativeBytecodeCacheCapacity"/> 增大容量。
        /// </summary>
        private void EvictAndWrite(DualHash64 key, NativeArray<byte> nativeArray, int length)
        {
            int startRing = _ringHead;

            // 寻找可驱逐槽位：空槽位 / 墓碑 / refCount==0 的活跃条目
            int victim;
            while (true)
            {
                victim = _ringHead;
                _ringHead = (_ringHead + 1) % Capacity;

                int state = _lengths[victim];
                if (state == Empty || state == Tombstone || _refCounts[victim] == 0)
                    break;

                if (_ringHead == startRing)
                {
                    // 全表扫描：所有活跃条目均有引用——无法驱逐
                    // 释放本次未命中分配的 NativeArray 避免泄漏
                    nativeArray.Dispose();
                    throw new InvalidOperationException(
                        $"NativeBytecodeCache (capacity={Capacity}) is full and all entries are actively referenced. " +
                        "Increase FluxConfig.NativeBytecodeCacheCapacity or ensure Release() is called before eviction.");
                }
            }

            // 清理旧条目
            int oldState = _lengths[victim];
            if (oldState >= 0)
            {
                if (_bytecodes[victim].IsCreated)
                    _bytecodes[victim].Dispose();

                _lengths[victim] = Tombstone;
                _tombstoneCount++;
                _count--;
            }
            // Empty 或 Tombstone：不需清理，直接覆盖

            // 写入新值
            WriteSlot(victim, key, nativeArray, length);
            _refCounts[victim] = 1;
            _count++;
        }

        /// <summary>
        /// 全表压缩：移除所有墓碑，重排条目使其探测链完整。
        /// 相当于全表 rehash——收集全部存活条目 → 清空 → 重插入。
        /// </summary>
        private void Compact()
        {
            // 收集所有存活条目
            var liveKeys     = new (ulong xx, ulong fnv)[_count];
            var liveBytecodes = new NativeArray<byte>[_count];
            var liveLens     = new int[_count];
            var liveRefs     = new int[_count];
            int idx = 0;

            for (int i = 0; i < Capacity; i++)
            {
                if (_lengths[i] >= 0)
                {
                    liveKeys[idx]      = (_xxHashKeys[i], _fnvHashKeys[i]);
                    liveBytecodes[idx] = _bytecodes[i];
                    liveLens[idx]      = _lengths[i];
                    liveRefs[idx]      = _refCounts[i];
                    idx++;
                }
            }

            // 清空全表
            for (int i = 0; i < Capacity; i++)
            {
                _lengths[i]   = Empty;
                _xxHashKeys[i]  = 0;
                _fnvHashKeys[i] = 0;
                _bytecodes[i]   = default;
                _refCounts[i]   = 0;
            }

            _tombstoneCount = 0;
            _count          = 0;

            // 重插入
            for (int i = 0; i < idx; i++)
            {
                var k = new DualHash64(liveKeys[i].xx, liveKeys[i].fnv);
                int insertSlot = FindInsertSlot(k.XxHash64);
                // 此时全表只有 Empty，FindInsertSlot 必定返回有效 slot
                WriteSlot(insertSlot, k, liveBytecodes[i], liveLens[i]);
                _refCounts[insertSlot] = liveRefs[i];
                _count++;
            }
        }

        // ═══════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════

        private int HashToSlot(ulong xxHash)
        {
            return (int)(xxHash % (ulong)Capacity);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NativeBytecodeCache));
        }

        /// <summary>
        /// 诊断：获取槽位状态字符串（仅调试用）
        /// </summary>
        internal string DumpSlot(int slot)
        {
            lock (_lock)
            {
                int state = _lengths[slot];
                return state switch
                {
                    Empty     => $"[{slot}] Empty",
                    Tombstone => $"[{slot}] Tombstone",
                    >= 0      => $"[{slot}] Key={_xxHashKeys[slot]:X16}{_fnvHashKeys[slot]:X16} Len={state} RefCount={_refCounts[slot]}",
                    _         => $"[{slot}] Unknown({state})"
                };
            }
        }
    }
}
