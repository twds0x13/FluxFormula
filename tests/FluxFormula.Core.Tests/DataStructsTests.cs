using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// 数据类型的构造、字段存取、ToString 测试。
/// Instruction / OpPair / VariableSlot
/// </summary>
public unsafe class DataStructsTests
{
    // ═══════════════════════════════════════════════════════
    // Instruction
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Instruction_SizeIs8()
    {
        Assert.That(sizeof(Instruction), Is.EqualTo(8));
    }

    [Test]
    public void Instruction_RawOverlapsOpCode()
    {
        var inst = new Instruction { OpCode = 42 };
        Assert.That((byte)inst.Raw, Is.EqualTo(42));
    }

    [Test]
    public void Instruction_RawRoundTrip()
    {
        var inst = new Instruction { Raw = 0x0102030405060708L };
        Assert.That(inst.OpCode, Is.EqualTo(8));
        Assert.That(inst.Dest, Is.EqualTo(7));
        Assert.That(inst.Arg0, Is.EqualTo(6));
        Assert.That(inst.Arg1, Is.EqualTo(5));
        Assert.That(inst.Arg2, Is.EqualTo(4));
        Assert.That(inst.Arg3, Is.EqualTo(3));
        Assert.That(inst.Arg4, Is.EqualTo(2));
        Assert.That(inst.Arg5, Is.EqualTo(1));
    }

    [Test]
    public void Instruction_AllFields()
    {
        var inst = new Instruction { OpCode = 1, Dest = 2, Arg0 = 3, Arg1 = 4, Arg2 = 5, Arg3 = 6, Arg4 = 7, Arg5 = 8 };
        Assert.That(inst.OpCode, Is.EqualTo(1));
        Assert.That(inst.Dest, Is.EqualTo(2));
        Assert.That(inst.Arg0, Is.EqualTo(3));
        Assert.That(inst.Arg1, Is.EqualTo(4));
        Assert.That(inst.Arg2, Is.EqualTo(5));
        Assert.That(inst.Arg3, Is.EqualTo(6));
        Assert.That(inst.Arg4, Is.EqualTo(7));
        Assert.That(inst.Arg5, Is.EqualTo(8));
    }

    [Test]
    public void Instruction_ToString_ContainsOpCode()
    {
        var inst = new Instruction { OpCode = 7 };
        Assert.That(inst.ToString(), Does.Contain("7"));
    }

    // ═══════════════════════════════════════════════════════
    // OpPair
    // ═══════════════════════════════════════════════════════

    [Test]
    public void OpPair_None_ToString()
    {
        var p = new OpPair<FloatOp> { PairRole = Pair.None };
        Assert.That(p.ToString(), Does.Contain("None"));
    }

    [Test]
    public void OpPair_Left_ToString()
    {
        var p = new OpPair<FloatOp>
        {
            PairRole = Pair.Left,
            TargetLeft = FloatOp.LParen,
            EmitOnMatch = true,
            EmitOpCode = FloatOp.Mul,
        };
        Assert.That(p.ToString(), Does.Contain("Left"));
        Assert.That(p.ToString(), Does.Contain("LParen"));
    }

    [Test]
    public void OpPair_Right_ToString()
    {
        var p = new OpPair<FloatOp>
        {
            PairRole = Pair.Right,
            TargetLeft = FloatOp.LParen,
            EmitOnMatch = false,
        };
        Assert.That(p.ToString(), Does.Contain("Right"));
    }

    // ═══════════════════════════════════════════════════════
    // VariableSlot
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VariableSlot_Constructed_FieldsMatch()
    {
        var slot = new VariableSlot("damage", 3);
        Assert.That(slot.Name, Is.EqualTo("damage"));
        Assert.That(slot.SlotIndex, Is.EqualTo(3));
    }

    [Test]
    public void VariableSlot_EmptyName()
    {
        var slot = new VariableSlot("", 0);
        Assert.That(slot.Name, Is.EqualTo(""));
        Assert.That(slot.SlotIndex, Is.EqualTo(0));
    }

    [Test]
    public void VariableSlot_ToString_ContainsName()
    {
        var slot = new VariableSlot("health", 5);
        Assert.That(slot.ToString(), Does.Contain("health"));
    }
}
