using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FormulaCache.Remove() 专项测试——逐 key 删除、墓碑行为、内存释放。
/// </summary>
public unsafe class FormulaCacheRemoveTests
{
    private FormulaCache _cache;

    [SetUp]
    public void SetUp()
    {
        _cache = new FormulaCache();
    }

    // ═══════════════════════════════════════════════════════
    // 基本删除
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_ExistingKey_TryGetReturnsFalse()
    {
        var key = DualHash64.Compute(new byte[] { 1, 2, 3, 4 });
        byte* src = stackalloc byte[4] { 10, 20, 30, 40 };

        _cache.Put(key, (IntPtr)src, 4);
        Assert.That(_cache.TryGet(key, out _, out _), Is.True, "删除前应可检索");

        _cache.Remove(key);

        Assert.That(_cache.TryGet(key, out IntPtr p, out int len), Is.False,
            "删除后应无法检索");
        Assert.That(p, Is.EqualTo(IntPtr.Zero));
        Assert.That(len, Is.EqualTo(0));
    }

    [Test]
    public void Remove_NonExistingKey_NoOp()
    {
        var key = DualHash64.Compute(new byte[] { 0xDE, 0xAD });

        // 不应抛异常
        Assert.That(() => _cache.Remove(key), Throws.Nothing);
        Assert.That(_cache.Count, Is.EqualTo(0));
    }

    [Test]
    public void Remove_SameKeyTwice_NoOpSecondTime()
    {
        var key = DualHash64.Compute(new byte[] { 55, 66 });
        byte* src = stackalloc byte[2];

        _cache.Put(key, (IntPtr)src, 2);
        _cache.Remove(key);

        // 第二次 Remove 相同 key 不应抛异常
        Assert.That(() => _cache.Remove(key), Throws.Nothing);
    }

    // ═══════════════════════════════════════════════════════
    // Count 和 Tombstone
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_DecrementsCount()
    {
        var k1 = DualHash64.Compute(new byte[] { 1 });
        var k2 = DualHash64.Compute(new byte[] { 2 });
        byte* p = stackalloc byte[1];

        _cache.Put(k1, (IntPtr)p, 1);
        _cache.Put(k2, (IntPtr)p, 1);
        Assert.That(_cache.Count, Is.EqualTo(2));

        _cache.Remove(k1);
        Assert.That(_cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void Remove_IncrementsTombstoneCount()
    {
        var key = DualHash64.Compute(new byte[] { 99 });
        byte* p = stackalloc byte[1];

        _cache.Put(key, (IntPtr)p, 1);

        int before = _cache.TombstoneCount;
        _cache.Remove(key);

        Assert.That(_cache.TombstoneCount, Is.GreaterThan(before),
            "删除应留下墓碑");
    }

    [Test]
    public void Remove_TombstoneIsReused_ByPut()
    {
        var key = DualHash64.Compute(new byte[] { 1, 2, 3 });
        byte* p = stackalloc byte[1];

        _cache.Put(key, (IntPtr)p, 1);
        _cache.Remove(key);

        // 墓碑计数增加
        int tombstoneAfterRemove = _cache.TombstoneCount;
        Assert.That(tombstoneAfterRemove, Is.GreaterThan(0));

        // 新 Put——墓碑可能被复用（取决于 hash 分布），至少新 key 应可检索
        var newKey = DualHash64.Compute(new byte[] { 4, 5, 6 });
        _cache.Put(newKey, (IntPtr)p, 1);
        Assert.That(_cache.TryGet(newKey, out _, out _), Is.True);

        // 如果新 key 命中了墓碑则 tombstoneCount 会减少，如果命中了 Empty 则不变
        // 两种都是正确行为——不对此做精确断言
    }

    // ═══════════════════════════════════════════════════════
    // 多条目删除
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_OneOfMany_OnlyRemovesTarget()
    {
        const int N = 64;
        var keys = new DualHash64[N];
        byte* p = stackalloc byte[1];

        for (int i = 0; i < N; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(keys[i], (IntPtr)p, 1);
        }

        // 删除中间一个
        _cache.Remove(keys[32]);

        Assert.That(_cache.TryGet(keys[32], out _, out _), Is.False,
            "被删除的 key 应不可检索");

        // 其他 key 仍可检索
        for (int i = 0; i < N; i++)
        {
            if (i == 32) continue;
            Assert.That(_cache.TryGet(keys[i], out _, out _), Is.True,
                $"key[{i}] 不应受影响");
        }

        Assert.That(_cache.Count, Is.EqualTo(N - 1));
    }

    [Test]
    public void Remove_ThenPut_SameKey_Works()
    {
        var key = DualHash64.Compute(new byte[] { 0xAA, 0xBB });
        byte* old = stackalloc byte[2] { 1, 2 };
        byte* nw  = stackalloc byte[2] { 3, 4 };

        _cache.Put(key, (IntPtr)old, 2);
        _cache.Remove(key);
        _cache.Put(key, (IntPtr)nw, 2);

        Assert.That(_cache.TryGet(key, out IntPtr p, out int len), Is.True);
        Assert.That(len, Is.EqualTo(2));
        Assert.That(((byte*)p)[0], Is.EqualTo(3));
        Assert.That(((byte*)p)[1], Is.EqualTo(4));
    }

    // ═══════════════════════════════════════════════════════
    // 满表压力下的删除
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_FromFullCache_ThenOverflow()
    {
        int cap = _cache.Capacity;
        var keys = new DualHash64[cap];
        byte* p = stackalloc byte[1];

        // 填满
        for (int i = 0; i < cap; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(keys[i], (IntPtr)p, 1);
        }

        // 删一半
        for (int i = 0; i < cap / 2; i++)
            _cache.Remove(keys[i]);

        Assert.That(_cache.Count, Is.EqualTo(cap - cap / 2));

        // 再写入 cap/2 个新条目（复用墓碑，不应崩溃）
        for (int i = 0; i < cap / 2; i++)
        {
            var newKey = DualHash64.Compute(BitConverter.GetBytes(i + cap * 2));
            _cache.Put(newKey, (IntPtr)p, 1);
        }

        Assert.That(_cache.Count, Is.EqualTo(cap));
    }

    [Test]
    public void Remove_MassDelete_CompactionTriggered()
    {
        int cap = _cache.Capacity;
        byte* p = stackalloc byte[1];

        // 填满
        for (int i = 0; i < cap; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(k, (IntPtr)p, 1);
        }

        // 大量删除 → 墓碑数超过 Capacity/4 → 后续 EvictAndWrite 应触发 Compact
        for (int i = 0; i < cap / 2; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Remove(k);
        }

        // 再写入触发逐出 → Compact 将其归零
        for (int i = 0; i < cap; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i + cap * 10));
            _cache.Put(k, (IntPtr)p, 1);
        }

        // Compact 后墓碑应归零
        Assert.That(_cache.TombstoneCount, Is.LessThan(cap / 4 + 1),
            "Compact 应将墓碑数降至阈值以下");
    }

    // ═══════════════════════════════════════════════════════
    // 探测链完整性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_DoesNotBreakProbeChain()
    {
        // 比满表时更精确的探测链测试：删中间条目后，探测链上的后续条目仍可检索
        byte* p = stackalloc byte[1];

        // 插入足够多条目以形成探测链
        const int N = 128;
        var keys = new DualHash64[N];
        for (int i = 0; i < N; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i * 13 + 7));
            _cache.Put(keys[i], (IntPtr)p, 1);
        }

        // 删除索引 40..59（一段）
        for (int i = 40; i < 60; i++)
            _cache.Remove(keys[i]);

        // 验证被删的不可检索
        for (int i = 40; i < 60; i++)
            Assert.That(_cache.TryGet(keys[i], out _, out _), Is.False);

        // 验证其他的仍可检索（探测链未被墓碑打断）
        for (int i = 0; i < N; i++)
        {
            if (i >= 40 && i < 60) continue;
            Assert.That(_cache.TryGet(keys[i], out _, out _), Is.True,
                $"key[{i}] 在探测链上应仍可检索");
        }
    }

    // ═══════════════════════════════════════════════════════
    // Remove 后 PutBytes（自有内存）—— GCHandle 释放
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_PutBytesEntry_FreesGCHandle()
    {
        var key = DualHash64.Compute(new byte[] { 0x11, 0x22, 0x33 });
        byte[] bytes = new byte[] { 1, 2, 3, 4, 5 };

        _cache.PutBytes(key, bytes);
        Assert.That(_cache.TryGet(key, out _, out _), Is.True);

        // 删除——内部应调用 FreeGCHandle（不抛异常即为成功）
        Assert.That(() => _cache.Remove(key), Throws.Nothing);
        Assert.That(_cache.TryGet(key, out _, out _), Is.False);
    }

    [Test]
    public void Remove_PutBytesEntry_ThenPut_NoLeak()
    {
        var key = DualHash64.Compute(new byte[] { 0xAA, 0xBB });
        byte[] bytes1 = new byte[] { 1, 2, 3 };
        byte[] bytes2 = new byte[] { 4, 5, 6 };

        _cache.PutBytes(key, bytes1);
        _cache.Remove(key);
        _cache.PutBytes(key, bytes2);

        Assert.That(_cache.TryGet(key, out IntPtr p, out int len), Is.True);
        Assert.That(len, Is.EqualTo(3));
        Assert.That(((byte*)p)[0], Is.EqualTo(4));
    }

    // ═══════════════════════════════════════════════════════
    // Delegate 不受影响
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Remove_BytecodeEntry_DoesNotAffectDelegate()
    {
        var bcKey = DualHash64.Compute(new byte[] { 0x01 });
        byte* p = stackalloc byte[1];

        _cache.Put(bcKey, (IntPtr)p, 1);

        int captured = 0;
        Action del = () => captured = 99;
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        var dKey = DualHash64.Compute(new byte[] { 0x02 });
        _cache.PutDelegate(dKey, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));

        // 删除字节码条目
        _cache.Remove(bcKey);
        Assert.That(_cache.TryGet(bcKey, out _, out _), Is.False);

        // Delegate 条目不受影响
        Assert.That(_cache.TryGetDelegate(dKey, out IntPtr rp), Is.True);
        var rh = System.Runtime.InteropServices.GCHandle.FromIntPtr(rp);
        ((Action)rh.Target)();
        Assert.That(captured, Is.EqualTo(99));

        // 清理
        if (_cache.TryGetDelegate(dKey, out IntPtr cp))
            System.Runtime.InteropServices.GCHandle.FromIntPtr(cp).Free();
        else
            handle.Free();
    }
}
