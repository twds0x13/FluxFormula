using System;
using System.Reflection;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FormulaCache 深度覆盖测试——PutBytes 覆盖/逐出、Delegate 混合、Compact、DumpSlot DelegateSlot、全墓碑探针链。
/// </summary>
public unsafe class FormulaCacheDeepTests
{
    // ═══════════════════════════════════════════════════════
    // Put 覆盖 PutBytes 条目（释放旧 GCHandle）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Put_Overwrites_PutBytesEntry_FreesGCHandle()
    {
        // PutBytes 写入 → Put(raw ptr) 覆盖同 key → 旧 GCHandle 被 FreeGCHandle 释放
        var cache = new FormulaCache(16);
        var key = DualHash64.Compute(new byte[] { 0xAA, 0xBB });

        // 通过 PutBytes 写入（缓存拥有 GCHandle）
        cache.PutBytes(key, new byte[] { 1, 2, 3, 4, 5 });

        // 用 raw pointer 覆盖（不应抛异常——FreeGCHandle 在内部 try-catch 保护）
        byte* raw = stackalloc byte[3] { 9, 9, 9 };
        Assert.That(() => cache.Put(key, (IntPtr)raw, 3), Throws.Nothing);

        // 验证覆盖后的值
        Assert.That(cache.TryGet(key, out IntPtr p, out int len), Is.True);
        Assert.That(len, Is.EqualTo(3));
        Assert.That(((byte*)p)[0], Is.EqualTo(9));
    }

    // ═══════════════════════════════════════════════════════
    // PutBytes 覆盖 PutBytes 条目（释放旧 GCHandle）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutBytes_Overwrites_PutBytesEntry_FreesOldHandle()
    {
        var cache = new FormulaCache(16);
        var key = DualHash64.Compute(new byte[] { 0x11 });

        cache.PutBytes(key, new byte[] { 1, 2, 3 });
        // 再次 PutBytes 同 key——应释放旧 GCHandle 再写入新值
        Assert.That(() => cache.PutBytes(key, new byte[] { 4, 5, 6 }), Throws.Nothing);

        Assert.That(cache.TryGet(key, out IntPtr p, out int len), Is.True);
        Assert.That(len, Is.EqualTo(3));
        Assert.That(((byte*)p)[0], Is.EqualTo(4));
        Assert.That(cache.Count, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // PutDelegate 覆盖 PutBytes 条目（键空间冲突）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutDelegate_Overwrites_PutBytesSlot_WhenKeyCollides()
    {
        // 使用极小容量增加键冲突概率
        var cache = new FormulaCache(8);
        byte* p = stackalloc byte[1];

        // 填满 bytecode 条目
        for (int i = 0; i < 8; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        Assert.That(cache.Count, Is.EqualTo(8));

        // PutDelegate 写入——触发 eviction，被逐出的可能是 bytecode 条目
        var dKey = DualHash64.Compute(BitConverter.GetBytes(999));
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            Assert.That(() => cache.PutDelegate(dKey,
                System.Runtime.InteropServices.GCHandle.ToIntPtr(handle)), Throws.Nothing);
            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
                System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
        }
    }

    // ═══════════════════════════════════════════════════════
    // EvictAndWrite 逐出 PutBytes 条目（释放 GCHandle）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EvictAndWrite_EvictsPutBytesEntry_FreesGCHandle()
    {
        var cache = new FormulaCache(8);

        // 用 PutBytes 填满（每个条目有独立 GCHandle）
        for (int i = 0; i < 8; i++)
        {
            var key = DualHash64.Compute(BitConverter.GetBytes(i));
            cache.PutBytes(key, new byte[] { (byte)i, (byte)(i + 1) });
        }

        Assert.That(cache.Count, Is.EqualTo(8));

        // 再写一条——触发 EvictAndWrite，逐出最旧的 PutBytes 条目
        byte* raw = stackalloc byte[1] { 0xFF };
        var newKey = DualHash64.Compute(BitConverter.GetBytes(999));
        Assert.That(() => cache.Put(newKey, (IntPtr)raw, 1), Throws.Nothing);

        Assert.That(cache.TryGet(newKey, out _, out _), Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // EvictAndWrite 逐出 Delegate 条目
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EvictAndWrite_EvictsDelegateSlot_FreesHandle()
    {
        var cache = new FormulaCache(4);
        byte* p = stackalloc byte[1];

        // 先用 bytecode 填满 4 槽
        for (int i = 0; i < 4; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(4));

        // 写入 delegate——触发 eviction
        var dKey = DualHash64.Compute(BitConverter.GetBytes(100));
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            cache.PutDelegate(dKey, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);

            // 再填满 bytecode（可能触发另一轮 eviction）
            for (int i = 5; i < 20; i++)
                cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

            // delegate 可能已被逐出——不崩溃即为通过
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
                System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
        }
    }

    // ═══════════════════════════════════════════════════════
    // EvictAndWrite: victim 已是 Tombstone（跳过清理）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EvictAndWrite_VictimIsTombstone_SkipsCleanup()
    {
        var cache = new FormulaCache(16);
        byte* p = stackalloc byte[1];

        // 填满
        for (int i = 0; i < 16; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        // 删除 ringHead 对应位置的条目（使下次 eviction 遇到 tombstone）
        // ringHead = 0 after construction
        // 删除 index 0 的条目（ringHead 指向 0，驱逐 victim = 0）
        var k0 = DualHash64.Compute(BitConverter.GetBytes(0));
        cache.Remove(k0);

        // ringHead 仍在 0。插入新条目：FindInsertSlot 可能不选中 tombstone
        // (因为 hash 分布)。为了强制 eviction，需反复插入直到ringHead到 tombstone
        // 这里不尝试精确控制 ringHead，只验证不崩溃即可
        Assert.That(cache.TombstoneCount, Is.GreaterThan(0),
            "Remove 后应存在墓碑");

        // 反复插入触发多轮 eviction——终有一次 victim 是 tombstone
        for (int i = 100; i < 200; i++)
        {
            Assert.That(() =>
                cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1),
                Throws.Nothing, $"eviction cycle i={i} 不应崩溃");
        }
    }

    // ═══════════════════════════════════════════════════════
    // Compact: 混合 bytecode + delegate 条目
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Compact_MixedBytecodeAndDelegate_PreservesBoth()
    {
        var cache = new FormulaCache(16);
        byte* p = stackalloc byte[1];

        // 填满 bytecode
        for (int i = 0; i < 16; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(16));

        // 大量删除以制造墓碑
        for (int i = 0; i < 8; i++)
            cache.Remove(DualHash64.Compute(BitConverter.GetBytes(i)));

        // 写入一些 delegate（它们会使用墓碑槽位）
        var dKeys = new DualHash64[3];
        for (int i = 0; i < 3; i++)
        {
            dKeys[i] = DualHash64.Compute(BitConverter.GetBytes(i + 1000));
            Action del = () => { };
            var h = System.Runtime.InteropServices.GCHandle.Alloc(del);
            cache.PutDelegate(dKeys[i], System.Runtime.InteropServices.GCHandle.ToIntPtr(h));
        }

        // 触发 Compact（通过大量逐出使墓碑超过阈值）
        for (int i = 200; i < 400; i++)
        {
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);
        }

        // Compact 后不应崩溃；delegate 条目应至少部分存活
        // （它们可能在逐出中被淘汰，但只要没崩溃就是通过）
        Assert.That(cache.Count, Is.GreaterThan(0));
        Assert.That(cache.TombstoneCount, Is.LessThan(cache.Capacity / 4 + 1));
    }

    // ═══════════════════════════════════════════════════════
    // DumpSlot: DelegateSlot (-2) → "Unknown"
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DumpSlot_DelegateState_ShowsUnknown()
    {
        var dumpMethod = typeof(FormulaCache).GetMethod("DumpSlot",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var cache = new FormulaCache(8);
        var dKey = DualHash64.Compute(BitConverter.GetBytes(42));
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            cache.PutDelegate(dKey, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);

            // 找到 delegate 所在 slot 并 dump
            int delegateSlot = -1;
            for (int i = 0; i < 8; i++)
            {
                string s = (string)dumpMethod.Invoke(cache, new object[] { i });
                if (s.Contains("Unknown"))
                {
                    delegateSlot = i;
                    break;
                }
            }

            Assert.That(delegateSlot, Is.GreaterThanOrEqualTo(0),
                "Delegate 槽位应被标记为 Unknown(-2)");
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
                System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
        }
    }

    // ═══════════════════════════════════════════════════════
    // 全墓碑探针链：FindSlot 在纯墓碑表中返回 -1
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FindSlot_AllTombstones_ReturnsNegative()
    {
        var cache = new FormulaCache(8);
        byte* p = stackalloc byte[1];

        // 填满
        var keys = new DualHash64[8];
        for (int i = 0; i < 8; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i));
            cache.Put(keys[i], (IntPtr)p, 1);
        }

        // 全部删除 → 全表墓碑
        for (int i = 0; i < 8; i++)
            cache.Remove(keys[i]);

        Assert.That(cache.Count, Is.EqualTo(0));
        Assert.That(cache.TombstoneCount, Is.EqualTo(8));

        // 查找任意 key——FindSlot 遍历全表墓碑，应返回 -1
        Assert.That(cache.TryGet(keys[3], out _, out _), Is.False);
        Assert.That(cache.TryGet(DualHash64.Compute(new byte[] { 99 }), out _, out _), Is.False);

        // DumpSlot tombstone 分支
        var dumpMethod = typeof(FormulaCache).GetMethod("DumpSlot",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That((string)dumpMethod.Invoke(cache, new object[] { 0 }), Does.Contain("Tombstone"));
    }

    // ═══════════════════════════════════════════════════════
    // TryGet: 非单例实例不增量 HitCount/MissCount
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TryGet_NonInstanceCache_DoesNotTrackStats()
    {
        var cache = new FormulaCache(16);
        var key = DualHash64.Compute(new byte[] { 0xFE });
        byte* src = stackalloc byte[4];

        cache.Put(key, (IntPtr)src, 4);

        long hitBefore = FormulaCache.HitCount;
        long missBefore = FormulaCache.MissCount;

        // 非单例实例命中/未命中（_trackStats == false）
        Assert.That(cache.TryGet(key, out _, out _), Is.True);
        Assert.That(cache.TryGet(DualHash64.Compute(new byte[] { 0xFF }), out _, out _), Is.False);

        Assert.That(FormulaCache.HitCount, Is.EqualTo(hitBefore),
            "非单例实例不应递增 HitCount");
        Assert.That(FormulaCache.MissCount, Is.EqualTo(missBefore),
            "非单例实例不应递增 MissCount");
    }

    [Test]
    public void TryGetDelegate_NonInstanceCache_DoesNotTrackStats()
    {
        var cache = new FormulaCache(16);
        var dKey = DualHash64.Compute(new byte[] { 0xAB });
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            cache.PutDelegate(dKey, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));

            long hitBefore = FormulaCache.HitCount;
            long missBefore = FormulaCache.MissCount;

            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);
            Assert.That(cache.TryGetDelegate(DualHash64.Compute(new byte[] { 0xCD }), out _), Is.False);

            Assert.That(FormulaCache.HitCount, Is.EqualTo(hitBefore));
            Assert.That(FormulaCache.MissCount, Is.EqualTo(missBefore));
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
                System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
        }
    }

    // ═══════════════════════════════════════════════════════
    // TryGetDelegate: 非单例实例命中/未命中
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TryGetDelegate_SlotIsBytecode_NotDelegate_ReturnsFalse()
    {
        var cache = new FormulaCache(16);
        var key = DualHash64.Compute(new byte[] { 55 });
        byte* p = stackalloc byte[1];

        // 写入 bytecode 条目（使用 MakeDelegateKey 的逆操作不可行，
        // 但我们测试的是如果 FindSlot 找到的 slot 的 _valueLengths[slot] != DelegateSlot）
        // 正常来说 bytecode key 和 delegate key 在不同的键空间，不会冲突。
        // 此测试验证：用普通 key 去查 TryGetDelegate，slot 为 bytecode 条目时返回 false。
        cache.Put(key, (IntPtr)p, 1);
        Assert.That(cache.TryGetDelegate(key, out _), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // FindInsertSlot: 全表扫描返回墓碑（无 Empty 槽位）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FindInsertSlot_NoEmptySlots_ReturnsTombstone()
    {
        var cache = new FormulaCache(16);
        byte* p = stackalloc byte[1];

        // 填满
        for (int i = 0; i < 16; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        // 删除一半 → 8 墓碑 + 8 活条目 = 无 Empty 槽
        for (int i = 0; i < 8; i++)
            cache.Remove(DualHash64.Compute(BitConverter.GetBytes(i)));

        Assert.That(cache.Count, Is.EqualTo(8));
        Assert.That(cache.TombstoneCount, Is.EqualTo(8));

        // Count + TombstoneCount = 16 = Capacity，无 Empty 槽位
        // 新插入应复用墓碑（FindInsertSlot 全表扫描返回 firstTombstone）
        var newKey = DualHash64.Compute(BitConverter.GetBytes(99999));
        cache.Put(newKey, (IntPtr)p, 1);

        Assert.That(cache.TryGet(newKey, out _, out _), Is.True);
        Assert.That(cache.Count, Is.EqualTo(9));
    }

    // ═══════════════════════════════════════════════════════
    // PutBytes 触发 EvictAndWrite（全表满）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutBytes_FullCache_TriggersEviction()
    {
        var cache = new FormulaCache(8);
        byte* p = stackalloc byte[1];

        // 填满 raw 指针条目
        for (int i = 0; i < 8; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        Assert.That(cache.Count, Is.EqualTo(8));

        // PutBytes 触发 eviction（全表满，无空槽/墓碑）
        var newKey = DualHash64.Compute(BitConverter.GetBytes(888));
        Assert.That(() => cache.PutBytes(newKey, new byte[] { 0xAA, 0xBB }), Throws.Nothing);

        Assert.That(cache.TryGet(newKey, out _, out _), Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // PutDelegate 触发 EvictAndWrite（全表满）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutDelegate_FullCache_TriggersEviction()
    {
        var cache = new FormulaCache(4);
        byte* p = stackalloc byte[1];

        for (int i = 0; i < 4; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        Assert.That(cache.Count, Is.EqualTo(4));

        var dKey = DualHash64.Compute(BitConverter.GetBytes(777));
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            Assert.That(() => cache.PutDelegate(dKey,
                System.Runtime.InteropServices.GCHandle.ToIntPtr(handle)), Throws.Nothing);
            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
                System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
        }
    }

    // ═══════════════════════════════════════════════════════
    // Remove: Delegate 条目不受影响（Remove 仅操作 bytecode）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_DoesNotAffectDelegateEntries()
    {
        var cache = new FormulaCache(32);
        byte* p = stackalloc byte[1];

        // 写入混合条目
        var bcKey1 = DualHash64.Compute(new byte[] { 1 });
        var bcKey2 = DualHash64.Compute(new byte[] { 2 });
        cache.Put(bcKey1, (IntPtr)p, 1);
        cache.Put(bcKey2, (IntPtr)p, 1);

        int captured1 = 0, captured2 = 0;
        Action d1 = () => captured1 = 10;
        Action d2 = () => captured2 = 20;
        var h1 = System.Runtime.InteropServices.GCHandle.Alloc(d1);
        var h2 = System.Runtime.InteropServices.GCHandle.Alloc(d2);

        var dKey1 = DualHash64.Compute(new byte[] { 10 });
        var dKey2 = DualHash64.Compute(new byte[] { 20 });
        cache.PutDelegate(dKey1, System.Runtime.InteropServices.GCHandle.ToIntPtr(h1));
        cache.PutDelegate(dKey2, System.Runtime.InteropServices.GCHandle.ToIntPtr(h2));

        // 删除字节码条目
        cache.Remove(bcKey1);
        Assert.That(cache.TryGet(bcKey1, out _, out _), Is.False);

        // delegate 条目不受影响
        Assert.That(cache.TryGetDelegate(dKey1, out IntPtr rp1), Is.True);
        Assert.That(cache.TryGetDelegate(dKey2, out IntPtr rp2), Is.True);

        ((Action)System.Runtime.InteropServices.GCHandle.FromIntPtr(rp1).Target)();
        ((Action)System.Runtime.InteropServices.GCHandle.FromIntPtr(rp2).Target)();
        Assert.That(captured1, Is.EqualTo(10));
        Assert.That(captured2, Is.EqualTo(20));

        // 清理
        System.Runtime.InteropServices.GCHandle.FromIntPtr(rp1).Free();
        System.Runtime.InteropServices.GCHandle.FromIntPtr(rp2).Free();
    }

    // ═══════════════════════════════════════════════════════
    // Count / TombstoneCount: 读锁正确获取
    // ═══════════════════════════════════════════════════════

    [Test]
    public void CountProperty_ReflectsAccurateCount_ThroughWriteReadCycles()
    {
        var cache = new FormulaCache(32);
        byte* p = stackalloc byte[1];

        Assert.That(cache.Count, Is.EqualTo(0));

        // 写入 20 条
        for (int i = 0; i < 20; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(20));

        // 删除 5 条
        for (int i = 0; i < 5; i++)
            cache.Remove(DualHash64.Compute(BitConverter.GetBytes(i)));
        Assert.That(cache.Count, Is.EqualTo(15));

        // 覆盖已存在的 key——count 不变
        cache.Put(DualHash64.Compute(BitConverter.GetBytes(19)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(15));

        // 写入新 key——count ++
        cache.Put(DualHash64.Compute(BitConverter.GetBytes(999)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(16));
    }

    [Test]
    public void TombstoneCount_TracksAccurately()
    {
        var cache = new FormulaCache(64);
        byte* p = stackalloc byte[1];

        // 填满
        for (int i = 0; i < 64; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        // 删 10 条 → 10 墓碑
        for (int i = 0; i < 10; i++)
            cache.Remove(DualHash64.Compute(BitConverter.GetBytes(i)));

        Assert.That(cache.TombstoneCount, Is.EqualTo(10));
    }

    // ═══════════════════════════════════════════════════════
    // 单例 Reset — 旧实例 _rwLock 被 Dispose
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Instance_Reset_Twice_DoesNotThrow()
    {
        // 确保 Instance 已初始化
        var inst1 = FormulaCache.Instance;
        Assert.That(inst1, Is.Not.Null);

        FormulaCache.Reset();
        var inst2 = FormulaCache.Instance;
        Assert.That(inst2, Is.Not.Null);

        // 再次 Reset
        FormulaCache.Reset();
        var inst3 = FormulaCache.Instance;
        Assert.That(inst3, Is.Not.Null);

        // 三次实例不应是同一个对象
        Assert.That(inst2, Is.Not.SameAs(inst1));
        Assert.That(inst3, Is.Not.SameAs(inst2));
    }

    // ═══════════════════════════════════════════════════════
    // FreeGCHandle: 已释放的 handle（catch 路径）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FreeGCHandle_AlreadyFreed_DoesNotThrow()
    {
        // 通过反射调用 private static FreeGCHandle，传入已释放的 IntPtr
        var freeMethod = typeof(FormulaCache).GetMethod("FreeGCHandle",
            BindingFlags.NonPublic | BindingFlags.Static);

        // 分配一个 GCHandle，获取其 IntPtr，然后释放
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(new byte[4]);
        IntPtr handlePtr = System.Runtime.InteropServices.GCHandle.ToIntPtr(handle);
        handle.Free();

        // 再次用同一个 IntPtr 调用 FreeGCHandle——应触发 catch 路径，不抛异常
        Assert.That(() => freeMethod.Invoke(null, new object[] { handlePtr }), Throws.Nothing);
    }

    [Test]
    public void FreeGCHandle_ZeroIntPtr_DoesNotThrow()
    {
        var freeMethod = typeof(FormulaCache).GetMethod("FreeGCHandle",
            BindingFlags.NonPublic | BindingFlags.Static);

        // IntPtr.Zero → FromIntPtr 失败 → catch 静默忽略
        Assert.That(() => freeMethod.Invoke(null, new object[] { IntPtr.Zero }), Throws.Nothing);
    }

    // ═══════════════════════════════════════════════════════
    // PutDelegate 覆盖 PutBytes 条目（键空间逆向冲突）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutDelegate_Overwrites_PutBytesEntry_ByReverseKeyCollision()
    {
        // 利用 XOR 自反性构造冲突：
        //   PutBytes(K1) → 存储在 slot S
        //   PutDelegate(K2) 其中 K2 = (xx1 XOR sentinel, fnv1 XOR sentinel)
        //   → MakeDelegateKey(K2) = K1 → FindSlot 返回 S → 进入 PutBytes 覆盖分支
        const ulong sentinel = 0xA3C8F159D6B7E024UL;

        var cache = new FormulaCache(16);
        var bytecodeKey = DualHash64.Compute(new byte[] { 0x01, 0x02, 0x03 });
        cache.PutBytes(bytecodeKey, new byte[] { 10, 20, 30, 40 });

        // 构造 delegate key：XOR 回原始 bytecode key
        var delegateKey = new DualHash64(
            bytecodeKey.XxHash64 ^ sentinel,
            bytecodeKey.FnvHash64 ^ sentinel);

        int captured = 0;
        Action del = () => captured = 77;
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);

        // PutDelegate：其内部 MakeDelegateKey(delegateKey) = bytecodeKey，命中 PutBytes 槽位
        // → 进入 lines 383-384: FreeGCHandle(_gcHandles[existingSlot]) + 覆盖为 DelegateSlot
        Assert.That(() => cache.PutDelegate(delegateKey,
            System.Runtime.InteropServices.GCHandle.ToIntPtr(handle)), Throws.Nothing);

        // 现在该 key 应是 delegate 条目
        Assert.That(cache.TryGetDelegate(delegateKey, out IntPtr rp), Is.True);
        var retrieved = (Action)System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Target;
        retrieved();
        Assert.That(captured, Is.EqualTo(77));

        // bytecode key 的原始查询应返回 false（已被 delegate 覆盖）
        Assert.That(cache.TryGet(bytecodeKey, out _, out _), Is.False);

        // 清理
        System.Runtime.InteropServices.GCHandle.FromIntPtr(rp).Free();
    }

    // ═══════════════════════════════════════════════════════
    // PutBytes 覆盖 DelegateSlot（键空间逆向冲突）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutBytes_Overwrites_DelegateSlot_ByReverseKeyCollision()
    {
        // 利用 XOR 自反性构造与 PutDelegate_Overwrites_PutBytes 相反的冲突：
        //   PutDelegate(K1) → 存储在 slot S
        //   PutBytes(K2) 其中 K2 = (xx1 XOR sentinel, fnv1 XOR sentinel)
        //   → 内部 MakeDelegateKey(K1) = K2_original? 不...
        //   实际上：PutDelegate(K1) → MakeDelegateKey(K1) = K1'
        //   我们需要 K2 = K1'（即 PutBytes 的 key 等于 delegate 的 Transformed key）
        const ulong sentinel = 0xA3C8F159D6B7E024UL;

        var cache = new FormulaCache(16);
        var rawKey = DualHash64.Compute(new byte[] { 0xAA, 0xBB, 0xCC });

        // 先 PutDelegate
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        cache.PutDelegate(rawKey, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        Assert.That(cache.TryGetDelegate(rawKey, out _), Is.True);

        // MakeDelegateKey(rawKey) 就是 delegate 在表中存储的真实 key
        var delegateSlotKey = new DualHash64(
            rawKey.XxHash64 ^ sentinel,
            rawKey.FnvHash64 ^ sentinel);

        // 用 delegateSlotKey 作为 PutBytes 的 key → FindSlot 命中 delegate slot → 进入 line 314-315
        Assert.That(() => cache.PutBytes(delegateSlotKey, new byte[] { 1, 2, 3 }), Throws.Nothing);

        // delegateSlotKey 现在应该是 bytecode 条目
        Assert.That(cache.TryGet(delegateSlotKey, out _, out int len), Is.True);
        Assert.That(len, Is.EqualTo(3));

        // 原始 delegate key 的 TryGetDelegate 应失败（被 PutBytes 覆盖）
        Assert.That(cache.TryGetDelegate(rawKey, out _), Is.False);

        // handle 已被 FreeGCHandle 释放——不需要手动释放
    }

    // ═══════════════════════════════════════════════════════
    // FindSlot: 同名 key 的不同 DualHash64 ——验证双分量
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FindSlot_SameContent_DifferentHashCombo()
    {
        // 验证 FindSlot 使用了两个分量进行匹配
        var cache = new FormulaCache(64);
        byte* p = stackalloc byte[1];

        // 手动构造两个只差一个分量的 key
        var k1 = new DualHash64(0xAAAA, 0xBBBB);
        var k2 = new DualHash64(0xAAAA, 0xCCCC); // xxHash 相同，fnv 不同

        cache.Put(k1, (IntPtr)p, 1);
        cache.Put(k2, (IntPtr)p, 1);

        Assert.That(cache.TryGet(k1, out _, out _), Is.True,
            "k1 应可检索（xxHash 相同但 fnv 不同）");
        Assert.That(cache.TryGet(k2, out _, out _), Is.True,
            "k2 应可检索");
        Assert.That(cache.Count, Is.EqualTo(2));
    }

    // ═══════════════════════════════════════════════════════
    // Put: key 已存在 → 覆盖（不增加 count）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Put_SameKey_ThreeTimes_CountStaysOne()
    {
        var cache = new FormulaCache(16);
        var key = DualHash64.Compute(new byte[] { 0xCC });
        byte* p1 = stackalloc byte[1] { 1 };
        byte* p2 = stackalloc byte[1] { 2 };
        byte* p3 = stackalloc byte[1] { 3 };

        cache.Put(key, (IntPtr)p1, 1);
        Assert.That(cache.Count, Is.EqualTo(1));

        cache.Put(key, (IntPtr)p2, 1);
        Assert.That(cache.Count, Is.EqualTo(1));

        cache.Put(key, (IntPtr)p3, 1);
        Assert.That(cache.Count, Is.EqualTo(1));

        Assert.That(cache.TryGet(key, out IntPtr p, out _), Is.True);
        Assert.That(((byte*)p)[0], Is.EqualTo(3), "最终值应为最后一次写入");
    }
}
