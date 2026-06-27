using System;
using FluxFormula.Core;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace FluxFormula.Burst.Tests
{
    public unsafe class NativeBytecodeCacheTests
    {
        private NativeBytecodeCache _cache;

        [SetUp]
        public void SetUp()
        {
            _cache = new NativeBytecodeCache();
        }

        [TearDown]
        public void TearDown()
        {
            _cache.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // Acquire / Release 基本
        // ═══════════════════════════════════════════════════════

        [Test]
        public void AcquireRelease_RoundTrip()
        {
            byte[] src = { 1, 2, 3, 4 };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));

            var na = _cache.Acquire(hash, src);
            Assert.That(na.IsCreated, Is.True);
            Assert.That(na.Length, Is.EqualTo(4));
            Assert.That(na[0], Is.EqualTo(1));
            Assert.That(na[3], Is.EqualTo(4));

            _cache.Release(hash);
            Assert.That(_cache.Count, Is.EqualTo(1),
                "Release 不驱逐条目，Count 应保持");
        }

        [Test]
        public void Acquire_SameHashTwice_ReturnsSamePointer()
        {
            byte[] src = { 10, 20, 30 };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));

            var na1 = _cache.Acquire(hash, src);
            var na2 = _cache.Acquire(hash, src);

            unsafe
            {
                var p1 = na1.GetUnsafePtr();
                var p2 = na2.GetUnsafePtr();
                Assert.That(p1, Is.EqualTo(p2),
                    "同 hash 的两次 Acquire 应返回同一块 NativeArray 内存");
            }

            _cache.Release(hash);
            _cache.Release(hash);
        }

        [Test]
        public void Acquire_Hit_IncrementsRefCount()
        {
            byte[] src = { 0xFF };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));

            _cache.Acquire(hash, src);  // refCount = 1
            _cache.Acquire(hash, src);  // refCount = 2
            // 不抛异常即通过——第二次 Acquire 应命中缓存

            _cache.Release(hash);
            _cache.Release(hash);
        }

        [Test]
        public void Acquire_DifferentHashes_IndependentEntries()
        {
            byte[] src1 = { 1 };
            byte[] src2 = { 2 };
            var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(src1));
            var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(src2));

            var na1 = _cache.Acquire(hash1, src1);
            var na2 = _cache.Acquire(hash2, src2);

            Assert.That(_cache.Count, Is.EqualTo(2));

            unsafe
            {
                Assert.That(na1.GetUnsafePtr(), Is.Not.EqualTo(na2.GetUnsafePtr()),
                    "不同 hash 应返回不同的 NativeArray");
            }

            _cache.Release(hash1);
            _cache.Release(hash2);
        }

        // ═══════════════════════════════════════════════════════
        // Release 行为
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Release_UnknownHash_NoOp()
        {
            var hash = new DualHash64(0xDEAD, 0xBEEF);
            Assert.That(() => _cache.Release(hash), Throws.Nothing,
                "Release 未知 hash 应静默无操作");
        }

        [Test]
        public void Release_OnDisposedCache_NoOp()
        {
            byte[] src = { 1 };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
            _cache.Acquire(hash, src);
            _cache.Dispose();

            Assert.That(() => _cache.Release(hash), Throws.Nothing,
                "已 Dispose 的缓存上 Release 应静默无操作");
        }

        // ═══════════════════════════════════════════════════════
        // 多条目
        // ═══════════════════════════════════════════════════════

        [Test]
        public void MultipleEntries_AllRetrievable()
        {
            const int N = 32;
            var hashes = new DualHash64[N];
            var arrays = new NativeArray<byte>[N];

            for (int i = 0; i < N; i++)
            {
                byte[] src = BitConverter.GetBytes(i);
                hashes[i] = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                arrays[i] = _cache.Acquire(hashes[i], src);

                Assert.That(arrays[i].IsCreated, Is.True);
                unsafe
                {
                    Assert.That(*(int*)arrays[i].GetUnsafePtr(), Is.EqualTo(i),
                        $"条目 {i} 的字节码应包含正确的值");
                }
            }

            Assert.That(_cache.Count, Is.EqualTo(N));

            for (int i = 0; i < N; i++)
                _cache.Release(hashes[i]);
        }

        // ═══════════════════════════════════════════════════════
        // 驱逐
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Overflow_EvictsUnreferencedEntry()
        {
            int cap = _cache.Capacity;
            var hashes = new DualHash64[cap + 1];

            // 先 Acquire 满 Capacity 个条目，然后全部 Release
            for (int i = 0; i < cap; i++)
            {
                byte[] src = BitConverter.GetBytes(i * 13);
                hashes[i] = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                _cache.Acquire(hashes[i], src);
                _cache.Release(hashes[i]);  // refCount → 0，变为可驱逐
            }

            Assert.That(_cache.Count, Is.EqualTo(cap));

            // 再写一个——触发驱逐
            byte[] extraSrc = BitConverter.GetBytes(99999);
            var extraHash = DualHash64.Compute(new ReadOnlySpan<byte>(extraSrc));
            var extraNa = _cache.Acquire(extraHash, extraSrc);

            Assert.That(extraNa.IsCreated, Is.True);
            Assert.That(extraNa[0], Is.EqualTo(extraSrc[0]));
            Assert.That(_cache.Count, Is.LessThanOrEqualTo(cap),
                "驱逐后 Count 不应超过 Capacity");

            _cache.Release(extraHash);
        }

        [Test]
        public void Overflow_SkipsReferencedEntry()
        {
            // 创建一个小容量缓存来加速测试
            using var smallCache = new NativeBytecodeCache(capacity: 8);

            // 填满缓存（7 个 refCount=0 + 1 个 refCount=1）
            var pinnedSrc = new byte[] { 42 };
            var pinnedHash = DualHash64.Compute(new ReadOnlySpan<byte>(pinnedSrc));
            var pinnedNa = smallCache.Acquire(pinnedHash, pinnedSrc);
            // pinnedNa refCount = 1 —— 不可驱逐

            for (int i = 0; i < 7; i++)
            {
                byte[] src = BitConverter.GetBytes(i);
                var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                smallCache.Acquire(hash, src);
                smallCache.Release(hash); // refCount → 0
            }

            Assert.That(smallCache.Count, Is.EqualTo(8),
                "缓存应满 (8 条目)");

            // 再写入一轮——应驱逐 refCount=0 的条目，跳过 pinned 条目
            for (int i = 0; i < 4; i++)
            {
                byte[] src = BitConverter.GetBytes(i + 100);
                var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                smallCache.Acquire(hash, src);
                smallCache.Release(hash);
            }

            // pinned 条目应仍然可检索
            var reacquired = smallCache.Acquire(pinnedHash, pinnedSrc);
            unsafe
            {
                Assert.That(reacquired.GetUnsafePtr(), Is.EqualTo(pinnedNa.GetUnsafePtr()),
                    "被引用的条目不应被驱逐——指针应不变");
            }

            smallCache.Release(pinnedHash);
            smallCache.Release(pinnedHash); // 第二次 Acquire 的那次
        }

        // ═══════════════════════════════════════════════════════
        // 驱逐：全表被引用时抛异常
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Overflow_AllReferenced_Throws()
        {
            using var tinyCache = new NativeBytecodeCache(capacity: 4);

            // 全部条目保持引用（不 Release）
            for (int i = 0; i < 4; i++)
            {
                byte[] src = BitConverter.GetBytes(i);
                var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                tinyCache.Acquire(hash, src);
                // 不 Release —— refCount 全为 1
            }

            // 第 5 个写入应抛异常
            byte[] overflowSrc = BitConverter.GetBytes(999);
            var overflowHash = DualHash64.Compute(new ReadOnlySpan<byte>(overflowSrc));

            Assert.That(
                () => tinyCache.Acquire(overflowHash, overflowSrc),
                Throws.InvalidOperationException,
                "全表被引用且无空槽位时应抛出 InvalidOperationException");
        }

        // ═══════════════════════════════════════════════════════
        // Dispose
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Dispose_FreesAllEntries()
        {
            byte[] src = { 7, 7, 7 };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
            var na = _cache.Acquire(hash, src);

            _cache.Dispose();

            // NativeArray 在 Dispose 后变成无效（IsCreated == false）
            Assert.That(na.IsCreated, Is.False,
                "缓存 Dispose 后 NativeArray 应被释放");
        }

        [Test]
        public void Dispose_Idempotent()
        {
            _cache.Dispose();
            Assert.That(() => _cache.Dispose(), Throws.Nothing,
                "重复 Dispose 不应抛异常");
        }

        [Test]
        public void Acquire_AfterDispose_Throws()
        {
            _cache.Dispose();

            byte[] src = { 1 };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));

            Assert.That(
                () => _cache.Acquire(hash, src),
                Throws.ObjectDisposedException,
                "Dispose 后 Acquire 应抛出 ObjectDisposedException");
        }

        // ═══════════════════════════════════════════════════════
        // 空缓存
        // ═══════════════════════════════════════════════════════

        [Test]
        public void EmptyCache_Properties()
        {
            Assert.That(_cache.Count, Is.EqualTo(0));
            Assert.That(_cache.TombstoneCount, Is.EqualTo(0));
            Assert.That(_cache.Capacity, Is.EqualTo(64),
                "默认容量应为 FluxConfig 的 64");
        }

        [Test]
        public void EmptyCache_Acquire_CreatesEntry()
        {
            byte[] src = { 0xAA };
            var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));

            var na = _cache.Acquire(hash, src);
            Assert.That(na.IsCreated, Is.True);
            Assert.That(_cache.Count, Is.EqualTo(1));

            _cache.Release(hash);
        }

        // ═══════════════════════════════════════════════════════
        // 容量配置
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Capacity_RespectsConstructor()
        {
            using var c = new NativeBytecodeCache(capacity: 128);
            Assert.That(c.Capacity, Is.EqualTo(128));
        }

        [Test]
        public void Capacity_UsesFluxConfig_WhenZero()
        {
            // 默认构造使用 FluxConfig.Current.NativeBytecodeCacheCapacity (64)
            Assert.That(_cache.Capacity, Is.EqualTo(64));
        }

        // ═══════════════════════════════════════════════════════
        // DualHash64 双分量区分
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Keys_SameXxHash_DifferentFnv_AreDistinct()
        {
            byte[] src = { 1 };
            var k1 = new DualHash64(0xABCDEF0123456789UL, 0x1111111111111111UL);
            var k2 = new DualHash64(0xABCDEF0123456789UL, 0x2222222222222222UL);

            _cache.Acquire(k1, src);
            _cache.Acquire(k2, src);

            Assert.That(_cache.Count, Is.EqualTo(2),
                "xxHash 相同但 FNV 不同的两个 key 应占不同槽位");

            _cache.Release(k1);
            _cache.Release(k2);
        }

        // ═══════════════════════════════════════════════════════
        // 墓碑回收
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Tombstones_AreCompacted()
        {
            using var smallCache = new NativeBytecodeCache(capacity: 16);

            // 填满
            var hashes = new DualHash64[16];
            for (int i = 0; i < 16; i++)
            {
                byte[] src = BitConverter.GetBytes(i);
                hashes[i] = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                smallCache.Acquire(hashes[i], src);
                smallCache.Release(hashes[i]); // refCount → 0
            }

            // 再写入一轮（触发驱逐 + 墓碑）
            for (int i = 0; i < 8; i++)
            {
                byte[] src = BitConverter.GetBytes(i + 100);
                var hash = DualHash64.Compute(new ReadOnlySpan<byte>(src));
                smallCache.Acquire(hash, src);
                smallCache.Release(hash);
            }

            // Compact 应在 tombstoneCount > Capacity/4 (4) 时触发
            Assert.That(smallCache.TombstoneCount, Is.LessThan(4 + 1),
                "压缩后墓碑数不应超过阈值");
        }
    }
}
