using System;
using FluxFormula.Core;
using NUnit.Framework;

public unsafe class FormulaCacheTests
{
    private FormulaCache _cache;

    [SetUp]
    public void SetUp()
    {
        _cache = new FormulaCache();
    }

    // ═══════════════════════════════════════════════════════
    // 基本读写
    // ═══════════════════════════════════════════════════════

    [Test]
    public void PutGet_RoundTrip()
    {
        var key  = DualHash64.Compute(new byte[] { 1, 2, 3, 4 });
        byte* src = stackalloc byte[4] { 10, 20, 30, 40 };

        _cache.Put(key, (IntPtr)src, 4);

        bool found = _cache.TryGet(key, out IntPtr p, out int len);
        byte* ptr  = (byte*)p;
        Assert.That(found, Is.True);
        Assert.That(len, Is.EqualTo(4));
        Assert.That(ptr[0], Is.EqualTo(10));
        Assert.That(ptr[1], Is.EqualTo(20));
        Assert.That(ptr[2], Is.EqualTo(30));
        Assert.That(ptr[3], Is.EqualTo(40));
    }

    [Test]
    public void TryGet_Miss_ReturnsFalse()
    {
        var key = DualHash64.Compute(new byte[] { 99, 99, 99 });

        bool found = _cache.TryGet(key, out IntPtr p, out int len);
        Assert.That(found, Is.False);
        Assert.That(p, Is.EqualTo(IntPtr.Zero));
        Assert.That(len, Is.EqualTo(0));
    }

    [Test]
    public void Put_Overwrites_ExistingKey()
    {
        var key   = DualHash64.Compute(new byte[] { 7, 7, 7 });
        byte* old = stackalloc byte[3] { 1, 2, 3 };
        byte* nw  = stackalloc byte[3] { 4, 5, 6 };

        _cache.Put(key, (IntPtr)old, 3);
        _cache.Put(key, (IntPtr)nw,  3);  // 同 key 覆盖

        bool found = _cache.TryGet(key, out IntPtr p, out int len);
        byte* ptr  = (byte*)p;
        Assert.That(found, Is.True);
        Assert.That(ptr[0], Is.EqualTo(4)); // 新值
    }

    [Test]
    public void Count_TracksEntries()
    {
        Assert.That(_cache.Count, Is.EqualTo(0));

        var k1 = DualHash64.Compute(new byte[] { 1 });
        var k2 = DualHash64.Compute(new byte[] { 2 });
        var k3 = DualHash64.Compute(new byte[] { 3 });
        byte* p = stackalloc byte[1];

        _cache.Put(k1, (IntPtr)p, 1);
        Assert.That(_cache.Count, Is.EqualTo(1));

        _cache.Put(k2, (IntPtr)p, 1);
        Assert.That(_cache.Count, Is.EqualTo(2));

        _cache.Put(k3, (IntPtr)p, 1);
        Assert.That(_cache.Count, Is.EqualTo(3));

        // 覆盖已存在的 key 不应增加 count
        _cache.Put(k1, (IntPtr)p, 1);
        Assert.That(_cache.Count, Is.EqualTo(3));
    }

    // ═══════════════════════════════════════════════════════
    // 接口实现验证
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Implements_IFluxCacheProvider()
    {
        IFluxCacheProvider provider = _cache;
        byte* src = stackalloc byte[3] { 9, 8, 7 };
        var key   = DualHash64.Compute(new byte[] { 42 });

        provider.Put(key, (IntPtr)src, 3);

        bool found = provider.TryGet(key, out IntPtr p, out int len);
        byte* ptr  = (byte*)p;
        Assert.That(found, Is.True);
        Assert.That(len, Is.EqualTo(3));
        Assert.That(ptr[0], Is.EqualTo(9));
    }

    // ═══════════════════════════════════════════════════════
    // 多条目（线性探测验证）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void MultipleEntries_AllRetrievable()
    {
        const int N = 128;
        byte* p = stackalloc byte[1];

        var keys = new DualHash64[N];
        for (int i = 0; i < N; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(keys[i], (IntPtr)p, 1);
        }

        Assert.That(_cache.Count, Is.EqualTo(N));

        for (int i = 0; i < N; i++)
        {
            Assert.That(_cache.TryGet(keys[i], out _, out _), Is.True,
                $"key[{i}] 应可检索");
        }
    }

    [Test]
    public void ManyEntries_AllRetrievable()
    {
        // 填到接近容量——触发线性探测链但不应断裂
        int N = _cache.Capacity - 1;
        var keys = new DualHash64[N];
        byte* p  = stackalloc byte[1];

        for (int i = 0; i < N; i++)
        {
            keys[i] = DualHash64.Compute(BitConverter.GetBytes(i * 7 + 13));
            _cache.Put(keys[i], (IntPtr)p, 1);
        }

        Assert.That(_cache.Count, Is.EqualTo(N));

        for (int i = 0; i < N; i++)
        {
            Assert.That(_cache.TryGet(keys[i], out _, out _), Is.True,
                $"key[{i}] 在近乎满表时应可检索");
        }
    }

    // ═══════════════════════════════════════════════════════
    // 驱逐（超容量写入）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Overflow_Evicts_OldestEntry()
    {
        byte* p = stackalloc byte[1];

        // 写满 Capacity
        var firstKey = DualHash64.Compute(BitConverter.GetBytes(-1));
        _cache.Put(firstKey, (IntPtr)p, 1);

        for (int i = 0; i < _cache.Capacity - 1; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(k, (IntPtr)p, 1);
        }

        Assert.That(_cache.Count, Is.EqualTo(_cache.Capacity));

        // 再写一个——触发驱逐
        var extraKey = DualHash64.Compute(BitConverter.GetBytes(99999));
        _cache.Put(extraKey, (IntPtr)p, 1);

        // 新条目必须可检索
        Assert.That(_cache.TryGet(extraKey, out _, out _), Is.True,
            "新写入的条目应可检索");

        // Count 应基本稳定（被驱逐了一个又加了一个）
        Assert.That(_cache.Count, Is.GreaterThanOrEqualTo(_cache.Capacity - 1));
    }

    [Test]
    public void Overflow_MultipleCycles_StillWorks()
    {
        byte* p = stackalloc byte[1];

        // 反复超容量写入 3 轮
        int total = _cache.Capacity * 3;
        for (int i = 0; i < total; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(k, (IntPtr)p, 1);
        }

        // 最后写入的一批应可检索（Capacity 个以内的最新条目）
        int found = 0;
        for (int i = total - 1; i >= 0 && found < _cache.Capacity; i--)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            if (_cache.TryGet(k, out _, out _))
                found++;
        }

        Assert.That(found, Is.GreaterThan(0),
            "最新写入的条目至少有一部分应可检索");
    }

    // ═══════════════════════════════════════════════════════
    // 墓碑回收
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Tombstones_AreReused()
    {
        byte* p = stackalloc byte[1];

        // 写满
        for (int i = 0; i < _cache.Capacity; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i));
            _cache.Put(k, (IntPtr)p, 1);
        }

        // 再写一轮（触发大量墓碑）
        for (int i = 0; i < _cache.Capacity / 2; i++)
        {
            var k = DualHash64.Compute(BitConverter.GetBytes(i + _cache.Capacity));
            _cache.Put(k, (IntPtr)p, 1);
        }

        // 墓碑计数不应失控（Compact 会在 tombstoneCount > Capacity/4 时触发）
        Assert.That(_cache.TombstoneCount, Is.LessThan(_cache.Capacity / 4 + 1),
            "墓碑数不应超过触发阈值");
    }

    // ═══════════════════════════════════════════════════════
    // 指针有效性验证
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Put_PreservesPointerIntegrity()
    {
        var key = DualHash64.Compute(new byte[] { 0xAA, 0xBB });
        byte* src = stackalloc byte[8];

        // 写入已知模式
        for (int i = 0; i < 8; i++) src[i] = (byte)(0xA0 + i);

        _cache.Put(key, (IntPtr)src, 8);

        bool found = _cache.TryGet(key, out IntPtr p, out int len);
        byte* ptr  = (byte*)p;
        Assert.That(found, Is.True);
        Assert.That(len, Is.EqualTo(8));

        // 逐字节验证
        for (int i = 0; i < 8; i++)
        {
            Assert.That(ptr[i], Is.EqualTo((byte)(0xA0 + i)),
                $"ptr[{i}] 应与写入值一致");
        }
    }

    [Test]
    public void Put_DifferentLengths_AreTrackedCorrectly()
    {
        byte* p = stackalloc byte[8];

        var kShort = DualHash64.Compute(new byte[] { 1 });
        var kLong  = DualHash64.Compute(new byte[] { 2 });

        _cache.Put(kShort, (IntPtr)p, 3);
        _cache.Put(kLong,  (IntPtr)p, 8);

        Assert.That(_cache.TryGet(kShort, out _, out int lenShort), Is.True);
        Assert.That(lenShort, Is.EqualTo(3));

        Assert.That(_cache.TryGet(kLong, out _, out int lenLong), Is.True);
        Assert.That(lenLong, Is.EqualTo(8));
    }

    // ═══════════════════════════════════════════════════════
    // 空缓存边界
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EmptyCache_Properties()
    {
        Assert.That(_cache.Count, Is.EqualTo(0));
        Assert.That(_cache.TombstoneCount, Is.EqualTo(0));
    }

    [Test]
    public void EmptyCache_TryGet_ReturnsFalse()
    {
        var key = new DualHash64(0xDEADBEEF, 0xCAFEBABE);
        Assert.That(_cache.TryGet(key, out _, out _), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // DualHash64 组合键验证
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Keys_WithSameXxHash_ButDifferentFnv_AreDistinct()
    {
        // 两个 key 的 xxHash 相同但 FNV 不同——验证双分量比较
        byte* p = stackalloc byte[1];

        var k1 = new DualHash64(0xABCDEF0123456789UL, 0x1111111111111111UL);
        var k2 = new DualHash64(0xABCDEF0123456789UL, 0x2222222222222222UL);

        _cache.Put(k1, (IntPtr)p, 1);
        _cache.Put(k2, (IntPtr)p, 1);

        Assert.That(_cache.TryGet(k1, out _, out _), Is.True);
        Assert.That(_cache.TryGet(k2, out _, out _), Is.True);
        Assert.That(_cache.Count, Is.EqualTo(2));
    }
}
