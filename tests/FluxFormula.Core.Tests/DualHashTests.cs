using System;
using System.IO.Hashing;
using System.Text;
using FluxFormula.Core;
using NUnit.Framework;

public class DualHashTests
{
    // ═══════════════════════════════════════════════════════
    // xxHash64 — 与 .NET 9 System.IO.Hashing.XxHash64 交叉验证
    // ═══════════════════════════════════════════════════════

    private static void AssertXxHashMatches(byte[] data)
    {
        ulong expected = XxHash64.HashToUInt64(data);
        var h = DualHash64.Compute(data);
        Assert.That(h.XxHash64, Is.EqualTo(expected),
            $"xxHash64({BytesToHex(data)}) 应与 System.IO.Hashing.XxHash64 一致");
    }

    [Test]
    public void XxHash64_EmptyString_MatchesReference()
    {
        AssertXxHashMatches(Array.Empty<byte>());
    }

    [Test]
    public void XxHash64_ShortStrings_MatchesReference()
    {
        AssertXxHashMatches(Encoding.ASCII.GetBytes("abc"));
        AssertXxHashMatches(Encoding.ASCII.GetBytes("foo"));
        AssertXxHashMatches(Encoding.ASCII.GetBytes("bar"));
        AssertXxHashMatches(Encoding.ASCII.GetBytes("hello"));
    }

    [Test]
    public void XxHash64_32Bytes_ExactlyOneStripe_MatchesReference()
    {
        // 刚好 32 字节——分条路径边界
        var data = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyz012345");
        Assert.That(data.Length, Is.EqualTo(32));
        AssertXxHashMatches(data);
    }

    [Test]
    public void XxHash64_LongString_MatchesReference()
    {
        AssertXxHashMatches(
            Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"));
        AssertXxHashMatches(
            Encoding.ASCII.GetBytes("0123456789abcdefghijklmnopqrstuvwxyz"));
    }

    [Test]
    public void XxHash64_256Bytes_MultipleStripes_MatchesReference()
    {
        var data = new byte[256];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        AssertXxHashMatches(data);
    }

    [Test]
    public void XxHash64_SingleByte_MatchesReference()
    {
        AssertXxHashMatches(new byte[] { 0xFF });
        AssertXxHashMatches(new byte[] { 0x00 });
        AssertXxHashMatches(new byte[] { 0x42 });
    }

    [Test]
    public void XxHash64_AllSizes_1To128_MatchesReference()
    {
        // 覆盖所有分条边界：1 字节 → 128 字节
        for (int len = 1; len <= 128; len++)
        {
            var data = new byte[len];
            for (int i = 0; i < len; i++) data[i] = (byte)((i * 7 + 13) & 0xFF);
            AssertXxHashMatches(data);
        }
    }

    [Test]
    public void XxHash64_Seed_ChangesOutput()
    {
        var data = Encoding.ASCII.GetBytes("hello");
        var h1 = DualHash64.Compute(data);
        var h2 = DualHash64.ComputeSeeded(data, 42UL);
        Assert.That(h1.XxHash64, Is.Not.EqualTo(h2.XxHash64),
            "不同种子应产生不同 xxHash64");
        Assert.That(h1.FnvHash64, Is.EqualTo(h2.FnvHash64),
            "FNV 不依赖种子，应保持不变");
    }

    // ═══════════════════════════════════════════════════════
    // FNV-1a 64 — 自洽性验证（无内置 .NET 参考实现）
    // 基准：空字符串 FNV-1a 64("") = 0xCBF29CE484222325 是已知标准向量
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Fnv1a64_EmptyString_MatchesStandardVector()
    {
        var h = DualHash64.Compute(Array.Empty<byte>());
        Assert.That(h.FnvHash64, Is.EqualTo(0xCBF29CE484222325UL),
            "FNV-1a 64(\"\") 应与 FNV 规范基准一致");
    }

    [Test]
    public void Fnv1a64_Deterministic()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var h1   = DualHash64.Compute(data);
        var h2   = DualHash64.Compute(data);
        Assert.That(h1.FnvHash64, Is.EqualTo(h2.FnvHash64));
    }

    [Test]
    public void Fnv1a64_DifferentData_ProducesDifferentHash()
    {
        var h1 = DualHash64.Compute(new byte[] { 1, 2, 3 });
        var h2 = DualHash64.Compute(new byte[] { 1, 2, 4 });
        Assert.That(h1.FnvHash64, Is.Not.EqualTo(h2.FnvHash64),
            "修改一个字节应改变 FNV 输出");
    }

    [Test]
    public void Fnv1a64_NotEmpty_NotEqualBasis()
    {
        // 非空输入不应返回 FNV_BASIS
        var h = DualHash64.Compute(new byte[] { 0x61 });
        Assert.That(h.FnvHash64, Is.Not.EqualTo(0xCBF29CE484222325UL),
            "非空输入的 FNV 不应等于初始 basis 值");
    }

    [Test]
    public void Fnv1a64_AllZeros_DifferentFromEmpty()
    {
        var hEmpty = DualHash64.Compute(Array.Empty<byte>());
        var hZeros = DualHash64.Compute(new byte[] { 0, 0, 0, 0 });
        Assert.That(hEmpty.FnvHash64, Is.Not.EqualTo(hZeros.FnvHash64),
            "空输入和全零字节序列应产生不同 FNV");
    }

    // ═══════════════════════════════════════════════════════
    // DualHash64 结构体
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Equality_SameHash_ReturnsTrue()
    {
        var a = new DualHash64(0xABCD, 0x1234);
        var b = new DualHash64(0xABCD, 0x1234);
        Assert.That(a.Equals(b));
        Assert.That(a == b);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equality_DifferentXx_ReturnsFalse()
    {
        var a = new DualHash64(0xAAAA, 0x1234);
        var b = new DualHash64(0xBBBB, 0x1234);
        Assert.That(!a.Equals(b));
        Assert.That(a != b);
    }

    [Test]
    public void Equality_DifferentFnv_ReturnsFalse()
    {
        var a = new DualHash64(0xABCD, 0xAAAA);
        var b = new DualHash64(0xABCD, 0xBBBB);
        Assert.That(!a.Equals(b));
    }

    [Test]
    public void ToString_32HexChars_RoundTrip()
    {
        var data   = Encoding.ASCII.GetBytes("test data for roundtrip");
        var hash   = DualHash64.Compute(data);
        var str    = hash.ToString();
        Assert.That(str.Length, Is.EqualTo(32));
        var parsed = DualHash64.Parse(str);
        Assert.That(hash, Is.EqualTo(parsed));
    }

    [Test]
    public void Parse_CaseInsensitive()
    {
        var lower = DualHash64.Parse("abcdef0123456789abcdef0123456789");
        var upper = DualHash64.Parse("ABCDEF0123456789ABCDEF0123456789");
        Assert.That(lower, Is.EqualTo(upper));
    }

    [Test]
    public void Parse_WrongLength_Throws()
    {
        Assert.Throws<FormatException>(() => DualHash64.Parse("too-short"));
    }

    [Test]
    public void Parse_InvalidHexChar_Throws()
    {
        Assert.Throws<FormatException>(() =>
            DualHash64.Parse("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG"));
    }

    [Test]
    public void Compute_DifferentData_DifferentDualHash()
    {
        var h1 = DualHash64.Compute(new byte[] { 1, 2, 3 });
        var h2 = DualHash64.Compute(new byte[] { 1, 2, 4 });
        Assert.That(h1, Is.Not.EqualTo(h2));
    }

    [Test]
    public void Compute_LargeInput_NonZero()
    {
        var data = new byte[1024];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        var h = DualHash64.Compute(data);
        Assert.That(h.XxHash64, Is.Not.EqualTo(0UL));
        Assert.That(h.FnvHash64, Is.Not.EqualTo(0UL));
    }

    // ═══════════════════════════════════════════════════════
    // Combine（累进 key 计算——为 Connect 链路设计）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Combine_IsOrderSensitive()
    {
        var hA = DualHash64.Compute(new byte[] { 1 });
        var hB = DualHash64.Compute(new byte[] { 2 });
        var hC = DualHash64.Compute(new byte[] { 3 });

        var leftFirst  = DualHash64.Combine(DualHash64.Combine(hA, hB), hC);
        var rightFirst = DualHash64.Combine(DualHash64.Combine(hA, hC), hB);

        Assert.That(leftFirst, Is.Not.EqualTo(rightFirst),
            "Combine 必须顺序敏感：A→B→C ≠ A→C→B");
    }

    [Test]
    public void Combine_WithSelf_ChangesOutput()
    {
        var hA       = DualHash64.Compute(new byte[] { 42 });
        var combined = DualHash64.Combine(hA, hA);
        Assert.That(combined, Is.Not.EqualTo(hA));
    }

    [Test]
    public void Combine_SimulatesConnectChainKey()
    {
        // Connect(A, B) 的 key = Combine(hash(A), hash(B))
        var fA = Encoding.ASCII.GetBytes("formula_A");
        var fB = Encoding.ASCII.GetBytes("formula_B");

        var hA = DualHash64.Compute(fA);
        var hB = DualHash64.Compute(fB);
        var chainKey = DualHash64.Combine(hA, hB);

        // 重算完整拼接 → 应不同（Combine 是 O(1) 代数混合，不重新扫描）
        var concat = new byte[fA.Length + fB.Length];
        Buffer.BlockCopy(fA, 0, concat, 0, fA.Length);
        Buffer.BlockCopy(fB, 0, concat, fA.Length, fB.Length);
        var directHash = DualHash64.Compute(concat);

        Assert.That(chainKey, Is.Not.EqualTo(directHash),
            "Combine O(1) 混合 ≠ 重新扫描字节码计算哈希");
        Assert.That(chainKey.XxHash64, Is.Not.EqualTo(0UL));
        Assert.That(chainKey.FnvHash64, Is.Not.EqualTo(0UL));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    private static string BytesToHex(byte[] data)
    {
        if (data.Length == 0) return "\"\"";
        if (data.Length <= 16)
            return $"\"{Encoding.ASCII.GetString(data)}\"";
        return $"({data.Length} bytes)";
    }
}
