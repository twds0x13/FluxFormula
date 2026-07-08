using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// BlobEntry 结构体单元测试——构造、相等性、GetHashCode、ToString。
/// </summary>
public class BlobEntryTests
{
    // ═══════════════════════════════════════════════════════
    // 构造
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Constructor_SetsAllFields()
    {
        var hash = new DualHash64(0x12345678ABCDEF00UL, 0xFEDCBA9876543210UL);
        var entry = new BlobEntry(hash, offset: 1024, length: 256);

        Assert.That(entry.Hash, Is.EqualTo(hash));
        Assert.That(entry.Offset, Is.EqualTo(1024));
        Assert.That(entry.Length, Is.EqualTo(256));
    }

    [Test]
    public void Constructor_ZeroOffsetAndLength()
    {
        var hash = DualHash64.Compute(new byte[] { 1, 2, 3 });
        var entry = new BlobEntry(hash, 0, 0);

        Assert.That(entry.Offset, Is.EqualTo(0));
        Assert.That(entry.Length, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // Equals
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Equals_SameValues_ReturnsTrue()
    {
        var hash = new DualHash64(0xAAAA, 0xBBBB);
        var a = new BlobEntry(hash, 100, 200);
        var b = new BlobEntry(hash, 100, 200);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void Equals_DifferentHash_ReturnsFalse()
    {
        var h1 = new DualHash64(0x1111, 0x2222);
        var h2 = new DualHash64(0x3333, 0x4444);
        var a = new BlobEntry(h1, 100, 200);
        var b = new BlobEntry(h2, 100, 200);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equals_DifferentOffset_ReturnsFalse()
    {
        var hash = new DualHash64(0xAAAA, 0xBBBB);
        var a = new BlobEntry(hash, 100, 200);
        var b = new BlobEntry(hash, 999, 200);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equals_DifferentLength_ReturnsFalse()
    {
        var hash = new DualHash64(0xAAAA, 0xBBBB);
        var a = new BlobEntry(hash, 100, 200);
        var b = new BlobEntry(hash, 100, 999);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equals_Null_ReturnsFalse()
    {
        var entry = new BlobEntry(new DualHash64(1, 2), 0, 0);
        Assert.That(entry.Equals(null), Is.False);
    }

    [Test]
    public void Equals_Object_SameValues_ReturnsTrue()
    {
        var hash = new DualHash64(0xCAFE, 0xBABE);
        var a = new BlobEntry(hash, 50, 75);
        object b = new BlobEntry(hash, 50, 75);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void Equals_Object_WrongType_ReturnsFalse()
    {
        var entry = new BlobEntry(new DualHash64(1, 2), 0, 0);
        Assert.That(entry.Equals("not a BlobEntry"), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // GetHashCode
    // ═══════════════════════════════════════════════════════

    [Test]
    public void GetHashCode_SameValues_SameHashCode()
    {
        var hash = new DualHash64(0xDEAD, 0xBEEF);
        var a = new BlobEntry(hash, 42, 128);
        var b = new BlobEntry(hash, 42, 128);

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentValues_UsuallyDifferent()
    {
        // 不保证一定不同，但应极大概率不同
        var h1 = new DualHash64(0x1111111111111111UL, 0x2222222222222222UL);
        var h2 = new DualHash64(0x3333333333333333UL, 0x4444444444444444UL);
        var a = new BlobEntry(h1, 100, 200);
        var b = new BlobEntry(h2, 300, 400);

        Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
    }

    // ═══════════════════════════════════════════════════════
    // ToString
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToString_ContainsHashAndOffsetAndLength()
    {
        var hash = new DualHash64(0x1234, 0x5678);
        var entry = new BlobEntry(hash, 500, 256);

        string s = entry.ToString();
        Assert.That(s, Does.Contain("500"));
        Assert.That(s, Does.Contain("256"));
        Assert.That(s, Does.Contain("@"));
        Assert.That(s, Does.Contain("len="));
    }
}
