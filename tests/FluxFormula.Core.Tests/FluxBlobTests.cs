using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FluxBlob 完整测试——Load/Unload/Shutdown/Initialize/VerifyIntegrity/FluxBlobHandle。
/// </summary>
public unsafe class FluxBlobTests
{
    [SetUp]
    public void SetUp()
    {
        // 每个测试前确保干净状态
        if (FluxBlob.IsInitialized)
            FluxBlob.Shutdown();
        FormulaCache.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        if (FluxBlob.IsInitialized)
            FluxBlob.Shutdown();
    }

    // ═══════════════════════════════════════════════════════
    // Load 基本
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Load_ValidData_EntriesRegisteredInCache()
    {
        byte[] blobData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(blobData));
        var entries = new BlobEntry[] { new(hash, 0, blobData.Length) };

        var handle = FluxBlob.Load(blobData, entries);

        Assert.That(handle, Is.Not.Null);
        Assert.That(handle.IsLoaded, Is.True);
        Assert.That(handle.EntryCount, Is.EqualTo(1));
        Assert.That(FluxBlob.IsInitialized, Is.True);
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(1));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(1));

        // 验证缓存中可检索
        var cache = FormulaCache.Instance;
        Assert.That(cache.TryGet(hash, out IntPtr ptr, out int len), Is.True);
        Assert.That(len, Is.EqualTo(blobData.Length));
        Assert.That(((byte*)ptr)[0], Is.EqualTo(0x01));
        Assert.That(((byte*)ptr)[7], Is.EqualTo(0x08));
    }

    [Test]
    public void Load_NullData_ThrowsArgumentNullException()
    {
        var entries = new BlobEntry[] { new(new DualHash64(1, 2), 0, 10) };

        var ex = Assert.Throws<ArgumentNullException>(() => FluxBlob.Load(null, entries));
        Assert.That(ex.ParamName, Is.EqualTo("blobData"));
    }

    [Test]
    public void Load_EmptyData_ReturnsEmptyHandle()
    {
        var handle = FluxBlob.Load(Array.Empty<byte>(), Array.Empty<BlobEntry>());

        Assert.That(handle, Is.Not.Null);
        Assert.That(handle.IsLoaded, Is.False, "空 handle 应为未加载状态");
        Assert.That(handle.EntryCount, Is.EqualTo(0));
        Assert.That(FluxBlob.IsInitialized, Is.False);
    }

    [Test]
    public void Load_EmptyEntries_ReturnsEmptyHandle()
    {
        byte[] data = new byte[] { 1, 2, 3 };

        var handle = FluxBlob.Load(data, Array.Empty<BlobEntry>());

        Assert.That(handle.IsLoaded, Is.False);
        Assert.That(FluxBlob.IsInitialized, Is.False);
    }

    [Test]
    public void Load_OutOfBoundsEntry_Throws()
    {
        byte[] blobData = new byte[10];
        var entries = new BlobEntry[] { new(new DualHash64(1, 2), offset: 5, length: 100) };

        var ex = Assert.Throws<ArgumentException>(() => FluxBlob.Load(blobData, entries));
        Assert.That(ex.Message, Does.Contain("out of bounds"));
    }

    [Test]
    public void Load_NegativeOffset_Throws()
    {
        byte[] blobData = new byte[10];
        var entries = new BlobEntry[] { new(new DualHash64(1, 2), offset: -1, length: 5) };

        var ex = Assert.Throws<ArgumentException>(() => FluxBlob.Load(blobData, entries));
        Assert.That(ex.Message, Does.Contain("out of bounds"));
    }

    [Test]
    public void Load_ZeroLength_Throws()
    {
        byte[] blobData = new byte[10];
        var entries = new BlobEntry[] { new(new DualHash64(1, 2), offset: 0, length: 0) };

        var ex = Assert.Throws<ArgumentException>(() => FluxBlob.Load(blobData, entries));
        Assert.That(ex.Message, Does.Contain("out of bounds"));
    }

    // ═══════════════════════════════════════════════════════
    // Load 多个 blob（并存）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Load_MultipleBlobs_AllRegistered()
    {
        byte[] data1 = new byte[] { 0xAA, 0xBB };
        var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(data1));
        var entries1 = new BlobEntry[] { new(hash1, 0, data1.Length) };

        byte[] data2 = new byte[] { 0xCC, 0xDD, 0xEE };
        var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(data2));
        var entries2 = new BlobEntry[] { new(hash2, 0, data2.Length) };

        var h1 = FluxBlob.Load(data1, entries1);
        var h2 = FluxBlob.Load(data2, entries2);

        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(2));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(2));

        var cache = FormulaCache.Instance;
        Assert.That(cache.TryGet(hash1, out _, out _), Is.True);
        Assert.That(cache.TryGet(hash2, out _, out _), Is.True);

        Assert.That(h1.IsLoaded, Is.True);
        Assert.That(h2.IsLoaded, Is.True);
    }

    [Test]
    public void Load_MultipleEntries_SingleBlob()
    {
        // 模拟 blob data 中有两条背靠背的公式字节码
        byte[] data1 = new byte[] { 0x01, 0x02, 0x03 };
        byte[] data2 = new byte[] { 0x04, 0x05 };
        byte[] blobData = new byte[data1.Length + data2.Length];
        Buffer.BlockCopy(data1, 0, blobData, 0, data1.Length);
        Buffer.BlockCopy(data2, 0, blobData, data1.Length, data2.Length);

        var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(data1));
        var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(data2));
        var entries = new BlobEntry[]
        {
            new(hash1, 0, data1.Length),
            new(hash2, data1.Length, data2.Length),
        };

        var handle = FluxBlob.Load(blobData, entries);

        Assert.That(handle.EntryCount, Is.EqualTo(2));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(2));

        var cache = FormulaCache.Instance;
        Assert.That(cache.TryGet(hash1, out IntPtr p1, out int l1), Is.True);
        Assert.That(l1, Is.EqualTo(3));
        Assert.That(((byte*)p1)[0], Is.EqualTo(0x01));

        Assert.That(cache.TryGet(hash2, out IntPtr p2, out int l2), Is.True);
        Assert.That(l2, Is.EqualTo(2));
        Assert.That(((byte*)p2)[0], Is.EqualTo(0x04));
    }

    // ═══════════════════════════════════════════════════════
    // Unload
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Unload_RemovesEntriesFromCache()
    {
        byte[] data = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(hash, 0, data.Length) };

        var handle = FluxBlob.Load(data, entries);
        Assert.That(FormulaCache.Instance.TryGet(hash, out _, out _), Is.True);

        FluxBlob.Unload(handle);

        Assert.That(handle.IsLoaded, Is.False);
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(0));
        Assert.That(FormulaCache.Instance.TryGet(hash, out _, out _), Is.False);
        Assert.That(FluxBlob.IsInitialized, Is.False);
    }

    [Test]
    public void Unload_Null_NoOp()
    {
        Assert.That(() => FluxBlob.Unload(null), Throws.Nothing);
    }

    [Test]
    public void Unload_AlreadyUnloaded_NoOp()
    {
        byte[] data = new byte[] { 0xFF };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(hash, 0, data.Length) };

        var handle = FluxBlob.Load(data, entries);
        FluxBlob.Unload(handle);

        // 再次 Unload 不抛异常
        Assert.That(() => FluxBlob.Unload(handle), Throws.Nothing);
    }

    [Test]
    public void Unload_OnlyTargetBlob_OthersUnaffected()
    {
        byte[] data1 = new byte[] { 0xAA };
        var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(data1));
        var e1 = new BlobEntry[] { new(hash1, 0, data1.Length) };

        byte[] data2 = new byte[] { 0xBB };
        var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(data2));
        var e2 = new BlobEntry[] { new(hash2, 0, data2.Length) };

        var h1 = FluxBlob.Load(data1, e1);
        var h2 = FluxBlob.Load(data2, e2);

        FluxBlob.Unload(h1);

        // h2 不应受影响
        Assert.That(h2.IsLoaded, Is.True);
        Assert.That(FormulaCache.Instance.TryGet(hash2, out _, out _), Is.True);
        Assert.That(FormulaCache.Instance.TryGet(hash1, out _, out _), Is.False);
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // Shutdown
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Shutdown_ClearsAll()
    {
        byte[] d1 = new byte[] { 0x01 };
        byte[] d2 = new byte[] { 0x02 };
        byte[] d3 = new byte[] { 0x03 };
        var h1 = DualHash64.Compute(new ReadOnlySpan<byte>(d1));
        var h2 = DualHash64.Compute(new ReadOnlySpan<byte>(d2));
        var h3 = DualHash64.Compute(new ReadOnlySpan<byte>(d3));

        FluxBlob.Load(d1, new[] { new BlobEntry(h1, 0, d1.Length) });
        FluxBlob.Load(d2, new[] { new BlobEntry(h2, 0, d2.Length) });
        FluxBlob.Load(d3, new[] { new BlobEntry(h3, 0, d3.Length) });

        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(3));

        FluxBlob.Shutdown();

        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(0));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(0));
        Assert.That(FluxBlob.TotalBlobSize, Is.EqualTo(0));
        Assert.That(FluxBlob.IsInitialized, Is.False);
    }

    [Test]
    public void Shutdown_WhenEmpty_NoOp()
    {
        Assert.That(FluxBlob.IsInitialized, Is.False);
        Assert.That(() => FluxBlob.Shutdown(), Throws.Nothing);
    }

    // ═══════════════════════════════════════════════════════
    // Initialize（向后兼容）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Initialize_LoadsBlob()
    {
        byte[] data = new byte[] { 0x77, 0x88 };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(hash, 0, data.Length) };

        FluxBlob.Initialize(data, entries);

        Assert.That(FluxBlob.IsInitialized, Is.True);
        Assert.That(FormulaCache.Instance.TryGet(hash, out _, out _), Is.True);
    }

    [Test]
    public void Initialize_ReplacesExisting()
    {
        byte[] oldData = new byte[] { 0x01 };
        var oldHash = DualHash64.Compute(new ReadOnlySpan<byte>(oldData));
        FluxBlob.Initialize(oldData, new[] { new BlobEntry(oldHash, 0, oldData.Length) });

        byte[] newData = new byte[] { 0x02 };
        var newHash = DualHash64.Compute(new ReadOnlySpan<byte>(newData));
        FluxBlob.Initialize(newData, new[] { new BlobEntry(newHash, 0, newData.Length) });

        // 旧条目应已清除，新条目存在
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(1));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(1));
    }

    // ═══════════════════════════════════════════════════════
    // VerifyIntegrity
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VerifyIntegrity_Match_ReturnsTrue()
    {
        byte[] data = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(hash, 0, data.Length) };

        FluxBlob.Load(data, entries);

        Assert.That(FluxBlob.VerifyIntegrity(hash), Is.True);
    }

    [Test]
    public void VerifyIntegrity_NotInitialized_ReturnsFalse()
    {
        var hash = DualHash64.Compute(new byte[] { 1, 2, 3 });
        Assert.That(FluxBlob.VerifyIntegrity(hash), Is.False);
    }

    [Test]
    public void VerifyIntegrity_KeyNotInCache_ReturnsFalse()
    {
        byte[] data = new byte[] { 0xAA };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        FluxBlob.Load(data, new[] { new BlobEntry(hash, 0, data.Length) });

        var otherHash = DualHash64.Compute(new byte[] { 0xBB, 0xCC });
        Assert.That(FluxBlob.VerifyIntegrity(otherHash), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // FluxBlobHandle
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Handle_Dispose_UnloadsBlob()
    {
        byte[] data = new byte[] { 0x50, 0x60 };
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(hash, 0, data.Length) };

        var handle = FluxBlob.Load(data, entries);
        Assert.That(handle.IsLoaded, Is.True);

        handle.Dispose();

        Assert.That(handle.IsLoaded, Is.False);
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(0));
    }

    [Test]
    public void Handle_EntryKeys_MatchesInput()
    {
        byte[] data1 = new byte[] { 0x01, 0x02 };
        byte[] data2 = new byte[] { 0x03, 0x04, 0x05 };
        byte[] blobData = new byte[data1.Length + data2.Length];
        Buffer.BlockCopy(data1, 0, blobData, 0, data1.Length);
        Buffer.BlockCopy(data2, 0, blobData, data1.Length, data2.Length);

        var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(data1));
        var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(data2));
        var entries = new BlobEntry[]
        {
            new(hash1, 0, data1.Length),
            new(hash2, data1.Length, data2.Length),
        };

        var handle = FluxBlob.Load(blobData, entries);

        Assert.That(handle.EntryKeys.Length, Is.EqualTo(2));
        Assert.That(handle.EntryKeys[0], Is.EqualTo(hash1));
        Assert.That(handle.EntryKeys[1], Is.EqualTo(hash2));
    }

    [Test]
    public void Handle_Empty_Properties()
    {
        var handle = FluxBlob.Load(Array.Empty<byte>(), Array.Empty<BlobEntry>());

        Assert.That(handle.EntryCount, Is.EqualTo(0));
        Assert.That(handle.IsLoaded, Is.False);
        Assert.That(handle.EntryKeys.Length, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // 压缩条目
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Load_CompressedEntry_DecompressedAndRegistered()
    {
        byte[] original = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        byte[] compressed = FluxCompression.Compress(original);
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(original));

        var entries = new BlobEntry[] { new(hash, 0, compressed.Length) };

        var handle = FluxBlob.Load(compressed, entries);

        Assert.That(handle.EntryCount, Is.EqualTo(1));

        // 缓存中的条目应为解压后的原始数据
        var cache = FormulaCache.Instance;
        Assert.That(cache.TryGet(hash, out IntPtr ptr, out int len), Is.True);
        Assert.That(len, Is.EqualTo(original.Length));

        byte* p = (byte*)ptr;
        for (int i = 0; i < original.Length; i++)
            Assert.That(p[i], Is.EqualTo(original[i]),
                $"解压后 byte[{i}] 应与原始数据匹配");
    }

    [Test]
    public void Load_MixedCompressedAndUncompressed()
    {
        // 条目 0 = 压缩，条目 1 = 未压缩
        byte[] raw1 = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        byte[] raw2 = new byte[] { 0xAA, 0xBB };
        byte[] comp1 = FluxCompression.Compress(raw1);

        byte[] blobData = new byte[comp1.Length + raw2.Length];
        Buffer.BlockCopy(comp1, 0, blobData, 0, comp1.Length);
        Buffer.BlockCopy(raw2, 0, blobData, comp1.Length, raw2.Length);

        var hash1 = DualHash64.Compute(new ReadOnlySpan<byte>(raw1));
        var hash2 = DualHash64.Compute(new ReadOnlySpan<byte>(raw2));
        var entries = new BlobEntry[]
        {
            new(hash1, 0, comp1.Length),
            new(hash2, comp1.Length, raw2.Length),
        };

        var handle = FluxBlob.Load(blobData, entries);

        Assert.That(handle.EntryCount, Is.EqualTo(2));

        var cache = FormulaCache.Instance;
        Assert.That(cache.TryGet(hash1, out _, out int len1), Is.True);
        Assert.That(len1, Is.EqualTo(raw1.Length)); // 解压后长度

        Assert.That(cache.TryGet(hash2, out _, out int len2), Is.True);
        Assert.That(len2, Is.EqualTo(raw2.Length));
    }

    // ═══════════════════════════════════════════════════════
    // 状态属性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TotalBlobSize_TracksAccurately()
    {
        long before = FluxBlob.TotalBlobSize;

        byte[] data = new byte[100];
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        FluxBlob.Load(data, new[] { new BlobEntry(hash, 0, data.Length) });

        Assert.That(FluxBlob.TotalBlobSize, Is.EqualTo(before + 100));
    }

    [Test]
    public void TotalBlobSize_DecreasesAfterUnload()
    {
        byte[] data = new byte[50];
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var handle = FluxBlob.Load(data, new[] { new BlobEntry(hash, 0, data.Length) });

        Assert.That(FluxBlob.TotalBlobSize, Is.GreaterThan(0));

        FluxBlob.Unload(handle);
        Assert.That(FluxBlob.TotalBlobSize, Is.EqualTo(0));
    }

    [Test]
    public void EntryCount_BackwardCompatProperty()
    {
        byte[] data = new byte[10];
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        FluxBlob.Load(data, new[] { new BlobEntry(hash, 0, data.Length) });

        Assert.That(FluxBlob.EntryCount, Is.EqualTo(FluxBlob.TotalEntryCount));
    }

    // ═══════════════════════════════════════════════════════
    // 压缩条目 × Unload / Shutdown（DecompressedHandles 清理）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Unload_CompressedEntry_FreesDecompressedHandles()
    {
        byte[] original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        byte[] compressed = FluxCompression.Compress(original);
        var hash = DualHash64.Compute(new ReadOnlySpan<byte>(original));
        var entries = new BlobEntry[] { new(hash, 0, compressed.Length) };

        var handle = FluxBlob.Load(compressed, entries);
        Assert.That(handle.EntryCount, Is.EqualTo(1));

        // Unload 应释放解压临时 pinned 数组
        Assert.That(() => FluxBlob.Unload(handle), Throws.Nothing);
        Assert.That(handle.IsLoaded, Is.False);
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(0));
    }

    [Test]
    public void Shutdown_WithCompressedEntries_FreesAllHandles()
    {
        byte[] orig1 = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        byte[] orig2 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 };
        byte[] comp1 = FluxCompression.Compress(orig1);
        byte[] comp2 = FluxCompression.Compress(orig2);

        var h1 = DualHash64.Compute(new ReadOnlySpan<byte>(orig1));
        var h2 = DualHash64.Compute(new ReadOnlySpan<byte>(orig2));

        FluxBlob.Load(comp1, new[] { new BlobEntry(h1, 0, comp1.Length) });
        FluxBlob.Load(comp2, new[] { new BlobEntry(h2, 0, comp2.Length) });

        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(2));

        // Shutdown 应释放所有解压后的 pinned 数组 —— 不崩溃即为通过
        Assert.That(() => FluxBlob.Shutdown(), Throws.Nothing);
        Assert.That(FluxBlob.IsInitialized, Is.False);
    }

    [Test]
    public void Unload_OnlyTargetCompressedBlob_OtherUnaffected()
    {
        byte[] orig1 = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        byte[] orig2 = new byte[] { 0xAA, 0xBB };
        byte[] comp1 = FluxCompression.Compress(orig1);

        var h1 = DualHash64.Compute(new ReadOnlySpan<byte>(orig1));
        var h2 = DualHash64.Compute(new ReadOnlySpan<byte>(orig2));

        var handle1 = FluxBlob.Load(comp1, new[] { new BlobEntry(h1, 0, comp1.Length) });
        var handle2 = FluxBlob.Load(orig2, new[] { new BlobEntry(h2, 0, orig2.Length) });

        // 卸载压缩的 blob
        FluxBlob.Unload(handle1);
        Assert.That(handle1.IsLoaded, Is.False);
        Assert.That(handle2.IsLoaded, Is.True);

        // 未压缩的 blob 不受影响
        Assert.That(FormulaCache.Instance.TryGet(h2, out _, out _), Is.True);
        Assert.That(FormulaCache.Instance.TryGet(h1, out _, out _), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // VerifyIntegrity: 数据篡改（hash 和实际内容不同）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VerifyIntegrity_DataTampered_ReturnsFalse()
    {
        byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var originalHash = DualHash64.Compute(new ReadOnlySpan<byte>(data));
        var entries = new BlobEntry[] { new(originalHash, 0, data.Length) };

        FluxBlob.Load(data, entries);

        // 篡改原始 byte[]（指针仍指向同一内存）
        data[0] = 0xFF;
        data[1] = 0xFF;

        // originalHash 对应的数据已被篡改，VerifyIntegrity 应检测到不匹配
        Assert.That(FluxBlob.VerifyIntegrity(originalHash), Is.False,
            "数据被篡改后 integrity 验证应失败");
    }

    // ═══════════════════════════════════════════════════════
    // Initialize: 覆盖已有 blob（先 Shutdown 再 Load）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Initialize_OverwritesWithCompressedData()
    {
        byte[] d1 = new byte[] { 0x01, 0x02, 0x03 };
        var h1 = DualHash64.Compute(new ReadOnlySpan<byte>(d1));
        FluxBlob.Initialize(d1, new[] { new BlobEntry(h1, 0, d1.Length) });
        Assert.That(FluxBlob.IsInitialized, Is.True);

        byte[] d2 = new byte[] { 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        byte[] comp2 = FluxCompression.Compress(d2);
        var h2 = DualHash64.Compute(new ReadOnlySpan<byte>(d2));
        FluxBlob.Initialize(comp2, new[] { new BlobEntry(h2, 0, comp2.Length) });

        // 新 blob 应是唯一的已加载 blob（旧 blob 已被 Shutdown 替换）
        Assert.That(FluxBlob.LoadedBlobCount, Is.EqualTo(1));
        Assert.That(FluxBlob.TotalEntryCount, Is.EqualTo(1));

        // 新条目（压缩 → 解压）应可检索
        Assert.That(FormulaCache.Instance.TryGet(h2, out IntPtr p, out int len), Is.True,
            "新条目（压缩）应可检索");
        Assert.That(len, Is.EqualTo(d2.Length), "解压后应为原始长度");
    }
}
