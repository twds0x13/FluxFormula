using System;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SourceSerializer;

/// <summary>
/// SerializerBlocks 公共 API 测试：AddBlock/AddBlocks/RemoveBlock/Builder/内置类型。
/// 所有 fake-block 测试使用非内置类型（T1-T5），避免覆盖/移除 16 种内置 block。
/// SetUp/TearDown 确保测试间状态隔离。
/// </summary>
public class SerializerBlocksTests
{
    // 非内置测试类型
    private struct T1 { public int X; }
    private struct T2 { public int X; }
    private struct T3 { public int X; }
    private struct T4 { public int X; }
    private struct T5 { public int X; }

    private static readonly Type[] TestTypes = { typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(decimal) };

    [SetUp]
    public void SetUp()
    {
        foreach (var t in TestTypes)
            SerializerBlocks.RemoveBlock(t);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var t in TestTypes)
            SerializerBlocks.RemoveBlock(t);
    }

    // 测试用 fake block
    private sealed class FakeBlock<T> : ISerializerBlock<T>
    {
        public int Scan(ReadOnlySpan<char> text, int pos, out T value) { value = default!; return pos; }
        public void Emit(StringBuilder sb, T value) { }
    }

    // 往返测试 block
    private sealed class RoundtripBlock<T> : ISerializerBlock<T>
    {
        private readonly Func<string, T> _parse;
        public RoundtripBlock(Func<string, T> parse) => _parse = parse;
        public int Scan(ReadOnlySpan<char> text, int pos, out T value)
        {
            value = default!;
            if (pos >= text.Length) return pos;
            int end = pos;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '|') end++;
            if (end == pos) return pos;
            value = _parse(text.Slice(pos, end - pos).ToString());
            return end;
        }
        public void Emit(StringBuilder sb, T value) => sb.Append(value!.ToString());
    }

    // ═══════════════════════════════════════════════════════
    // 注册/查询
    // ═══════════════════════════════════════════════════════

    [Test]
    public void AddBlock_Registers_TryGetReturnsBlock()
    {
        var block = new FakeBlock<T1>();
        SerializerBlocks.AddBlock(block);
        Assert.That(SerializerBlocks.TryGet<T1>(out var result), Is.True);
        Assert.That(result, Is.SameAs(block));
        SerializerBlocks.RemoveBlock<T1>();
    }

    [Test]
    public void AddBlock_Overwrites_ExistingRegistration()
    {
        var first = new FakeBlock<T1>();
        var second = new FakeBlock<T1>();
        SerializerBlocks.AddBlock(first);
        SerializerBlocks.AddBlock(second);
        Assert.That(SerializerBlocks.TryGet<T1>(out var result), Is.True);
        Assert.That(result, Is.SameAs(second));
        Assert.That(result, Is.Not.SameAs(first));
        SerializerBlocks.RemoveBlock<T1>();
    }

    [Test]
    public void TryGet_NotRegistered_ReturnsFalse()
    {
        Assert.That(SerializerBlocks.TryGet<T1>(out var block), Is.False);
        Assert.That(block, Is.Null);
    }

    [Test]
    public void AddBlock_NullBlock_ThrowsArgumentNullException()
    {
        Assert.That(() => SerializerBlocks.AddBlock<T1>(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void AddBlocks_NullArray_ThrowsArgumentNullException()
    {
        Assert.That(() => SerializerBlocks.AddBlocks(null!),
            Throws.ArgumentNullException);
    }

    [Test]
    public void AddBlocks_NullElement_Skipped()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        SerializerBlocks.AddBlocks(blockA, null!, blockB);
        Assert.That(SerializerBlocks.TryGet<T1>(out var rA), Is.True);
        Assert.That(rA, Is.SameAs(blockA));
        Assert.That(SerializerBlocks.TryGet<T2>(out var rB), Is.True);
        Assert.That(rB, Is.SameAs(blockB));
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
    }

    // ═══════════════════════════════════════════════════════
    // 流式 API
    // ═══════════════════════════════════════════════════════

    [Test]
    public void AddBlock_FluentChaining_RegistersBoth()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        SerializerBlocks.AddBlock(blockA).AddBlock(blockB);
        Assert.That(SerializerBlocks.TryGet<T1>(out var rA), Is.True);
        Assert.That(rA, Is.SameAs(blockA));
        Assert.That(SerializerBlocks.TryGet<T2>(out var rB), Is.True);
        Assert.That(rB, Is.SameAs(blockB));
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
    }

    [Test]
    public void Builder_AddBlocks_BatchRegistration()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        SerializerBlocks.AddBlock(blockA).AddBlocks(blockB);
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
    }

    [Test]
    public void Builder_RemoveBlock_Fluent()
    {
        SerializerBlocks.AddBlock(new FakeBlock<T1>());
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        SerializerBlocks.AddBlock(new FakeBlock<T2>()).RemoveBlock<T1>();
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.False);
        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T2>();
    }

    [Test]
    public void Builder_FluentMix_AddRemove()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        SerializerBlocks
            .AddBlock(blockA)
            .RemoveBlock<T3>()              // 未注册的移除——静默成功
            .AddBlock(blockB);
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
    }

    // ═══════════════════════════════════════════════════════
    // 移除
    // ═══════════════════════════════════════════════════════

    [Test]
    public void RemoveBlock_Removes_TryGetReturnsFalse()
    {
        SerializerBlocks.AddBlock(new FakeBlock<T1>());
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T1>();
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.False);
    }

    [Test]
    public void RemoveBlock_NotRegistered_SilentNoOp()
    {
        Assert.That(() => SerializerBlocks.RemoveBlock<T1>(), Throws.Nothing);
    }

    // ═══════════════════════════════════════════════════════
    // 内置类型
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TryGet_BuiltinFloat_ReturnsBlock()
    {
        Assert.That(SerializerBlocks.TryGet<float>(out var block), Is.True);
        Assert.That(block, Is.Not.Null);
        Assert.That(block, Is.InstanceOf<ISerializerBlock<float>>());
    }

    [Test]
    public void TryGet_BuiltinInt_ReturnsBlock()
    {
        Assert.That(SerializerBlocks.TryGet<int>(out var block), Is.True);
        Assert.That(block, Is.Not.Null);
    }

    [Test]
    public void TryGet_BuiltinString_ReturnsBlock()
    {
        Assert.That(SerializerBlocks.TryGet<string>(out var block), Is.True);
        Assert.That(block, Is.Not.Null);
    }

    [Test]
    public void BuiltinTypes_All16_HaveBlock()
    {
        Assert.That(SerializerBlocks.TryGet<float>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<double>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<int>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<uint>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<long>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<ulong>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<short>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<ushort>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<byte>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<sbyte>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<bool>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<char>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<string>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<IntPtr>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<UIntPtr>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<Guid>(out _), Is.True);
    }

    // ═══════════════════════════════════════════════════════
    // 序列化往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Serialize_Deserialize_BuiltinFloat_Roundtrip()
    {
        float value = 3.14f;
        var serialized = SerializerBlocks.Serialize(value);
        var deserialized = SerializerBlocks.Deserialize<float>(serialized);
        Assert.That(deserialized, Is.EqualTo(value).Within(1e-5f));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinInt_Roundtrip()
    {
        var serialized = SerializerBlocks.Serialize(42);
        var deserialized = SerializerBlocks.Deserialize<int>(serialized);
        Assert.That(deserialized, Is.EqualTo(42));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinString_Roundtrip()
    {
        // 带空格的字符串：始终加引号
        var serialized = SerializerBlocks.Serialize("hello world");
        Assert.That(serialized, Is.EqualTo("\"hello world\""));
        var deserialized = SerializerBlocks.Deserialize<string>("\"hello world\"");
        Assert.That(deserialized, Is.EqualTo("hello world"));
    }

    [Test]
    public void Serialize_Deserialize_PlainString_AlwaysQuoted()
    {
        // 无空格字符串现在也始终加引号，消除与 float 等类型的歧义
        var serialized = SerializerBlocks.Serialize("hello");
        Assert.That(serialized, Is.EqualTo("\"hello\""));
        var deserialized = SerializerBlocks.Deserialize<string>("\"hello\"");
        Assert.That(deserialized, Is.EqualTo("hello"));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinDouble_Roundtrip()
    {
        double value = 3.14159265358979;
        var serialized = SerializerBlocks.Serialize(value);
        var deserialized = SerializerBlocks.Deserialize<double>(serialized);
        Assert.That(deserialized, Is.EqualTo(value).Within(1e-12));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinBool_Roundtrip()
    {
        Assert.That(SerializerBlocks.Deserialize<bool>(SerializerBlocks.Serialize(true)), Is.True);
        Assert.That(SerializerBlocks.Deserialize<bool>(SerializerBlocks.Serialize(false)), Is.False);
    }

    [Test]
    public void Serialize_Deserialize_BuiltinLong_Roundtrip()
    {
        long value = long.MaxValue - 1;
        var deserialized = SerializerBlocks.Deserialize<long>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinUlong_Roundtrip()
    {
        ulong value = ulong.MaxValue - 1;
        var deserialized = SerializerBlocks.Deserialize<ulong>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinShort_Roundtrip()
    {
        short value = -12345;
        var deserialized = SerializerBlocks.Deserialize<short>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinUshort_Roundtrip()
    {
        ushort value = 65000;
        var deserialized = SerializerBlocks.Deserialize<ushort>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinByte_Roundtrip()
    {
        byte value = 200;
        var deserialized = SerializerBlocks.Deserialize<byte>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinSbyte_Roundtrip()
    {
        sbyte value = -100;
        var deserialized = SerializerBlocks.Deserialize<sbyte>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinUint_Roundtrip()
    {
        uint value = 4000000000;
        var deserialized = SerializerBlocks.Deserialize<uint>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    [Test]
    public void Serialize_Deserialize_BuiltinChar_Roundtrip()
    {
        char value = 'Z';
        var deserialized = SerializerBlocks.Deserialize<char>(SerializerBlocks.Serialize(value));
        Assert.That(deserialized, Is.EqualTo(value));
    }

    // ═══════════════════════════════════════════════════════
    // 空白处理（TryScan / Deserialize 内部经 WhitespaceStripper）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Deserialize_HandlesWhitespaceInInput()
    {
        var deserialized = SerializerBlocks.Deserialize<float>("  3.14  ");
        Assert.That(deserialized, Is.EqualTo(3.14f).Within(1e-5f));
    }

    [Test]
    public void TryScan_HandlesWhitespaceInInput()
    {
        Assert.That(SerializerBlocks.TryScan<float>("  42  ", out var value), Is.True);
        Assert.That(value, Is.EqualTo(42f).Within(1e-5f));
    }

    [Test]
    public void Deserialize_PreservesWhitespaceInsideQuotedStrings()
    {
        var deserialized = SerializerBlocks.Deserialize<string>("\"hello world\"");
        Assert.That(deserialized, Is.EqualTo("hello world"));
    }

    [Test]
    public void Serialize_AfterManualAddBlock_Roundtrip()
    {
        var block = new RoundtripBlock<decimal>(decimal.Parse);
        SerializerBlocks.AddBlock(block);
        decimal value = 123.456m;
        var serialized = SerializerBlocks.Serialize(value);
        var deserialized = SerializerBlocks.Deserialize<decimal>(serialized);
        Assert.That(deserialized, Is.EqualTo(value));
        SerializerBlocks.RemoveBlock<decimal>();
    }

    [Test]
    public void Serialize_UnregisteredType_ThrowsInvalidOperationException()
    {
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.False);
        Assert.That(() => SerializerBlocks.Serialize(new T1()),
            Throws.InvalidOperationException.With.Message.Contains("SerializerBlock"));
    }

    // ═══════════════════════════════════════════════════════
    // EnsureInitialized 懒初始化
    // ═══════════════════════════════════════════════════════

    [Test]
    public void EnsureInitialized_BuiltinsAvailable_WithoutManualRegistration()
    {
        // 16 种内置类型应在首次 TryGet 时自动注册，无需手动 AddBlock
        Assert.That(SerializerBlocks.TryGet<float>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<int>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<string>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<double>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<bool>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<long>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<ulong>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<short>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<ushort>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<byte>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<sbyte>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<uint>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<char>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<IntPtr>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<UIntPtr>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<Guid>(out _), Is.True);
    }

    [Test]
    public void EnsureInitialized_BuiltinsPersist_AfterMultipleAccess()
    {
        // 多次 TryGet 不应改变注册状态
        Assert.That(SerializerBlocks.TryGet<float>(out var b1), Is.True);
        Assert.That(SerializerBlocks.TryGet<float>(out var b2), Is.True);
        Assert.That(b2, Is.SameAs(b1), "Same builtin block instance should be returned");
    }

    [Test]
    public void EnsureInitialized_ConcurrentInit_NoCorruption()
    {
        // 多线程首次访问内置类型——即使竞态进入 EnsureInitialized，不应抛异常或损坏状态
        Assert.That(() =>
        {
            Parallel.For(0, 50, i =>
            {
                int idx = i % 16;
                bool ok = idx switch
                {
                    0 => SerializerBlocks.TryGet<float>(out _),
                    1 => SerializerBlocks.TryGet<int>(out _),
                    2 => SerializerBlocks.TryGet<string>(out _),
                    3 => SerializerBlocks.TryGet<double>(out _),
                    4 => SerializerBlocks.TryGet<bool>(out _),
                    5 => SerializerBlocks.TryGet<long>(out _),
                    6 => SerializerBlocks.TryGet<ulong>(out _),
                    7 => SerializerBlocks.TryGet<short>(out _),
                    8 => SerializerBlocks.TryGet<ushort>(out _),
                    9 => SerializerBlocks.TryGet<byte>(out _),
                    10 => SerializerBlocks.TryGet<sbyte>(out _),
                    11 => SerializerBlocks.TryGet<uint>(out _),
                    12 => SerializerBlocks.TryGet<char>(out _),
                    13 => SerializerBlocks.TryGet<IntPtr>(out _),
                    14 => SerializerBlocks.TryGet<UIntPtr>(out _),
                    _ => SerializerBlocks.TryGet<Guid>(out _),
                };
                Assert.That(ok, Is.True);
            });
        }, Throws.Nothing);
    }

    // ═══════════════════════════════════════════════════════
    // Builder 流式 API 组合
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Builder_AddThreeBlocks_AllRegistered()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        var blockC = new FakeBlock<T3>();
        SerializerBlocks
            .AddBlock(blockA)
            .AddBlock(blockB)
            .AddBlock(blockC);
        Assert.That(SerializerBlocks.TryGet<T1>(out var rA), Is.True);
        Assert.That(rA, Is.SameAs(blockA));
        Assert.That(SerializerBlocks.TryGet<T2>(out var rB), Is.True);
        Assert.That(rB, Is.SameAs(blockB));
        Assert.That(SerializerBlocks.TryGet<T3>(out var rC), Is.True);
        Assert.That(rC, Is.SameAs(blockC));
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
        SerializerBlocks.RemoveBlock<T3>();
    }

    [Test]
    public void Builder_AddThenOverwrite_LastWins()
    {
        var first = new FakeBlock<T1>();
        var second = new FakeBlock<T1>();
        SerializerBlocks.AddBlock(first).AddBlock(second);
        Assert.That(SerializerBlocks.TryGet<T1>(out var r), Is.True);
        Assert.That(r, Is.SameAs(second));
        SerializerBlocks.RemoveBlock<T1>();
    }

    [Test]
    public void Builder_AddBlocks_MixedTypes_AllRegistered()
    {
        var blockA = new FakeBlock<T1>();
        var blockB = new FakeBlock<T2>();
        var blockC = new FakeBlock<T3>();
        SerializerBlocks.AddBlock(blockA).AddBlocks(blockB, blockC);
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<T3>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T1>();
        SerializerBlocks.RemoveBlock<T2>();
        SerializerBlocks.RemoveBlock<T3>();
    }

    [Test]
    public void Builder_AddRemoveAdd_SameType()
    {
        var first = new FakeBlock<T1>();
        var second = new FakeBlock<T1>();
        SerializerBlocks.AddBlock(first).RemoveBlock<T1>().AddBlock(second);
        Assert.That(SerializerBlocks.TryGet<T1>(out var r), Is.True);
        Assert.That(r, Is.SameAs(second));
        SerializerBlocks.RemoveBlock<T1>();
    }

    [Test]
    public void Builder_RemoveWhenEmpty_Silent()
    {
        Assert.That(() => SerializerBlocks.AddBlock(new FakeBlock<T1>()).RemoveBlock<T2>(), Throws.Nothing);
        Assert.That(SerializerBlocks.TryGet<T1>(out _), Is.True);
        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.False);
        SerializerBlocks.RemoveBlock<T1>();
    }

    // ═══════════════════════════════════════════════════════
    // 并发安全
    // ═══════════════════════════════════════════════════════

    [Test]
    public void AddBlock_Concurrent_DifferentTypes_NoDataLoss()
    {
        const int typeCount = 20;
        var ran = new bool[typeCount];

        Assert.That(() =>
        {
            Parallel.For(0, typeCount, i =>
            {
                // 每个线程使用相同类型——测试并发写入同一 key 无异常
                SerializerBlocks.AddBlock(new FakeBlock<T1>());
                ran[i] = true;
            });
        }, Throws.Nothing);

        Assert.That(ran, Has.All.True);
    }

    [Test]
    public void AddBlock_Concurrent_SameType_NoThrow()
    {
        const int iterations = 100;
        Assert.That(() =>
        {
            Parallel.For(0, iterations, _ =>
            {
                SerializerBlocks.AddBlock(new FakeBlock<T2>());
            });
        }, Throws.Nothing);

        Assert.That(SerializerBlocks.TryGet<T2>(out _), Is.True);
        SerializerBlocks.RemoveBlock<T2>();
    }

    [Test]
    public void AddBlock_ConcurrentReadWrite_NoCorruption()
    {
        SerializerBlocks.AddBlock(new FakeBlock<T3>());

        Assert.That(() =>
        {
            Parallel.For(0, 50, i =>
            {
                if (i % 2 == 0)
                    SerializerBlocks.AddBlock(new FakeBlock<T3>());
                else
                    SerializerBlocks.TryGet<T3>(out _);
            });
        }, Throws.Nothing);

        SerializerBlocks.RemoveBlock<T3>();
    }
}
