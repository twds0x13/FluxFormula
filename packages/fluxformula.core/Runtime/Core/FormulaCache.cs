using System;
using System.Runtime.CompilerServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FluxFormula")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FluxFormula.Tests")]

namespace FluxFormula.Core
{
    /// <summary>
    /// 公式编译产物缓存：DualHash64 → (指针 + 长度) 的开放寻址 hashmap。
    /// 单例生命周期内零动态分配——所有数组在构造时一次分配。
    /// </summary>
    /// <remarks>
    /// <para>设计要点：</para>
    /// <list type="bullet">
    ///   <item>开放寻址 + 线性探测，无链表指针，无 GC 压力</item>
    ///   <item>墓碑标记避免驱逐破坏探测链——删除时留墓碑，插入时复用</item>
    ///   <item>默认 2048 槽（可通过 <see cref="FluxConfig"/> 调整），满时环形覆盖最老条目</item>
    ///   <item>键存储为两个独立 ulong[]（xxHash64 + FNV-1a 64），避免 16 字节对齐损失</item>
    ///   <item>值用 IntPtr 存储指针——当 length ≥ 0 时为字节码 (byte*, length)；未来用负数值区分 delegate 缓存</item>
    /// </list>
    /// <para>线程安全：当前无锁。Unity 主线程单线程使用。如需多线程，外层加锁。</para>
    /// </remarks>
    public unsafe class FormulaCache : IFluxCacheProvider
    {
        // ═══════════════════════════════════════════════════════
        // 常量
        // ═══════════════════════════════════════════════════════

        /// <summary>空槽位——从未写入过</summary>
        private const int Empty = -1;

        /// <summary>墓碑——曾经有值但被驱逐</summary>
        private const int Tombstone = -3;

        /// <summary>JIT delegate 缓存槽位标记</summary>
        private const int DelegateSlot = -2;

        /// <summary>
        /// Delegate 条目键空间分离哨兵：XOR xxHash 使 delegate 与 bytecode 条目
        /// 在同一公式哈希下占用不同槽位，避免 PutBytes 覆盖 PutDelegate。
        /// </summary>
        private const ulong DelegateKeySentinel = 0xA3C8F159D6B7E024;

        // ═══════════════════════════════════════════════════════
        // 存储
        // ═══════════════════════════════════════════════════════

        /// <summary>当前槽位数（构造时从 <see cref="FluxConfig"/> 读取）</summary>
        internal readonly int Capacity;

        /// <summary>DualHash64.XxHash64 分量</summary>
        private readonly ulong[] _xxHashKeys;

        /// <summary>DualHash64.FnvHash64 分量</summary>
        private readonly ulong[] _fnvHashKeys;

        /// <summary>值指针：字节码起始地址（byte*）或 delegate 函数指针</summary>
        private readonly IntPtr[] _valuePtrs;

        /// <summary>
        /// 值元数据：
        /// <br/>≥ 0 = 字节码长度（byte count）
        /// <br/>Empty (-1) = 空槽位
        /// <br/>Tombstone (-3) = 已驱逐
        /// <br/>未来预留 -2 = delegate 缓存
        /// </summary>
        private readonly int[] _valueLengths;

        /// <summary>
        /// 字节码自有内存的 GCHandle。
        /// 仅当条目由 <see cref="PutBytes"/> 写入时非 Zero——此时缓存拥有该内存，
        /// 驱逐/覆盖/压缩时需释放 GCHandle。blob 路径和 delegate 条目的此槽位恒为 Zero。
        /// </summary>
        private readonly IntPtr[] _gcHandles;

        /// <summary>当前存活条目数（不含墓碑）</summary>
        private int _count;

        /// <summary>环形写入头：下次驱逐时覆盖此槽位</summary>
        private int _ringHead;

        /// <summary>墓碑计数器——超过阈值触发全表压缩</summary>
        private int _tombstoneCount;

        /// <summary>是否对 TryGet/TryGetDelegate 增量静态 HitCount/MissCount。仅单例实例启用。</summary>
        private bool _trackStats;

        // ═══════════════════════════════════════════════════════
        // 构造
        // ═══════════════════════════════════════════════════════

        public FormulaCache(int capacity = 0)
        {
            Capacity       = capacity > 0 ? capacity : FluxConfig.Current.FormulaCacheCapacity;
            _xxHashKeys   = new ulong[Capacity];
            _fnvHashKeys  = new ulong[Capacity];
            _valuePtrs    = new IntPtr[Capacity];
            _valueLengths = new int[Capacity];
            _gcHandles    = new IntPtr[Capacity];

            for (int i = 0; i < Capacity; i++)
                _valueLengths[i] = Empty;
        }

        // ═══════════════════════════════════════════════════════
        // 查询
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 按 DualHash64 查找缓存的字节码。
        /// </summary>
        /// <returns>true 且 ptr/len 有效；false 表示缓存未命中</returns>
        public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
        {
            int slot = FindSlot(key);

            if (slot >= 0 && _valueLengths[slot] >= 0)
            {
                ptr    = _valuePtrs[slot];
                length = _valueLengths[slot];
                if (_trackStats) HitCount++;
                return true;
            }

            ptr    = IntPtr.Zero;
            length = 0;
            if (_trackStats) MissCount++;
            return false;
        }

        /// <summary>
        /// 在探测链中定位 key 所在的槽位索引。
        /// 返回 ≥ 0 = 命中；返回 -1 = 未找到。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlot(DualHash64 key)
        {
            int start = HashToSlot(key.XxHash64);
            int slot  = start;

            do
            {
                int state = _valueLengths[slot];

                if (state == Empty)
                    return -1;               // 探测链断裂——从未写入过

                if (state != Tombstone)
                {
                    if (_xxHashKeys[slot]  == key.XxHash64 &&
                        _fnvHashKeys[slot] == key.FnvHash64)
                        return slot;         // 命中
                }
                // state == Tombstone: 继续探测（墓碑不阻断裂，仅占位）

                slot = (slot + 1) % Capacity;
            }
            while (slot != start);

            return -1; // 全表扫描完仍未找到
        }

        // ═══════════════════════════════════════════════════════
        // 写入
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 将字节码指针写入缓存。
        /// 若 key 已存在则更新值，否则占用首个空槽位或墓碑。
        /// 缓存满时环形覆盖最老条目。
        /// </summary>
        public void Put(DualHash64 key, IntPtr ptr, int length)
        {
            // 先检查是否已存在——存在则原地更新
            int existingSlot = FindSlot(key);
            if (existingSlot >= 0)
            {
                // 若旧条目持有自有内存（PutBytes 写入的），先释放
                if (_valueLengths[existingSlot] >= 0 && _gcHandles[existingSlot] != IntPtr.Zero)
                    FreeGCHandle(_gcHandles[existingSlot]);
                WriteSlot(existingSlot, key, ptr, length, IntPtr.Zero);
                return;
            }

            // 找插入位置——优先复用墓碑
            int insertSlot = FindInsertSlot(key.XxHash64);

            if (insertSlot >= 0)
            {
                // 空槽位或墓碑——直接写入
                bool wasTombstone = _valueLengths[insertSlot] == Tombstone;
                WriteSlot(insertSlot, key, ptr, length);
                if (!wasTombstone) _count++;
                else               _tombstoneCount--;
            }
            else
            {
                // 全表满（无空槽位/墓碑）——环形驱逐
                EvictAndWrite(key, ptr, length);
            }
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
                int state = _valueLengths[slot];

                if (state == Empty)
                {
                    // 空槽位最优——直接返回。但有墓碑时优先回收墓碑。
                    return firstTombstone >= 0 ? firstTombstone : slot;
                }

                if (state == Tombstone && firstTombstone < 0)
                {
                    firstTombstone = slot;    // 记住第一个墓碑，继续看后面有没有空槽位
                }

                slot = (slot + 1) % Capacity;
            }
            while (slot != start);

            // 全表扫描完毕：优先用墓碑
            return firstTombstone;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSlot(int slot, DualHash64 key, IntPtr ptr, int length, IntPtr gcHandle = default)
        {
            _xxHashKeys[slot]   = key.XxHash64;
            _fnvHashKeys[slot]  = key.FnvHash64;
            _valuePtrs[slot]    = ptr;
            _valueLengths[slot] = length;
            _gcHandles[slot]    = gcHandle;
        }

        /// <summary>
        /// 将字节数组钉住（pinned）后写入缓存。缓存获取该内存的所有权——
        /// 驱逐、覆盖或压缩时自动释放 GCHandle。
        /// 调用方无需保留对数组的引用。
        /// </summary>
        public void PutBytes(DualHash64 key, byte[] bytes)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            IntPtr ptr = handle.AddrOfPinnedObject();
            IntPtr gcHandle = System.Runtime.InteropServices.GCHandle.ToIntPtr(handle);

            // 已存在 → 原地更新，先释放旧值
            int existingSlot = FindSlot(key);
            if (existingSlot >= 0)
            {
                if (_valueLengths[existingSlot] == DelegateSlot)
                    FreeGCHandle(_valuePtrs[existingSlot]);
                else if (_valueLengths[existingSlot] >= 0 && _gcHandles[existingSlot] != IntPtr.Zero)
                    FreeGCHandle(_gcHandles[existingSlot]);
                WriteSlot(existingSlot, key, ptr, bytes.Length, gcHandle);
                return;
            }

            // 找插入位置
            int insertSlot = FindInsertSlot(key.XxHash64);

            if (insertSlot >= 0)
            {
                bool wasTombstone = _valueLengths[insertSlot] == Tombstone;
                WriteSlot(insertSlot, key, ptr, bytes.Length, gcHandle);
                if (!wasTombstone) _count++;
                else               _tombstoneCount--;
            }
            else
            {
                EvictAndWrite(key, ptr, bytes.Length, gcHandle);
            }
        }

        // ═══════════════════════════════════════════════════════
        // Delegate 缓存
        // ═══════════════════════════════════════════════════════

        /// <inheritdoc cref="IFluxCacheProvider.TryGetDelegate"/>
        public bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
        {
            // Delegate 条目使用独立键空间，避免与 bytecode 条目冲突
            var delegateKey = MakeDelegateKey(key);
            int slot = FindSlot(delegateKey);

            if (slot >= 0 && _valueLengths[slot] == DelegateSlot)
            {
                gcHandle = _valuePtrs[slot];
                if (_trackStats) HitCount++;
                return true;
            }

            gcHandle = IntPtr.Zero;
            if (_trackStats) MissCount++;
            return false;
        }

        /// <inheritdoc cref="IFluxCacheProvider.PutDelegate"/>
        public void PutDelegate(DualHash64 key, IntPtr gcHandle)
        {
            // Delegate 条目使用独立键空间，避免与 bytecode 条目冲突
            var delegateKey = MakeDelegateKey(key);

            // 已存在 → 原地更新，先释放旧的 GCHandle
            int existingSlot = FindSlot(delegateKey);
            if (existingSlot >= 0)
            {
                if (_valueLengths[existingSlot] == DelegateSlot)
                    FreeGCHandle(_valuePtrs[existingSlot]);
                else if (_valueLengths[existingSlot] >= 0 && _gcHandles[existingSlot] != IntPtr.Zero)
                    FreeGCHandle(_gcHandles[existingSlot]);

                WriteSlot(existingSlot, delegateKey, gcHandle, DelegateSlot);
                return;
            }

            // 找插入位置
            int insertSlot = FindInsertSlot(delegateKey.XxHash64);

            if (insertSlot >= 0)
            {
                bool wasTombstone = _valueLengths[insertSlot] == Tombstone;
                WriteSlot(insertSlot, delegateKey, gcHandle, DelegateSlot);
                if (!wasTombstone) _count++;
                else               _tombstoneCount--;
            }
            else
            {
                EvictAndWrite(delegateKey, gcHandle, DelegateSlot);
            }
        }

        /// <summary>
        /// 将公式字节码哈希映射到 delegate 独立键空间：XOR 两个分量。
        /// 调用方仍传入原始 DualHash64——此方法在内部做变换，
        /// 使 delegate 与 bytecode（同公式哈希）占用不同槽位。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DualHash64 MakeDelegateKey(DualHash64 formulaKey)
        {
            return new DualHash64(
                formulaKey.XxHash64 ^ DelegateKeySentinel,
                formulaKey.FnvHash64 ^ DelegateKeySentinel);
        }

        // ═══════════════════════════════════════════════════════
        // GCHandle 生命周期
        // ═══════════════════════════════════════════════════════

        /// <summary>安全释放 GCHandle（无异常，IsAllocated 为 false 时跳过）</summary>
        private static void FreeGCHandle(IntPtr gcHandlePtr)
        {
            try
            {
                var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(gcHandlePtr);
                if (handle.IsAllocated)
                    handle.Free();
            }
            catch
            {
                // GCHandle 可能已被释放或损坏——静默忽略
            }
        }

        // ═══════════════════════════════════════════════════════
        // 驱逐
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 环形驱逐写入：覆盖 _ringHead 槽位，标记旧条目为墓碑后前移。
        /// 然后对受影响探测链做后向移位修复。
        /// </summary>
        private void EvictAndWrite(DualHash64 key, IntPtr ptr, int length, IntPtr gcHandle = default)
        {
            int victim = _ringHead;
            _ringHead  = (_ringHead + 1) % Capacity;

            // 标记被驱逐的槽位为墓碑（含活条目时）
            int oldState = _valueLengths[victim];
            if (oldState >= 0)
            {
                // 若为自有内存（PutBytes 写入），释放 GCHandle
                if (_gcHandles[victim] != IntPtr.Zero)
                    FreeGCHandle(_gcHandles[victim]);
                _gcHandles[victim]    = IntPtr.Zero;
                _valueLengths[victim] = Tombstone;
                _tombstoneCount++;
                _count--;
            }
            else if (oldState == DelegateSlot)
            {
                FreeGCHandle(_valuePtrs[victim]);
                _valueLengths[victim] = Tombstone;
                _tombstoneCount++;
                _count--;
            }
            // 若 victim 原本就是墓碑或空——直接覆盖，不需额外操作

            // 写入新值
            WriteSlot(victim, key, ptr, length, gcHandle);
            _count++;

            // 墓碑过多时全量压缩
            if (_tombstoneCount > Capacity / 4)
                Compact();
        }

        /// <summary>
        /// 全表压缩：移除所有墓碑，重排条目使其探测链完整。
        /// 在所有墓碑上调用相当于全表 rehash。
        /// </summary>
        private void Compact()
        {
            // 收集所有存活条目
            var liveKeys    = new (ulong xx, ulong fnv)[_count];
            var livePtrs    = new IntPtr[_count];
            var liveLens    = new int[_count];
            var liveHandles = new IntPtr[_count];
            int idx = 0;

            for (int i = 0; i < Capacity; i++)
            {
                int state = _valueLengths[i];
                if (state >= 0 || state == DelegateSlot) // 存活条目（字节码 或 delegate）
                {
                    liveKeys[idx]    = (_xxHashKeys[i], _fnvHashKeys[i]);
                    livePtrs[idx]    = _valuePtrs[i];
                    liveLens[idx]    = state;
                    liveHandles[idx] = _gcHandles[i];
                    idx++;
                }
                else if (state == Tombstone)
                {
                    // Tombstone 无数据需释放，跳过
                }
            }

            // 清空全表
            for (int i = 0; i < Capacity; i++)
            {
                _valueLengths[i] = Empty;
                _xxHashKeys[i]   = 0;
                _fnvHashKeys[i]  = 0;
                _valuePtrs[i]    = IntPtr.Zero;
                _gcHandles[i]    = IntPtr.Zero;
            }

            _tombstoneCount = 0;
            _count          = 0;

            // 重插入
            for (int i = 0; i < idx; i++)
            {
                var k = new DualHash64(liveKeys[i].xx, liveKeys[i].fnv);
                int insertSlot = FindInsertSlot(k.XxHash64);
                // 此时全表只有 Empty，FindInsertSlot 必定返回有效 slot
                WriteSlot(insertSlot, k, livePtrs[i], liveLens[i], liveHandles[i]);
                _count++;
            }
        }

        // ═══════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════

        /// <summary>当前存活条目数</summary>
        public int Count => _count;

        /// <summary>当前墓碑数（诊断用）</summary>
        public int TombstoneCount => _tombstoneCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int HashToSlot(ulong xxHash)
        {
            return (int)(xxHash % (ulong)Capacity);
        }

        /// <summary>
        /// 诊断：获取槽位状态字符串（仅调试用）
        /// </summary>
        internal string DumpSlot(int slot)
        {
            int state = _valueLengths[slot];
            return state switch
            {
                Empty      => $"[{slot}] Empty",
                Tombstone  => $"[{slot}] Tombstone",
                >= 0       => $"[{slot}] Key={_xxHashKeys[slot]:X16}{_fnvHashKeys[slot]:X16} Len={state}",
                _          => $"[{slot}] Unknown({state})"
            };
        }

        // ═══════════════════════════════════════════════════════
        // 静态单例（替代 ConnectCache）
        // ═══════════════════════════════════════════════════════

        private static FormulaCache _instance;

        /// <summary>
        /// 全局单例缓存实例。首次访问时延迟初始化。
        /// 替代已移除的 <see cref="ConnectCache"/>。
        /// </summary>
        public static FormulaCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FormulaCache();
                    _instance._trackStats = true;
                }
                return _instance;
            }
        }

        /// <summary>缓存命中计数（诊断用）。仅单例实例增量。</summary>
        public static long HitCount { get; private set; }

        /// <summary>缓存未命中计数（诊断用）。仅单例实例增量。</summary>
        public static long MissCount { get; private set; }

        /// <summary>
        /// 重置单例缓存：创建全新的 <see cref="FormulaCache"/> 实例，清零所有计数器。
        /// 所有旧缓存条目（blob 字节码指针、JIT delegate）均被丢弃。
        /// </summary>
        public static void Reset()
        {
            _instance = new FormulaCache();
            _instance._trackStats = true;
            HitCount = 0;
            MissCount = 0;
        }
    }
}
