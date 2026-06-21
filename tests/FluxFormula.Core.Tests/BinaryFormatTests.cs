using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// BinaryFormat 小端序 I/O 方法测试（Read/Write 非核心路径）。
/// </summary>
public class BinaryFormatTests
{
    [Test]
    public void ReadUInt16LE_RoundTrip()
    {
        byte[] buf = new byte[4];
        int off = 1;
        buf[off] = 0x34;
        buf[off + 1] = 0x12;

        int readOff = 1;
        ushort val = BinaryFormat.ReadUInt16LE(buf, ref readOff);

        Assert.That(val, Is.EqualTo(0x1234));
        Assert.That(readOff, Is.EqualTo(3));
    }
}
