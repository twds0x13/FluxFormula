using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FluxInjector 构造器、值注入、值检索测试。
/// </summary>
public unsafe class FluxInjectorTests
{
    [Test]
    public void Constructor_FromInstructionArray()
    {
        var payload = new Instruction[2];
        payload[0] = new Instruction { OpCode = 1, Dest = 2 };
        payload[1] = new Instruction { OpCode = 3, Dest = 4 };

        var injector = new FluxInjector<float>(payload, null, System.Array.Empty<VariableSlot>());
        Assert.That(injector.ToString(), Does.Contain("FluxInjector"));
    }

    [Test]
    public void SetIndex_ValidIndex()
    {
        var lexer = CreateVarLexer("[", "]");
        var f = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("[x] + [y]"));
        var inst = new FluxAssembler<float, FloatMathDef>(Def)
            .Instantiate(f).Set("x", 99f).Set("y", 1f);
        Assert.That(inst.Run(), Is.EqualTo(100f).Within(1e-6f));
    }

    [Test]
    public void GetValue_ReturnsInjectedValue()
    {
        var lexer = CreateVarLexer("[", "]");
        var f = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("[a] + [b] + [c]"));
        var inst = new FluxAssembler<float, FloatMathDef>(Def)
            .Instantiate(f).Set("a", 1f).Set("b", 2f).Set("c", 3f);
        Assert.That(inst.Run(), Is.EqualTo(6f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 边界：无 offsets 构造器 + SetIndex OOB + GetValue default
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Constructor_BufferOnly_NoOffsets()
    {
        // 内部构造器 FluxInjector(Instruction[]) — 仅 buffer，无 offsets/varNames/values
        var payload = new Instruction[4];
        var injector = new FluxInjector<float>(payload);

        Assert.That(injector.ToString(), Does.Contain("FluxInjector"));
        // GetValue 在 _values == null 时返回 default
        Assert.That(injector.GetValue(0), Is.EqualTo(0f));
    }

    [Test]
    public void GetValue_OutOfRange_ReturnsDefault()
    {
        // 通过含 varSlots 的构造器创建 injector，_values 长度为 maxSlot+1
        var slots = new VariableSlot[] { new("x", 0) };
        var payload = new Instruction[8];
        var injector = new FluxInjector<float>(payload, new int[] { 0 }, slots);

        // slotIndex 超出 _values 范围 → 返回 default
        Assert.That(injector.GetValue(999), Is.EqualTo(0f));
    }

    [Test]
    public void SetIndex_OutOfBounds_Throws()
    {
        // 仅 buffer 构造器（_offsets == null）
        var payload = new Instruction[4]; // 不够存一个 float (需要 slotsPerData 个 Instruction)
        var injector = new FluxInjector<float>(payload);

        // paramIndex 极大 → offset + _slotsPerData > _buffer.Length
        var ex = Assert.Throws<System.IndexOutOfRangeException>(
            () => injector.SetIndex(999, 1.0f));
        Assert.That(ex.Message, Does.Contain("out of bounds"));
    }
}
