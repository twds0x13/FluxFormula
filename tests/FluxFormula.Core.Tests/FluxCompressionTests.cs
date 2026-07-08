using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FluxCompression 压缩/解压往返测试。
/// </summary>
public class FluxCompressionTests
{
    [Test]
    public void Compress_Decompress_RoundTrip()
    {
        var rng = new Random(42);
        var raw = new byte[4096];
        rng.NextBytes(raw);

        byte[] compressed = FluxCompression.Compress(raw);
        byte[] decompressed = FluxCompression.Decompress(compressed);

        Assert.That(decompressed, Is.EqualTo(raw),
            "压缩→解压往返应完全一致");
    }

    [Test]
    public void Compress_SmallData_Passthrough()
    {
        // 极小数据压缩无收益，自动回退为 None 算法（直接存储）
        var raw = new byte[] { 0x01, 0x02, 0x03 };

        byte[] compressed = FluxCompression.Compress(raw);

        Assert.That(FluxCompression.IsCompressed(compressed), Is.True,
            "应带压缩头部");
        Assert.That(compressed.Length, Is.EqualTo(FluxCompression.HeaderSize + raw.Length),
            "小数据应走 None（passthrough）：头部 + 原始数据");
        Assert.That(compressed[1], Is.EqualTo(0),
            "Algorithm=0 即 None");

        byte[] decompressed = FluxCompression.Decompress(compressed);
        Assert.That(decompressed, Is.EqualTo(raw));
    }

    [Test]
    public void Compress_Empty_ReturnsEmpty()
    {
        var compressed = FluxCompression.Compress(ReadOnlySpan<byte>.Empty);
        Assert.That(compressed.Length, Is.EqualTo(0));
    }

    [Test]
    public void Decompress_Empty_ReturnsEmpty()
    {
        var decompressed = FluxCompression.Decompress(ReadOnlySpan<byte>.Empty);
        Assert.That(decompressed.Length, Is.EqualTo(0));
    }

    [Test]
    public void IsCompressed_ValidHeader_ReturnsTrue()
    {
        byte[] data = { FluxCompression.Magic, 0x01, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        Assert.That(FluxCompression.IsCompressed(data), Is.True);
    }

    [Test]
    public void IsCompressed_PlainData_ReturnsFalse()
    {
        // 旧版纯字节码（无压缩头）
        var raw = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        Assert.That(FluxCompression.IsCompressed(raw), Is.False);
    }

    [Test]
    public void IsCompressed_TooShort_ReturnsFalse()
    {
        var shortData = new byte[] { FluxCompression.Magic, 0x01, 0x00 };
        Assert.That(FluxCompression.IsCompressed(shortData), Is.False,
            "长度 < HeaderSize 不应被识别为压缩数据");
    }

    [Test]
    public void PeekUncompressedLength_ReturnsCorrectValue()
    {
        var raw = new byte[256];
        new Random(123).NextBytes(raw);
        byte[] compressed = FluxCompression.Compress(raw);

        int peeked = FluxCompression.PeekUncompressedLength(compressed);
        Assert.That(peeked, Is.EqualTo(raw.Length));
    }

    [Test]
    public void PeekUncompressedLength_PlainData_ReturnsDataLength()
    {
        var raw = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        int peeked = FluxCompression.PeekUncompressedLength(raw);
        Assert.That(peeked, Is.EqualTo(4));
    }

    [Test]
    public void Compress_LargerData_BrotliCompression()
    {
        // 具有重复模式的数据 → Brotli 有效压缩
        var raw = new byte[8192];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = (byte)(i % 16); // 高度可压缩

        byte[] compressed = FluxCompression.Compress(raw);

        Assert.That(FluxCompression.IsCompressed(compressed), Is.True);
        Assert.That(compressed.Length, Is.LessThan(raw.Length),
            "高度重复数据应产生有效压缩");

        string algo = FluxCompression.GetAlgorithmName(compressed);
        Assert.That(algo, Is.EqualTo("Brotli"));

        byte[] decompressed = FluxCompression.Decompress(compressed);
        Assert.That(decompressed, Is.EqualTo(raw));
    }

    [Test]
    public void Decompress_InvalidMagic_Throws()
    {
        var badData = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.That(() => FluxCompression.Decompress(badData),
            Throws.TypeOf<System.IO.InvalidDataException>());
    }

    [Test]
    public void GetAlgorithmName_PlainData_ReturnsUnknown()
    {
        var raw = new byte[] { 0x00, 0x01 };
        Assert.That(FluxCompression.GetAlgorithmName(raw), Is.EqualTo("Unknown"));
    }

    [Test]
    public void GetAlgorithmName_Compressed_ReturnsAlgorithm()
    {
        var raw = new byte[1024];
        new Random().NextBytes(raw);
        byte[] compressed = FluxCompression.Compress(raw);
        string algo = FluxCompression.GetAlgorithmName(compressed);

        Assert.That(algo, Is.AnyOf("None", "Brotli"),
            "算法名应为 None（回退）或 Brotli（压缩成功）");
    }

    [Test]
    public void GetAlgorithmName_BadAlgorithmByte_ReturnsUnknown()
    {
        // 有效 magic + header，但 algorithm byte 为非法值 99
        var data = new byte[FluxCompression.HeaderSize];
        data[0] = FluxCompression.Magic;  // 0xAB
        data[1] = 99;                     // 非法算法号

        string algo = FluxCompression.GetAlgorithmName(data);
        Assert.That(algo, Is.EqualTo("Unknown"));
    }

    [Test]
    public void Decompress_BadAlgorithm_Throws()
    {
        // 有效 header 结构，但算法号非法
        var data = new byte[FluxCompression.HeaderSize + 4];
        data[0] = FluxCompression.Magic;
        data[1] = 99; // 非法算法

        var ex = Assert.Throws<System.IO.InvalidDataException>(
            () => FluxCompression.Decompress(data));
        Assert.That(ex.Message, Does.Contain("unknown algorithm"));
    }

    [Test]
    public void Decompress_TruncatedBrotli_Throws()
    {
        // 构建有效的 Brotli 压缩数据然后截断 payload
        var raw = new byte[8192];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = (byte)(i % 16);

        byte[] compressed = FluxCompression.Compress(raw);
        // 取 header + 少量字节（远少于原始数据）
        byte[] truncated = new byte[FluxCompression.HeaderSize + 4];
        System.Buffer.BlockCopy(compressed, 0, truncated, 0, truncated.Length);

        // 修正 header 中的解压长度（保持原始值，但数据不足）
        Assert.That(() => FluxCompression.Decompress(truncated),
            Throws.Exception); // Brotli 解压肯定会失败
    }
}
