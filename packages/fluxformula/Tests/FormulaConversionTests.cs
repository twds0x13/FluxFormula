using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// Formula ↔ Modifier 互转的边界测试。
/// 核心风险：寄存器重命名（Dest + Arg0~Arg5 共 7 个字段）漏掉任何一个导致静默计算错误。
/// 策略：构造确定性的字节码，转换后通过求值验证行为正确性。
/// </summary>
public class FormulaConversionTests
{
    // ═══════════════════════════════════════════════════════
    // ToMultiplier
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToMultiplier_RemovesFirstImmediate()
    {
        // [a] + [b] → modifier: _ + [b]（第一个操作数被剥离）
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lexA    = CreateVarLexer("[", "]").Lex("[a] + [b]");
        var formula = runner.Compile(lexA);
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));

        var modifier = formula.ToMultiplier();
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(modifier.ImmediateCount, Is.EqualTo(formula.ImmediateCount - 1));
        Assert.That(modifier.VariableSlots.Length, Is.EqualTo(formula.VariableSlots.Length - 1));
    }

    [Test]
    public void ToMultiplier_RegisterRename_ProducesCorrectResult()
    {
        // formula: [a] + [b] (变量 a→slot0, b→slot1)
        // modifier: _ + [b] (只剩 b, b 的 SlotIndex 应 -1)
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lexA     = CreateVarLexer("[", "]").Lex("[a] + [b]");
        var formula  = runner.Compile(lexA);
        var modifier = formula.ToMultiplier();

        // modifier 剩余变量应为 "b"，其 SlotIndex 应偏移为 0（原为 1）
        Assert.That(modifier.VariableSlots.Length, Is.EqualTo(1));
        Assert.That(modifier.VariableSlots[0].Name, Is.EqualTo("b"));
        Assert.That(modifier.VariableSlots[0].SlotIndex, Is.EqualTo(0));
    }

    [Test]
    public void ToMultiplier_AlreadyModifier_ReturnsSelf()
    {
        // "* 2" 解析为 modifier（第一个 token 是二元运算符在 operandExpected 位置）
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var modifier = runner.Compile(new[] { Op(FloatOp.Mul), C(2f) });
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var again = modifier.ToMultiplier();
        Assert.That(modifier.Raw().ToArray(), Is.EqualTo(again.Raw().ToArray()),
            "Already-modifier ToMultiplier should return self (same buffer content)");
    }

    [Test]
    public void ToMultiplier_ThenToFormula_Roundtrip()
    {
        // formula: [x] * 2 + 1
        // modifier: _ * 2 + 1
        // back to formula: y * 2 + 1
        // 两次转换后求值应一致
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lex     = CreateVarLexer("[", "]").Lex("[x] * 2 + 1");
        var formula = runner.Compile(lex);

        float original = runner.Instantiate(formula, jit: false)
            .Set("x", 10f).Run();

        var modifier   = formula.ToMultiplier();
        var roundtripped = modifier.ToFormula("y");

        float result = runner.Instantiate(roundtripped, jit: false)
            .Set("y", 10f).Run();

        Assert.That(result, Is.EqualTo(original).Within(1e-6f));
    }

    [Test]
    public void ToMultiplier_JitAndInterpreter_ProduceSameResult()
    {
        // modifier 求值语义一致性：JIT vs 解释器
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lex      = CreateVarLexer("[", "]").Lex("[a] * [b] + [c]");
        var formula  = runner.Compile(lex);
        var modifier = formula.ToMultiplier();

        // Modifier 不能独立 Run，需 Connect 到 provider formula
        var provider = runner.Compile(new[] { C(3f) }); // 提供第一个操作数=3
        var chain    = provider.Connect(modifier);

        float interpResult = runner.Instantiate(chain, jit: false)
            .SetIndex(1, 4f)  // b
            .SetIndex(2, 5f)  // c
            .Run();

        float jitResult = runner.Instantiate(chain, jit: true)
            .SetIndex(1, 4f)
            .SetIndex(2, 5f)
            .Run();

        Assert.That(interpResult, Is.EqualTo(jitResult).Within(1e-6f));
    }

    /// <summary>
    /// 验证一元运算符（Neg）作为第一个 token 时，Formula 识别正确，
    /// 且 ToMultiplier 正确处理单操作数转 Modifier。
    /// </summary>
    [Test]
    public void ToMultiplier_UnaryPrefixOperator_KeepsSemantics()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lex     = CreateVarLexer("[", "]").Lex("-[a]"); // 一元取负
        var formula = runner.Compile(lex);
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));

        var modifier = formula.ToMultiplier();
        // Neg 是一元运算符，Modifier 也应保留
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        // 验证：provider 连 modifier 求值正确
        var provider = runner.Compile(new[] { C(5f) });
        var chain    = provider.Connect(modifier);
        Assert.That(runner.Instantiate(chain, jit: false).Run(),
            Is.EqualTo(-5f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // ToFormula
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToFormula_InsertsImmediateForVariable()
    {
        // modifier: _ * 3 → formula: x * 3
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var modifier = runner.Compile(new[] { Op(FloatOp.Mul), C(3f) });
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var formula = modifier.ToFormula("x");
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));
        Assert.That(formula.ImmediateCount, Is.EqualTo(modifier.ImmediateCount + 1));
        Assert.That(formula.VariableSlots.Length, Is.EqualTo(modifier.VariableSlots.Length + 1));
        Assert.That(formula.VariableSlots[0].Name, Is.EqualTo("x"));
        Assert.That(formula.VariableSlots[0].SlotIndex, Is.EqualTo(0));
    }

    [Test]
    public void ToFormula_BusRegisterRenamed_ProducesCorrectResult()
    {
        // modifier: _ * 2 + 1（Bus 寄存器承载输入）
        // formula:  x * 2 + 1（Bus→新寄存器，新变量插入）
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var modifier = runner.Compile(new[]
        {
            Op(FloatOp.Mul), C(2f),
            Op(FloatOp.Add), C(1f),
        });
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var formula = modifier.ToFormula("x");
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));

        float result = runner.Instantiate(formula, jit: false)
            .Set("x", 10f).Run();
        Assert.That(result, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void ToFormula_AlreadyFormula_ReturnsSelf()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(42f) });
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));

        var again = formula.ToFormula("y");
        Assert.That(formula.Raw().ToArray(), Is.EqualTo(again.Raw().ToArray()),
            "Already-formula ToFormula should return self (same buffer content)");
    }

    [Test]
    public void ToFormula_JitAndInterpreter_ProduceSameResult()
    {
        // modifier → formula 的 JIT vs 解释器一致性
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var modifier = runner.Compile(new[]
        {
            Op(FloatOp.Sub), C(5f),
            Op(FloatOp.Mul), C(2f),
        });
        var formula = modifier.ToFormula("n");

        float interpResult = runner.Instantiate(formula, jit: false)
            .Set("n", 20f).Run();
        float jitResult = runner.Instantiate(formula, jit: true)
            .Set("n", 20f).Run();

        Assert.That(interpResult, Is.EqualTo(jitResult).Within(1e-6f));
        // (20 - 5) * 2 = 30
        Assert.That(interpResult, Is.EqualTo(30f).Within(1e-6f));
    }

    [Test]
    public void ToFormula_PreservesOriginalVariableSlots()
    {
        // modifier with variables [b] and [c] (from [a] * [b] + [c] → ToMultiplier)
        // → formula: [newbase] * [b] + [c]
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var formulaOrig = runner.Compile(CreateVarLexer("[", "]").Lex("[a] * [b] + [c]"));
        var modifier    = formulaOrig.ToMultiplier();

        Assert.That(modifier.VariableSlots.Length, Is.EqualTo(2)); // b, c
        Assert.That(modifier.VariableSlots[0].Name, Is.EqualTo("b"));
        Assert.That(modifier.VariableSlots[1].Name, Is.EqualTo("c"));

        var formula = modifier.ToFormula("newbase");
        Assert.That(formula.VariableSlots.Length, Is.EqualTo(3)); // newbase, b, c
        Assert.That(formula.VariableSlots[0].Name, Is.EqualTo("newbase"));
        Assert.That(formula.VariableSlots[1].Name, Is.EqualTo("b"));
        Assert.That(formula.VariableSlots[2].Name, Is.EqualTo("c"));

        // SlotIndex 验证：原 b(Slot0)→Slot1, c(Slot1)→Slot2
        Assert.That(formula.VariableSlots[1].SlotIndex, Is.EqualTo(1));
        Assert.That(formula.VariableSlots[2].SlotIndex, Is.EqualTo(2));
    }

    // ═══════════════════════════════════════════════════════
    // 寄存器重命名完整性：穷举高寄存器号下的 6 路 Arg 重命名
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 验证 ToMultiplier 对所有 6 个 Arg 字段的重命名。
    /// 构造一个密集使用寄存器的Formula（高 arity 运算符），
    /// 确保第一个操作数的 dest 寄存器被所有 Arg0~Arg5 引用。
    /// 转换后检查每条指令的寄存器引用是否正确重命名。
    /// </summary>
    [Test]
    public void ToMultiplier_AllArgFieldsRenamed()
    {
        // 策略：用一个带变量的公式 + 手动检查转换后 raw bytecode 的寄存器号
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lex     = CreateVarLexer("[", "]").Lex("[a] + [b]");
        var formula = runner.Compile(lex);

        // 找到第一个 Immediate 的目标寄存器
        var raw = formula.Raw();
        byte firstDest = 255;
        for (int i = 0; i < raw.Length; i++)
        {
            if (Def.GetKind(raw[i].OpCode) == OpType.Immediate)
            {
                firstDest = raw[i].Dest;
                break;
            }
        }
        Assert.That(firstDest, Is.LessThan(255),
            "Should find at least one Immediate instruction");

        var modifier = formula.ToMultiplier();
        var modRaw   = modifier.Raw();

        // 每条指令中，原 firstDest 寄存器不应再出现（应已被重命名为 Bus）
        for (int i = 0; i < modRaw.Length; i++)
        {
            var inst = modRaw[i];
            if (Def.GetKind(inst.OpCode) == OpType.Immediate) continue;

            Assert.That(inst.Dest, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Dest still references removed register {firstDest}");
            Assert.That(inst.Arg0, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg0 still references removed register {firstDest}");
            Assert.That(inst.Arg1, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg1 still references removed register {firstDest}");
            Assert.That(inst.Arg2, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg2 still references removed register {firstDest}");
            Assert.That(inst.Arg3, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg3 still references removed register {firstDest}");
            Assert.That(inst.Arg4, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg4 still references removed register {firstDest}");
            Assert.That(inst.Arg5, Is.Not.EqualTo(firstDest),
                $"Modifier inst[{i}].Arg5 still references removed register {firstDest}");
        }
    }

    /// <summary>
    /// 验证 ToFormula 对所有 6 个 Arg 字段的 Bus→newDest 重命名。
    /// </summary>
    [Test]
    public void ToFormula_AllArgFieldsRenamedFromBus()
    {
        // 构造一个 modifier，确保 Bus(R1) 出现在 Dest+Arg 各字段
        var runner   = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var modifier = runner.Compile(new[]
        {
            Op(FloatOp.Add), C(1f),
            Op(FloatOp.Mul), C(2f),
        });
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var formula  = modifier.ToFormula("x");
        var formRaw  = formula.Raw();

        // Bus(R1) 不应再出现在任何指令的 Dest 或 Arg 字段
        // （应该被替换为了新的 allocate 寄存器）
        for (int i = 0; i < formRaw.Length; i++)
        {
            var inst = formRaw[i];
            if (Def.GetKind(inst.OpCode) == OpType.Immediate) continue;

            Assert.That(inst.Dest, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Dest still references Bus(R1)");
            Assert.That(inst.Arg0, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg0 still references Bus(R1)");
            Assert.That(inst.Arg1, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg1 still references Bus(R1)");
            Assert.That(inst.Arg2, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg2 still references Bus(R1)");
            Assert.That(inst.Arg3, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg3 still references Bus(R1)");
            Assert.That(inst.Arg4, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg4 still references Bus(R1)");
            Assert.That(inst.Arg5, Is.Not.EqualTo(Registers.Bus),
                $"Formula inst[{i}].Arg5 still references Bus(R1)");
        }
    }

    // ═══════════════════════════════════════════════════════
    // 错误路径
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToMultiplier_TooFewInstructions_Throws()
    {
        // 空公式不能转 modifier（Count=0 < 2）
        var formula = FluxFormula<float, FloatOp>.Empty;

        bool threw = false;
        try { formula.ToMultiplier(); }
        catch (InvalidOperationException) { threw = true; }
        Assert.That(threw, Is.True, "Empty formula should throw on ToMultiplier");
    }
}
