using System;
using System.Threading;
using System.Threading.Tasks;
using FluxFormula.Core;
using NUnit.Framework;

public class NativeTruncateBufferTests
{
    private NativeTruncateBuffer<byte> _buffer;

    [SetUp]
    public void SetUp()
    {
        _buffer = new NativeTruncateBuffer<byte>((nuint)65536, 256);
    }

    [TearDown]
    public void TearDown()
    {
        _buffer.Dispose();
    }

    [Test]
    public void AppendTruncate_ReadFrom_BasicCycle()
    {
        _buffer.AppendTruncate(new byte[] { 1, 2, 3, 4 });
        Assert.That(_buffer.TruncationCount, Is.EqualTo(1u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(4));

        var entry = _buffer.ReadFrom(1);
        Assert.That(entry.Length, Is.EqualTo(4));
        Assert.That(entry[0], Is.EqualTo(1));
        Assert.That(entry[3], Is.EqualTo(4));
    }

    [Test]
    public void AppendTruncate_MultipleEntries()
    {
        _buffer.AppendTruncate(new byte[] { 1, 2 });
        _buffer.AppendTruncate(new byte[] { 3, 4 });
        _buffer.AppendTruncate(new byte[] { 5, 6 });

        Assert.That(_buffer.TruncationCount, Is.EqualTo(3u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(6));

        Assert.That(_buffer.ReadFrom(1).ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(_buffer.ReadFrom(2).ToArray(), Is.EqualTo(new byte[] { 3, 4 }));
        Assert.That(_buffer.ReadFrom(3).ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
    }

    [Test]
    public void AppendTruncate_AfterClear_Works()
    {
        _buffer.AppendTruncate(new byte[] { 1, 2, 3 });
        _buffer.Clear();
        Assert.That(_buffer.TruncationCount, Is.EqualTo(0u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));

        _buffer.AppendTruncate(new byte[] { 4, 5 });
        Assert.That(_buffer.TruncationCount, Is.EqualTo(1u));
        Assert.That(_buffer.ReadFrom(1).ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
    }

    [Test]
    public void Append_EmptySpan_NoOp()
    {
        _buffer.Append(ReadOnlySpan<byte>.Empty);
        Assert.That(_buffer.TruncationCount, Is.EqualTo(0u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));
    }

    [Test]
    public void AppendTruncate_EmptySpan_MarksTruncation()
    {
        _buffer.AppendTruncate(ReadOnlySpan<byte>.Empty);
        Assert.That(_buffer.TruncationCount, Is.EqualTo(1u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));
        Assert.That(_buffer.ReadFrom(1).Length, Is.EqualTo(0));
    }

    [Test]
    public void RevertTo_RemovesLaterEntries()
    {
        _buffer.AppendTruncate(new byte[] { 1 });
        _buffer.AppendTruncate(new byte[] { 2 });
        _buffer.AppendTruncate(new byte[] { 3 });

        _buffer.RevertTo(1);

        Assert.That(_buffer.TruncationCount, Is.EqualTo(1u));
        Assert.That(_buffer.ReadFrom(1).ToArray(), Is.EqualTo(new byte[] { 1 }));
    }

    [Test]
    public void RevertTo_Zero_ClearsAll()
    {
        _buffer.AppendTruncate(new byte[] { 1, 2 });
        _buffer.AppendTruncate(new byte[] { 3, 4 });

        _buffer.RevertTo(0);

        Assert.That(_buffer.TruncationCount, Is.EqualTo(0u));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));
    }

    [Test]
    public void RevertTo_ThenReappend_RestoresCorrectly()
    {
        _buffer.AppendTruncate(new byte[] { 1, 2 });
        _buffer.AppendTruncate(new byte[] { 3, 4 });

        _buffer.RevertTo(1);
        _buffer.AppendTruncate(new byte[] { 5, 6 });

        Assert.That(_buffer.TruncationCount, Is.EqualTo(2u));
        Assert.That(_buffer.ReadFrom(1).ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(_buffer.ReadFrom(2).ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(4)); // 1,2 + 5,6, 3,4 被覆盖
    }

    [Test]
    public void ReadFrom_IndexZero_Throws()
    {
        _buffer.AppendTruncate(new byte[] { 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.ReadFrom(0));
    }

    [Test]
    public void ReadFrom_IndexOutOfRange_Throws()
    {
        _buffer.AppendTruncate(new byte[] { 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.ReadFrom(2));
    }

    [Test]
    public void ReadFrom_EmptyBuffer_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.ReadFrom(1));
    }

    [Test]
    public void RevertTo_IndexOutOfRange_Throws()
    {
        _buffer.AppendTruncate(new byte[] { 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.RevertTo(2));
    }

    [Test]
    public void Dispose_PreventsAppend()
    {
        _buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _buffer.Append(new byte[] { 1 }));
    }

    [Test]
    public void Dispose_PreventsAppendTruncate()
    {
        _buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _buffer.AppendTruncate(new byte[] { 1 }));
    }

    [Test]
    public void Dispose_PreventsTruncate()
    {
        _buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _buffer.Truncate());
    }

    [Test]
    public void Dispose_DoubleDispose_NoThrow()
    {
        _buffer.Dispose();
        Assert.DoesNotThrow(() => _buffer.Dispose());
    }

    [Test]
    public void ByteLength_TracksAccurately()
    {
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));
        _buffer.AppendTruncate(new byte[10]);
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(10));
        _buffer.AppendTruncate(new byte[20]);
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(30));
        _buffer.Clear();
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(0));
    }

    [Test]
    public void MultiThreaded_StressTest()
    {
        const int threadCount = 8;
        const int dataSize = 1000;
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(() =>
            {
                var data = new byte[dataSize];
                Array.Fill(data, (byte)threadId);
                _buffer.AppendTruncate(data);
            });
        }
        Task.WaitAll(tasks);

        Assert.That(_buffer.TruncationCount, Is.EqualTo((uint)threadCount));
        Assert.That((int)_buffer.ByteLength, Is.EqualTo(threadCount * dataSize));

        for (uint i = 1; i <= _buffer.TruncationCount; i++)
        {
            var entry = _buffer.ReadFrom(i);
            Assert.That(entry.Length, Is.EqualTo(dataSize),
                $"Entry {i} has wrong length");
            byte expected = entry[0];
            for (int j = 1; j < entry.Length; j++)
                Assert.That(entry[j], Is.EqualTo(expected),
                    $"Entry {i} has interleaved data at offset {j}: expected {expected}, got {entry[j]}");
        }
    }
}
