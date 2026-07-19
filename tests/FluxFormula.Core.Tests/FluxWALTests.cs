using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

// ═══════════════════════════════════════════════════════
// 测试用元数据类型
// ═══════════════════════════════════════════════════════

public struct TestMeta
{
    public long Timestamp;
    public int ResultCode;
}

// ═══════════════════════════════════════════════════════
// FramePtr
// ═══════════════════════════════════════════════════════

public class FramePtrTests
{
    [Test]
    public void Lsn_StoresValue()
    {
        var fp = new FramePtr(42);
        Assert.That(fp.Lsn, Is.EqualTo(42));
    }

    [Test]
    public void Equality_SameLsn_ReturnsTrue()
    {
        Assert.That(new FramePtr(100) == new FramePtr(100), Is.True);
    }

    [Test]
    public void Equality_DifferentLsn_ReturnsFalse()
    {
        Assert.That(new FramePtr(100) != new FramePtr(200), Is.True);
    }

    [Test]
    public void Comparison_Sorting()
    {
        Assert.That(new FramePtr(10) < new FramePtr(20), Is.True);
    }

    [Test]
    public void Contains_InRange_ReturnsTrue()
    {
        Assert.That(FramePtr.Contains(new FramePtr(0), new FramePtr(100), 50), Is.True);
    }

    [Test]
    public void Contains_OutOfRange_ReturnsFalse()
    {
        Assert.That(FramePtr.Contains(new FramePtr(50), new FramePtr(100), 100), Is.False);
    }

    [Test] public void Comparison_GreaterThan() => Assert.That(new FramePtr(20) > new FramePtr(10));
    [Test] public void Comparison_GreaterOrEqual_Same() => Assert.That(new FramePtr(10) >= new FramePtr(10));
    [Test] public void Comparison_GreaterOrEqual_Greater() => Assert.That(new FramePtr(20) >= new FramePtr(10));
    [Test] public void Comparison_LessOrEqual_Same() => Assert.That(new FramePtr(10) <= new FramePtr(10));
    [Test] public void Comparison_LessOrEqual_Less() => Assert.That(new FramePtr(5) <= new FramePtr(10));
    [Test] public void CompareTo_Sorting() => Assert.That(new FramePtr(5).CompareTo(new FramePtr(10)), Is.LessThan(0));
    [Test] public void Addition_Offset() => Assert.That((new FramePtr(100) + 50UL).Lsn, Is.EqualTo(150));
    [Test] public void Subtraction_Offset() => Assert.That((new FramePtr(100) - 30UL).Lsn, Is.EqualTo(70));
    [Test] public void Equals_Object_SameLsn() => Assert.That(new FramePtr(42).Equals((object)new FramePtr(42)));
    [Test] public void Equals_Object_DifferentLsn() => Assert.That(new FramePtr(42).Equals((object)new FramePtr(99)), Is.False);
    [Test] public void Equals_Object_Null() => Assert.That(new FramePtr(42).Equals(null), Is.False);
    [Test] public void ToString_ContainsLsn() => Assert.That(new FramePtr(123).ToString(), Does.Contain("123"));
}

// ═══════════════════════════════════════════════════════
// WALFormat
// ═══════════════════════════════════════════════════════

public class WALFormatTests
{
    [Test]
    public void IsWal_ValidMagic_ReturnsTrue()
    {
        byte[] data = new byte[24];
        data[0] = (byte)'F'; data[1] = (byte)'L'; data[2] = (byte)'X'; data[3] = (byte)'W';
        Assert.That(WALFormat.IsWal(data), Is.True);
    }

    [Test]
    public void IsWal_BadMagic_ReturnsFalse()
    {
        Assert.That(WALFormat.IsWal(new byte[24]), Is.False);
    }

    [Test]
    public void IsWal_TooShort_ReturnsFalse()
    {
        Assert.That(WALFormat.IsWal(new byte[3]), Is.False);
    }

    [Test]
    public void TryParseHeader_Valid_ReturnsTrue()
    {
        byte[] buf = new byte[256];
        int off = 0;
        WALFormat.WriteHeader(buf, ref off, 3, 24, 48, 100);

        Assert.That(WALFormat.TryParseHeader(buf, out WALHeader h), Is.True);
        Assert.That(h.FrameCount, Is.EqualTo(3));
        Assert.That(h.FrameTableOffset, Is.EqualTo(24));
        Assert.That(h.CheckpointLength, Is.EqualTo(100));
        Assert.That(h.HasCheckpoint, Is.True);
    }

    [Test]
    public void TryParseHeader_NoCheckpoint_HasCheckpointFalse()
    {
        byte[] buf = new byte[256];
        int off = 0;
        WALFormat.WriteHeader(buf, ref off, 1, 24, 0, 0);

        Assert.That(WALFormat.TryParseHeader(buf, out WALHeader h), Is.True);
        Assert.That(h.HasCheckpoint, Is.False);
    }

    [Test]
    public void TryParseHeader_TooShort_ReturnsFalse()
    {
        Assert.That(WALFormat.TryParseHeader(new byte[10], out _), Is.False);
    }

    [Test]
    public void WriteReadFramePtr_RoundTrip()
    {
        byte[] buf = new byte[32];
        int off = 0;
        WALFormat.WriteFramePtr(buf, ref off, new FramePtr(0xABCD1234));

        var restored = WALFormat.ReadFramePtr(buf, 0, 0);
        Assert.That(restored.Lsn, Is.EqualTo(0xABCD1234));
    }
}

// ═══════════════════════════════════════════════════════
// WALEntry
// ═══════════════════════════════════════════════════════

public class WALEntryTests
{
    [Test]
    public void EntryView_ReadsFormulaHash()
    {
        var hash = new DualHash64(0xAAAA, 0xBBBB);
        byte[] serialized = FluxWAL.SerializeEntry(hash,
            Array.Empty<(string, byte[])>(), Array.Empty<byte>());

        var entry = new WALEntry<TestMeta>(serialized, 0, serialized.Length);
        Assert.That(entry.FormulaHash.XxHash64, Is.EqualTo(0xAAAA));
        Assert.That(entry.FormulaHash.FnvHash64, Is.EqualTo(0xBBBB));
    }

    [Test]
    public void EntryView_ReadsBindingCount()
    {
        var bindings = new (string, byte[])[] { ("atk", new byte[] { 1, 0, 0, 0 }) };
        byte[] serialized = FluxWAL.SerializeEntry(new DualHash64(1, 2), bindings, Array.Empty<byte>());

        var entry = new WALEntry<TestMeta>(serialized, 0, serialized.Length);
        Assert.That(entry.BindingCount, Is.EqualTo(1));
    }

    [Test]
    public void EnumerateBindings_SingleBinding()
    {
        var bindings = new (string, byte[])[] { ("atk", new byte[] { 100, 0, 0, 0 }) };
        byte[] serialized = FluxWAL.SerializeEntry(new DualHash64(1, 2), bindings, Array.Empty<byte>());

        var entry = new WALEntry<TestMeta>(serialized, 0, serialized.Length);
        int count = 0;
        foreach (var (name, value) in entry.GetBindings())
        {
            Assert.That(name, Is.EqualTo("atk"));
            Assert.That(value[0], Is.EqualTo(100));
            count++;
        }
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void EnumerateBindings_MultipleBindings()
    {
        var bindings = new (string, byte[])[] {
            ("x", new byte[] { 1 }), ("y", new byte[] { 2 }), ("z", new byte[] { 3 })
        };
        byte[] serialized = FluxWAL.SerializeEntry(new DualHash64(1, 2), bindings, Array.Empty<byte>());

        var entry = new WALEntry<TestMeta>(serialized, 0, serialized.Length);
        var names = new List<string>();
        foreach (var (name, _) in entry.GetBindings())
            names.Add(name);
        Assert.That(names, Is.EqualTo(new[] { "x", "y", "z" }));
    }

    [Test]
    public void EntryView_ReadsMeta()
    {
        var meta = new TestMeta { Timestamp = 0x12345678ABCD, ResultCode = 42 };
        byte[] metaBytes = new byte[Unsafe.SizeOf<TestMeta>()];
        Unsafe.WriteUnaligned(ref metaBytes[0], meta);

        byte[] serialized = FluxWAL.SerializeEntry(new DualHash64(1, 2),
            new (string, byte[])[] { ("a", new byte[] { 1 }) }, metaBytes);

        var entry = new WALEntry<TestMeta>(serialized, 0, serialized.Length);
        var restored = entry.Meta;
        Assert.That(restored.Timestamp, Is.EqualTo(0x12345678ABCD));
        Assert.That(restored.ResultCode, Is.EqualTo(42));
    }
}

// ═══════════════════════════════════════════════════════
// FluxWAL 集成测试
// ═══════════════════════════════════════════════════════

public class FluxWALIntegrationTests
{
    private string _testDir;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"wal_test_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Append_IncrementsLsn()
    {
        using var wal = new FluxWAL(_testDir);
        var before = wal.CurrentLsn;
        wal.Append(new DualHash64(1, 2),
            new (string, byte[])[] { ("x", new byte[] { 1, 2, 3, 4 }) },
            Array.Empty<byte>());
        Assert.That(wal.CurrentLsn.Lsn, Is.GreaterThan(before.Lsn));
    }

    [Test]
    public void Append_MultipleEntries_MonotonicLsn()
    {
        using var wal = new FluxWAL(_testDir);
        var hash = new DualHash64(1, 2);
        var lsn1 = wal.Append(hash, new (string, byte[])[] { ("a", new byte[] { 1 }) }, Array.Empty<byte>());
        var lsn2 = wal.Append(hash, new (string, byte[])[] { ("b", new byte[] { 2 }) }, Array.Empty<byte>());
        Assert.That(lsn2.Lsn, Is.GreaterThan(lsn1.Lsn));
    }

    [Test]
    public void CommitFrame_WithEntries_ReturnsFrameIndex()
    {
        using var wal = new FluxWAL(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
        int frame = wal.CommitFrame();
        Assert.That(frame, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void CommitFrame_NoEntries_ReturnsNegativeOne()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.That(wal.CommitFrame(), Is.EqualTo(-1));
    }

    [Test]
    public void GetFramePtr_ReturnsCorrectLsn()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.That(wal.GetFramePtr(0).Lsn, Is.EqualTo(0));
    }

    [Test]
    public void TryGetFrameRange_ValidFrame_ReturnsBounds()
    {
        using var wal = new FluxWAL(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
        wal.CommitFrame();

        Assert.That(wal.TryGetFrameRange(1, out FramePtr start, out FramePtr end), Is.True);
        Assert.That(start.Lsn, Is.EqualTo(0));
        Assert.That(end.Lsn, Is.GreaterThan(0));
    }

    [Test]
    public void Checkpoint_WritesAndReadsBack()
    {
        using var wal = new FluxWAL(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
        wal.CommitFrame();

        byte[] ckpt = new byte[] { 0xCA, 0xFE };
        wal.Checkpoint(ckpt);

        Assert.That(wal.HasCheckpoint, Is.True);
        Assert.That(wal.ReadCheckpoint(), Is.EqualTo(ckpt));
    }

    [Test]
    public void Delete_RemovesFile()
    {
        using var wal = new FluxWAL(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
        wal.CommitFrame();
        wal.Delete();
        Assert.That(File.Exists(Path.Combine(_testDir, "flux.wal")), Is.False);
    }

    [Test]
    public void GetFramePtr_NegativeIndex_Throws()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.Throws<ArgumentOutOfRangeException>(() => wal.GetFramePtr(-1));
    }

    [Test]
    public void GetFramePtr_OutOfRange_Throws()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.Throws<ArgumentOutOfRangeException>(() => wal.GetFramePtr(999));
    }

    [Test]
    public void TryGetFrameRange_IndexZero_ReturnsFalse()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.That(wal.TryGetFrameRange(0, out _, out _), Is.False);
    }

    [Test]
    public void Checkpoint_NullArgument_Throws()
    {
        using var wal = new FluxWAL(_testDir);
        Assert.Throws<ArgumentNullException>(() => wal.Checkpoint(null!));
    }

    [Test]
    public void CommitFrame_DoubleCall_ReturnsNegativeOne()
    {
        using var wal = new FluxWAL(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
        Assert.That(wal.CommitFrame(), Is.GreaterThanOrEqualTo(1));
        Assert.That(wal.CommitFrame(), Is.EqualTo(-1));
    }
}

// ═══════════════════════════════════════════════════════
// FluxWAL<TMeta> 集成测试
// ═══════════════════════════════════════════════════════

public class FluxWALTMetaTests
{
    private string _testDir;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"wal_tmeta_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Append_TypedMeta_Succeeds()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        var meta = new TestMeta { Timestamp = 0xDEADBEEF, ResultCode = 99 };
        wal.Append(new DualHash64(0xAAAA, 0xBBBB),
            new (string, byte[])[] { ("atk", new byte[] { 100, 0, 0, 0 }) }, meta);
        Assert.That(wal.CurrentLsn.Lsn, Is.GreaterThan(0));
    }

    [Test]
    public void CommitFrame_DelegatesToInner()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), default);
        Assert.That(wal.CommitFrame(), Is.EqualTo(1));
        Assert.That(wal.FrameCount, Is.EqualTo(2));
    }

    [Test]
    public void Properties_DelegatedCorrectly()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        Assert.That(wal.CurrentLsn.Lsn, Is.EqualTo(0));
        Assert.That(wal.FrameCount, Is.EqualTo(1));
        Assert.That(wal.HasCheckpoint, Is.False);
    }

    [Test]
    public void MultipleAppends_DifferentMeta_NoInterference()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), new TestMeta { Timestamp = 100 });
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), new TestMeta { Timestamp = 200 });
        Assert.That(wal.CurrentLsn.Lsn, Is.GreaterThan(0));
    }

    [Test]
    public void Delete_DelegatesToInner()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Append(new DualHash64(1, 2), Array.Empty<(string, byte[])>(), default);
        wal.CommitFrame();
        wal.Delete();
        Assert.That(File.Exists(Path.Combine(_testDir, "flux.wal")), Is.False);
    }

    [Test]
    public void Append_WithFormula_Succeeds()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = assembler.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });
        var lsn = wal.Append<float, FloatMathDef>(formula, Array.Empty<(string, byte[])>(), new TestMeta());
        Assert.That(wal.CurrentLsn.Lsn, Is.GreaterThan(0));
    }

    [Test]
    public void CheckpointLsn_AfterCheckpoint_HasValue()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = assembler.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var curry = assembler.Curry(formula);
        wal.Checkpoint<float, FloatMathDef>(curry);
        Assert.That(wal.CheckpointLsn.Lsn, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetFramePtr_DelegatesToInner()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        Assert.That(wal.GetFramePtr(0).Lsn, Is.EqualTo(0));
    }

    [Test]
    public void TryGetFrameRange_DelegatesToInner()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        Assert.That(wal.TryGetFrameRange(1, out _, out _), Is.False);
    }
}

// ═══════════════════════════════════════════════════════
// FluxWAL<TMeta> — Replay
// ═══════════════════════════════════════════════════════

public class FluxWALReplayTests
{
    private string _testDir;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"wal_replay_{Guid.NewGuid():N}");
        FormulaCache.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private static byte[] FloatBytes(float v)
    {
        byte[] b = new byte[4];
        Unsafe.WriteUnaligned(ref b[0], v);
        return b;
    }

    [Test]
    public void Replay_SingleEntry_ReturnsOneResult()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula);

        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("x", FloatBytes(2f)) },
            new TestMeta { Timestamp = 1, ResultCode = 200 });

        var results = new List<(WALEntry<TestMeta> Entry, float Result)>();
        foreach (var r in wal.Replay<float, FloatMathDef>(evaluator, new FramePtr(0)))
            results.Add(r);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Result, Is.EqualTo(3f).Within(1e-6f));
        Assert.That(results[0].Entry.Meta.Timestamp, Is.EqualTo(1));
        Assert.That(results[0].Entry.Meta.ResultCode, Is.EqualTo(200));
    }

    [Test]
    public void Replay_MultipleEntries_AccumulatesResults()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula);

        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("x", FloatBytes(2f)) },
            new TestMeta { Timestamp = 1 });
        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("x", FloatBytes(5f)) },
            new TestMeta { Timestamp = 2 });

        var results = new List<(WALEntry<TestMeta> Entry, float Result)>();
        foreach (var r in wal.Replay<float, FloatMathDef>(evaluator, new FramePtr(0)))
            results.Add(r);

        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Result, Is.EqualTo(3f).Within(1e-6f));
        Assert.That(results[1].Result, Is.EqualTo(6f).Within(1e-6f));
    }

    [Test]
    public void Replay_EmptyWAL_ReturnsNothing()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula);

        using var wal = new FluxWAL<TestMeta>(_testDir);

        var results = new List<(WALEntry<TestMeta> Entry, float Result)>();
        foreach (var r in wal.Replay<float, FloatMathDef>(evaluator, new FramePtr(0)))
            results.Add(r);

        Assert.That(results.Count, Is.EqualTo(0));
    }

    [Test]
    public void Replay_AfterCheckpointRestore()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula);

        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Checkpoint<float, FloatMathDef>(evaluator);
        var checkpointLsn = wal.CheckpointLsn;

        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("x", FloatBytes(2f)) },
            new TestMeta { Timestamp = 1 });

        var restored = wal.Restore<float, FloatMathDef>(Def);

        var results = new List<(WALEntry<TestMeta> Entry, float Result)>();
        foreach (var r in wal.Replay<float, FloatMathDef>(restored, checkpointLsn))
            results.Add(r);

        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0].Result, Is.EqualTo(3f).Within(1e-6f));
    }

    [Test]
    public void Rollback_NoCheckpoint_Throws()
    {
        using var wal = new FluxWAL<TestMeta>(_testDir);
        Assert.Throws<InvalidOperationException>(
            () => wal.Rollback<float, FloatMathDef>(Def, new FramePtr(0)));
    }

    [Test]
    public void Rollback_AfterCheckpoint_RestoresState()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula).Bind("x", 10f);

        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Checkpoint<float, FloatMathDef>(evaluator);

        var restored = wal.Rollback<float, FloatMathDef>(Def, wal.CurrentLsn);
        Assert.That(restored.Result, Is.EqualTo(11f).Within(1e-6f));
    }

    [Test]
    public void Rollback_AccumulatesEntries()
    {
        var assembler = new FluxAssembler<float, FloatMathDef>(Def);
        var lexer = CreateVarLexer("[", "]");
        var formula = assembler.Compile(lexer.Lex("[x] + [y] + [z]"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());
        var evaluator = assembler.Curry(formula).Bind("x", 1f);

        using var wal = new FluxWAL<TestMeta>(_testDir);
        wal.Checkpoint<float, FloatMathDef>(evaluator);

        // Entry 1 binds y → x=1, y=2, z unbound (partial)
        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("y", FloatBytes(2f)) }, new TestMeta());
        wal.CommitFrame();

        // Entry 2 binds z → x=1, y=2, z=3 → result 6
        wal.Append<float, FloatMathDef>(formula,
            new (string, byte[])[] { ("z", FloatBytes(3f)) }, new TestMeta());
        wal.CommitFrame();

        var restored = wal.Rollback<float, FloatMathDef>(Def, wal.CurrentLsn);
        Assert.That(restored.Result, Is.EqualTo(6f).Within(1e-6f));
    }
}

// ═══════════════════════════════════════════════════════
// FluxWAL — Recovery
// ═══════════════════════════════════════════════════════

public class FluxWALRecoveryTests
{
    private string _testDir;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"wal_rec_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Recover_FrameTableRestored()
    {
        using (var wal = new FluxWAL(_testDir))
        {
            wal.Append(new DualHash64(0xAAA, 0xBBB), Array.Empty<(string, byte[])>(), Array.Empty<byte>());
            wal.CommitFrame();
        }

        using var wal2 = new FluxWAL(_testDir);
        Assert.That(wal2.FrameCount, Is.EqualTo(2));
        Assert.That(wal2.GetFramePtr(0).Lsn, Is.EqualTo(0));
        Assert.That(wal2.GetFramePtr(1).Lsn, Is.GreaterThan(0));
    }

    [Test]
    public void Recover_CheckpointRestored()
    {
        byte[] checkpoint = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        using (var wal = new FluxWAL(_testDir))
        {
            wal.Checkpoint(checkpoint);
        }

        using var wal2 = new FluxWAL(_testDir);
        Assert.That(wal2.HasCheckpoint, Is.True);
        Assert.That(wal2.ReadCheckpoint(), Is.EqualTo(checkpoint));
    }

    [Test]
    public void Recover_EmptyFile_DefaultsRestored()
    {
        Directory.CreateDirectory(_testDir);
        File.WriteAllBytes(Path.Combine(_testDir, "flux.wal"), Array.Empty<byte>());
        using var wal = new FluxWAL(_testDir);
        Assert.That(wal.FrameCount, Is.EqualTo(1));
        Assert.That(wal.CurrentLsn.Lsn, Is.EqualTo(0));
    }
}
