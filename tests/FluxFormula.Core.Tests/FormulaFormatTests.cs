using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FormulaFormat 格式读写方法单元测试。
/// 目标：覆盖 IsFormula、WriteHeader、ReadVariableSlots(baseSlotOffset) 等未测路径。
/// </summary>
public unsafe class FormulaFormatTests
{
    // ═══════════════════════════════════════════════════════
    // IsFormula
    // ═══════════════════════════════════════════════════════

    [Test]
    public void IsFormula_Valid_ReturnsTrue()
    {
        // 用 FluxAssembler 编译一个简单公式，取字节码
        var lexer = TestHelper.CreateMathLexer();
        var tokens = lexer.Lex("3.14 + 2.718");
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(tokens);
        byte[] bytes = formula.ToBytes();

        Assert.That(FormulaFormat.IsFormula(bytes), Is.True);
    }

    [Test]
    public void IsFormula_TooShort_ReturnsFalse()
    {
        var tooShort = new byte[10]; // < HeaderSize (14)
        Assert.That(FormulaFormat.IsFormula(tooShort), Is.False);
    }

    [Test]
    public void IsFormula_VffMagic_ReturnsFalse()
    {
        // 构造以 "VFF\0" 开头的字节——应被 IsFormula 排除
        byte[] vffMagic = new byte[20];
        vffMagic[0] = (byte)'V';
        vffMagic[1] = (byte)'F';
        vffMagic[2] = (byte)'F';
        vffMagic[3] = 0;

        Assert.That(FormulaFormat.IsFormula(vffMagic), Is.False,
            "\"VFF\\0\" magic 应被 IsFormula 排除");
    }

    [Test]
    public void IsFormula_ZeroCount_ReturnsFalse()
    {
        // 头部 count=0 的字节码不应被视为有效公式
        byte[] zeroCount = new byte[FormulaFormat.HeaderSize + 8]; // +8 保证不触发"过短"
        int off = 0;
        BinaryFormat.WriteInt32LE(zeroCount, ref off, 0); // count = 0
        off = 4;
        zeroCount[off++] = 0; // Type
        off = 5;
        BinaryFormat.WriteInt32LE(zeroCount, ref off, 1); // ImmCount
        off = 9;
        BinaryFormat.WriteInt32LE(zeroCount, ref off, 0); // VarSlotCount
        zeroCount[off++] = 0; // MaxRegister

        Assert.That(FormulaFormat.IsFormula(zeroCount), Is.False,
            "count=0 不应被视为有效公式");
    }

    [Test]
    public void IsFormula_InstructionOverflow_ReturnsFalse()
    {
        // 构造一个 byte[]，头部声明 count 很大但字节不够
        byte[] truncated = new byte[FormulaFormat.HeaderSize + 8]; // 仅 1 条指令的空间
        int off = 0;
        BinaryFormat.WriteInt32LE(truncated, ref off, 100); // 声称 100 条指令
        off = 4;
        truncated[off++] = (byte)FluxType.Formula;
        off = 5;
        BinaryFormat.WriteInt32LE(truncated, ref off, 0);
        off = 9;
        BinaryFormat.WriteInt32LE(truncated, ref off, 0);
        truncated[off++] = 0;

        // 100 条指令远超 8B 的 buffer
        Assert.That(FormulaFormat.IsFormula(truncated), Is.False,
            "指令条数超出 buffer 长度应返回 false");
    }

    [Test]
    public void IsFormula_ExactFit_ReturnsTrue()
    {
        // 精确匹配：Count + 指令段 = 字节长
        var lexer = TestHelper.CreateMathLexer();
        var tokens = lexer.Lex("42");
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(tokens);
        byte[] bytes = formula.ToBytes();

        Assert.That(FormulaFormat.IsFormula(bytes), Is.True,
            "精确匹配的字节码应被识别为公式");
    }

    // ═══════════════════════════════════════════════════════
    // WriteHeader → ReadHeader 往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void WriteHeader_ReadHeader_RoundTrip()
    {
        var header = new FormulaHeader(
            count: 5,
            type: FluxType.Modifier,
            immediateCount: 2,
            varSlotCount: 3,
            maxRegister: 10
        );

        byte[] buf = new byte[FormulaFormat.HeaderSize];
        int off = 0;
        FormulaFormat.WriteHeader(buf, ref off, header);
        Assert.That(off, Is.EqualTo(FormulaFormat.HeaderSize),
            "写入后 offset 应前进 HeaderSize 字节");

        var readBack = FormulaFormat.ReadHeader(buf);
        Assert.That(readBack.Count, Is.EqualTo(5));
        Assert.That(readBack.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(readBack.ImmediateCount, Is.EqualTo(2));
        Assert.That(readBack.VarSlotCount, Is.EqualTo(3));
        Assert.That(readBack.MaxRegister, Is.EqualTo(10));
    }

    [Test]
    public void WriteHeader_ReadHeader_FormulaType()
    {
        var header = new FormulaHeader(3, FluxType.Formula, 1, 0, 5);
        byte[] buf = new byte[FormulaFormat.HeaderSize];
        int off = 0;
        FormulaFormat.WriteHeader(buf, ref off, header);

        var readBack = FormulaFormat.ReadHeader(buf);
        Assert.That(readBack.Type, Is.EqualTo(FluxType.Formula));
        Assert.That(readBack.MaxRegister, Is.EqualTo(5));
    }

    [Test]
    public void WriteHeader_ReadHeader_MaxRegisterZero()
    {
        // maxRegister = 0 表示"未分析"——向下兼容旧格式
        var header = new FormulaHeader(1, FluxType.Formula, 0, 0, 0);
        byte[] buf = new byte[FormulaFormat.HeaderSize];
        int off = 0;
        FormulaFormat.WriteHeader(buf, ref off, header);

        var readBack = FormulaFormat.ReadHeader(buf);
        Assert.That(readBack.MaxRegister, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // WriteHeader + 指令段 + ReadVariableSlots 完整往返
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToBytes_FromBytes_PreservesHeader()
    {
        // 端到端：编译 → 序列化 → 反序列化，验证头部字段保留
        var lexer = TestHelper.CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(lexer.Lex("1.5 + 2.5 * 3"));
        byte[] bytes = formula.ToBytes();

        var header = FormulaFormat.ReadHeader(bytes);

        Assert.That(header.Count, Is.GreaterThan(0));
        Assert.That(header.Type, Is.EqualTo(FluxType.Formula));
        Assert.That(header.MaxRegister, Is.GreaterThanOrEqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // ReadVariableSlots (baseSlotOffset)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ReadVariableSlots_ZeroCount_ReturnsEmpty()
    {
        var lexer = TestHelper.CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(lexer.Lex("42"));
        byte[] bytes = formula.ToBytes();

        var slots = FormulaFormat.ReadVariableSlots(bytes);
        Assert.That(slots, Is.Empty,
            "无变量的公式不应有变量槽");
    }

    [Test]
    public void ReadVariableSlots_BaseSlotOffset_ShiftsIndices()
    {
        // 编译带变量的公式，验证 baseSlotOffset 正确偏移 SlotIndex
        var lexer = TestHelper.CreateVarLexer("[", "]");
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(lexer.Lex("[x] + [y]"));
        byte[] bytes = formula.ToBytes();

        var slots0 = FormulaFormat.ReadVariableSlots(bytes, baseSlotOffset: 0);
        var slots5 = FormulaFormat.ReadVariableSlots(bytes, baseSlotOffset: 5);

        Assert.That(slots0.Length, Is.EqualTo(2));
        Assert.That(slots5.Length, Is.EqualTo(2));

        // 带 offset 的 SlotIndex 应 = 原始 + 5
        Assert.That(slots5[0].SlotIndex, Is.EqualTo(slots0[0].SlotIndex + 5));
        Assert.That(slots5[1].SlotIndex, Is.EqualTo(slots0[1].SlotIndex + 5));
    }

    [Test]
    public void ReadVariableSlots_NamesMatch()
    {
        var lexer = TestHelper.CreateVarLexer("{", "}");
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(lexer.Lex("{damage} * {multiplier}"));
        byte[] bytes = formula.ToBytes();

        var slots = FormulaFormat.ReadVariableSlots(bytes);

        Assert.That(slots.Length, Is.EqualTo(2));
        Assert.That(slots[0].Name, Is.EqualTo("damage"));
        Assert.That(slots[1].Name, Is.EqualTo("multiplier"));
    }

    // ═══════════════════════════════════════════════════════
    // DataSlots 计算
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DataSlots_Float_IsPositive()
    {
        int slots = FormulaFormat.DataSlots<float>();
        Assert.That(slots, Is.GreaterThan(0));
    }

    [Test]
    public void DataSlots_LargerType_RequiresMoreSlots()
    {
        int floatSlots = FormulaFormat.DataSlots<float>();
        int doubleSlots = FormulaFormat.DataSlots<double>();
        Assert.That(doubleSlots, Is.GreaterThanOrEqualTo(floatSlots),
            $"sizeof(double)={sizeof(double)}, sizeof(float)={sizeof(float)}");
    }

    // ═══════════════════════════════════════════════════════
    // ReadHeader 边界
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ReadHeader_TooShort_Throws()
    {
        var tooShort = new byte[10];
        Assert.That(
            () => FormulaFormat.ReadHeader(tooShort),
            Throws.ArgumentException.With.Message.Contains("too short"));
    }

    // ═══════════════════════════════════════════════════════
    // GetInstructionSpan
    // ═══════════════════════════════════════════════════════

    [Test]
    public void GetInstructionSpan_Valid_ReturnsNonEmpty()
    {
        var lexer = TestHelper.CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(TestHelper.Def)
            .Compile(lexer.Lex("1 + 2 * 3 - 4 / 5"));
        byte[] bytes = formula.ToBytes();

        var span = FormulaFormat.GetInstructionSpan(bytes);

        Assert.That(span.Length, Is.GreaterThan(0),
            "有效公式应返回非空指令跨度");
    }

    [Test]
    public void GetInstructionSpan_Overflow_Throws()
    {
        // 构造头部声明 count=1000 但 buffer 只有 HeaderSize+8 字节
        byte[] corrupted = new byte[FormulaFormat.HeaderSize + 8];
        int off = 0;
        BinaryFormat.WriteInt32LE(corrupted, ref off, 1000);
        off = 4;
        corrupted[off++] = (byte)FluxType.Formula;
        off = 5;
        BinaryFormat.WriteInt32LE(corrupted, ref off, 0);
        off = 9;
        BinaryFormat.WriteInt32LE(corrupted, ref off, 0);
        corrupted[off++] = 0;

        Assert.That(
            () => FormulaFormat.GetInstructionSpan(corrupted),
            Throws.ArgumentException.With.Message.Contains("exceeds"));
    }
}
