using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// BlobFormat 二进制格式解析/写入完整测试。
/// 覆盖 TryParseHeader、ExtractBlobData、ReadEntryTable、WriteHeader、WriteEntry 及往返测试。
/// </summary>
public class BlobFormatTests
{
    // ═══════════════════════════════════════════════════════
    // TryParseHeader
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TryParseHeader_Valid_ReturnsTrue()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 5, blobDataSize: 1024, compressed: false);

        bool ok = BlobFormat.TryParseHeader(header, out int entryCount,
            out int blobDataOffset, out int blobDataSize, out bool compressed);

        Assert.That(ok, Is.True);
        Assert.That(entryCount, Is.EqualTo(5));
        Assert.That(blobDataSize, Is.EqualTo(1024));
        Assert.That(blobDataOffset, Is.EqualTo(BlobFormat.HeaderSize + 5 * BlobFormat.EntrySize));
        Assert.That(compressed, Is.False);
    }

    [Test]
    public void TryParseHeader_Compressed_FlagSet()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 3, blobDataSize: 512, compressed: true);

        bool ok = BlobFormat.TryParseHeader(header, out int entryCount,
            out int blobDataOffset, out int blobDataSize, out bool compressed);

        Assert.That(ok, Is.True);
        Assert.That(entryCount, Is.EqualTo(3));
        Assert.That(compressed, Is.True);
    }

    [Test]
    public void TryParseHeader_TooShort_ReturnsFalse()
    {
        byte[] data = new byte[10]; // 少于 HeaderSize (20)

        bool ok = BlobFormat.TryParseHeader(data, out _, out _, out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryParseHeader_BadMagic_ReturnsFalse()
    {
        byte[] data = new byte[BlobFormat.HeaderSize];
        data[0] = (byte)'X';
        data[1] = (byte)'X';
        data[2] = (byte)'X';
        data[3] = (byte)'X';

        bool ok = BlobFormat.TryParseHeader(data, out _, out _, out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryParseHeader_EmptyEntries()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 0, blobDataSize: 0, compressed: false);

        bool ok = BlobFormat.TryParseHeader(header, out int entryCount,
            out int blobDataOffset, out int blobDataSize, out bool compressed);

        Assert.That(ok, Is.True);
        Assert.That(entryCount, Is.EqualTo(0));
        Assert.That(blobDataSize, Is.EqualTo(0));
        Assert.That(blobDataOffset, Is.EqualTo(BlobFormat.HeaderSize)); // 0 entries * 24 = 0
        Assert.That(compressed, Is.False);
    }

    [Test]
    public void TryParseHeader_LargeValues()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 100000, blobDataSize: 5000000, compressed: false);

        bool ok = BlobFormat.TryParseHeader(header, out int entryCount,
            out _, out int blobDataSize, out _);

        Assert.That(ok, Is.True);
        Assert.That(entryCount, Is.EqualTo(100000));
        Assert.That(blobDataSize, Is.EqualTo(5000000));
    }

    // ═══════════════════════════════════════════════════════
    // ExtractBlobData
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ExtractBlobData_Valid_ReturnsDataSegment()
    {
        // 构建完整 blob 文件
        byte[] blobDataSegment = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        byte[] fileBytes = BuildBlobFile(
            new[] { (new DualHash64(1, 2), 0, blobDataSegment.Length) },
            blobDataSegment,
            compressed: false);

        byte[] extracted = BlobFormat.ExtractBlobData(fileBytes);

        Assert.That(extracted.Length, Is.EqualTo(blobDataSegment.Length));
        Assert.That(extracted[0], Is.EqualTo(0x01));
        Assert.That(extracted[4], Is.EqualTo(0x05));
    }

    [Test]
    public void ExtractBlobData_BadMagic_Throws()
    {
        byte[] data = new byte[BlobFormat.HeaderSize];
        // 全部置零 → magic 不匹配

        var ex = Assert.Throws<InvalidOperationException>(() => BlobFormat.ExtractBlobData(data));
        Assert.That(ex.Message, Does.Contain("Invalid blob file"));
    }

    [Test]
    public void ExtractBlobData_Truncated_Throws()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        // 声明 blobDataSize 很大，但实际文件只有 header
        BlobFormat.WriteHeader(span, entryCount: 0, blobDataSize: 99999, compressed: false);

        var ex = Assert.Throws<InvalidOperationException>(() => BlobFormat.ExtractBlobData(header));
        Assert.That(ex.Message, Does.Contain("Truncated"));
    }

    [Test]
    public void ExtractBlobData_EmptyData()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 0, blobDataSize: 0, compressed: false);

        byte[] extracted = BlobFormat.ExtractBlobData(header);

        Assert.That(extracted.Length, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // ReadEntryTable
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ReadEntryTable_SingleEntry()
    {
        var entries = new[] { (new DualHash64(0x12345678ABCDEF00UL, 0x0FEDCBA987654321UL), 500, 256) };
        byte[] fileBytes = BuildBlobFile(entries, new byte[256], compressed: false);

        var table = BlobFormat.ReadEntryTable(fileBytes, entryCount: 1);

        Assert.That(table.Length, Is.EqualTo(1));
        Assert.That(table[0].Hash.XxHash64, Is.EqualTo(0x12345678ABCDEF00UL));
        Assert.That(table[0].Hash.FnvHash64, Is.EqualTo(0x0FEDCBA987654321UL));
        Assert.That(table[0].Offset, Is.EqualTo(500));
        Assert.That(table[0].Length, Is.EqualTo(256));
    }

    [Test]
    public void ReadEntryTable_MultipleEntries()
    {
        var entries = new (DualHash64, int, int)[]
        {
            (new DualHash64(0x01, 0x0A), 0, 100),
            (new DualHash64(0x02, 0x0B), 100, 200),
            (new DualHash64(0x03, 0x0C), 300, 150),
        };
        byte[] blobData = new byte[450];
        byte[] fileBytes = BuildBlobFile(entries, blobData, compressed: false);

        var table = BlobFormat.ReadEntryTable(fileBytes, entryCount: 3);

        Assert.That(table.Length, Is.EqualTo(3));
        Assert.That(table[0].Hash.FnvHash64, Is.EqualTo(0x0A));
        Assert.That(table[1].Offset, Is.EqualTo(100));
        Assert.That(table[2].Length, Is.EqualTo(150));
    }

    [Test]
    public void ReadEntryTable_EmptyTable()
    {
        byte[] header = new byte[BlobFormat.HeaderSize];
        var span = header.AsSpan();
        BlobFormat.WriteHeader(span, entryCount: 0, blobDataSize: 0, compressed: false);

        var table = BlobFormat.ReadEntryTable(header, entryCount: 0);

        Assert.That(table.Length, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // WriteHeader → TryParseHeader 往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void WriteHeader_RoundTrip_NoCompression()
    {
        byte[] buf = new byte[BlobFormat.HeaderSize];
        BlobFormat.WriteHeader(buf.AsSpan(), entryCount: 42, blobDataSize: 8192, compressed: false);

        bool ok = BlobFormat.TryParseHeader(buf, out int ec, out int bdo, out int bds, out bool comp);
        Assert.That(ok, Is.True);
        Assert.That(ec, Is.EqualTo(42));
        Assert.That(bds, Is.EqualTo(8192));
        Assert.That(bdo, Is.EqualTo(BlobFormat.HeaderSize + 42 * BlobFormat.EntrySize));
        Assert.That(comp, Is.False);
    }

    [Test]
    public void WriteHeader_RoundTrip_Compressed()
    {
        byte[] buf = new byte[BlobFormat.HeaderSize];
        BlobFormat.WriteHeader(buf.AsSpan(), entryCount: 7, blobDataSize: 4096, compressed: true);

        bool ok = BlobFormat.TryParseHeader(buf, out int ec, out int bdo, out int bds, out bool comp);
        Assert.That(ok, Is.True);
        Assert.That(ec, Is.EqualTo(7));
        Assert.That(bds, Is.EqualTo(4096));
        Assert.That(comp, Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // WriteEntry → ReadEntryTable 往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void WriteEntry_RoundTrip()
    {
        byte[] entryBuf = new byte[BlobFormat.EntrySize];
        BlobFormat.WriteEntry(entryBuf.AsSpan(),
            xxHash64: 0x1122334455667788UL,
            fnvHash64: 0x99AABBCCDDEEFF00UL,
            entryOffset: 2048,
            length: 512);

        // 将 entry 嵌入 header 后，用 ReadEntryTable 读回
        byte[] header = new byte[BlobFormat.HeaderSize];
        byte[] blobData = new byte[512];
        var headerSpan = header.AsSpan();
        BlobFormat.WriteHeader(headerSpan, entryCount: 1, blobDataSize: 512, compressed: false);

        byte[] fileBytes = new byte[BlobFormat.HeaderSize + BlobFormat.EntrySize];
        Buffer.BlockCopy(header, 0, fileBytes, 0, BlobFormat.HeaderSize);
        Buffer.BlockCopy(entryBuf, 0, fileBytes, BlobFormat.HeaderSize, BlobFormat.EntrySize);

        var table = BlobFormat.ReadEntryTable(fileBytes, entryCount: 1);

        Assert.That(table[0].Hash.XxHash64, Is.EqualTo(0x1122334455667788UL));
        Assert.That(table[0].Hash.FnvHash64, Is.EqualTo(0x99AABBCCDDEEFF00UL));
        Assert.That(table[0].Offset, Is.EqualTo(2048));
        Assert.That(table[0].Length, Is.EqualTo(512));
    }

    // ═══════════════════════════════════════════════════════
    // 完整往返：WriteHeader + WriteEntry → TryParseHeader + ReadEntryTable + ExtractBlobData
    // ═══════════════════════════════════════════════════════

    [Test]
    public void FullRoundTrip_ThreeEntries()
    {
        byte[] payload = new byte[600];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 256);

        var entries = new (DualHash64 hash, int offset, int length)[]
        {
            (new DualHash64(0xAA, 0x11), 0,   200),
            (new DualHash64(0xBB, 0x22), 200, 200),
            (new DualHash64(0xCC, 0x33), 400, 200),
        };

        byte[] fileBytes = BuildBlobFile(entries, payload, compressed: false);

        // 解析
        bool ok = BlobFormat.TryParseHeader(fileBytes, out int entryCount,
            out int blobDataOffset, out int blobDataSize, out bool compressed);
        Assert.That(ok, Is.True);
        Assert.That(entryCount, Is.EqualTo(3));
        Assert.That(blobDataSize, Is.EqualTo(600));
        Assert.That(compressed, Is.False);

        var table = BlobFormat.ReadEntryTable(fileBytes, entryCount);
        Assert.That(table.Length, Is.EqualTo(3));
        Assert.That(table[0].Hash.XxHash64, Is.EqualTo(0xAA));
        Assert.That(table[0].Offset, Is.EqualTo(0));
        Assert.That(table[0].Length, Is.EqualTo(200));
        Assert.That(table[2].Hash.FnvHash64, Is.EqualTo(0x33));
        Assert.That(table[2].Offset, Is.EqualTo(400));
        Assert.That(table[2].Length, Is.EqualTo(200));

        byte[] extracted = BlobFormat.ExtractBlobData(fileBytes);
        Assert.That(extracted.Length, Is.EqualTo(600));
        for (int i = 0; i < 600; i++)
            Assert.That(extracted[i], Is.EqualTo((byte)(i % 256)));
    }

    // ═══════════════════════════════════════════════════════
    // 常量验证
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Constants_AreCorrect()
    {
        Assert.That(BlobFormat.Magic, Is.EqualTo(0x42584C46U)); // "FLXB" LE
        Assert.That(BlobFormat.HeaderSize, Is.EqualTo(20));
        Assert.That(BlobFormat.EntrySize, Is.EqualTo(24));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 构建一个完整的 .blob 文件字节数组（header + entry table + blob data）。
    /// </summary>
    private static byte[] BuildBlobFile(
        (DualHash64 hash, int offset, int length)[] entries,
        byte[] blobData,
        bool compressed)
    {
        int headerSize = BlobFormat.HeaderSize;
        int entryTableSize = entries.Length * BlobFormat.EntrySize;
        int totalSize = headerSize + entryTableSize + blobData.Length;

        byte[] fileBytes = new byte[totalSize];
        var span = fileBytes.AsSpan();

        BlobFormat.WriteHeader(span, entries.Length, blobData.Length, compressed);

        int entryOffset = headerSize;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            BlobFormat.WriteEntry(
                span.Slice(entryOffset),
                e.hash.XxHash64,
                e.hash.FnvHash64,
                e.offset,
                e.length);
            entryOffset += BlobFormat.EntrySize;
        }

        Buffer.BlockCopy(blobData, 0, fileBytes, entryOffset, blobData.Length);
        return fileBytes;
    }
}
