using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ModifierFormulaTests
{
    // ═══════════════════════════════════════════════════════
    // ToModifier
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToModifier_Formula_BecomesModifier()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "2 + 3");

        var m = f.ToModifier();
        Assert.That(m.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(m.ImmediateCount, Is.EqualTo(f.ImmediateCount - 1));
    }

    [Test]
    public void ToModifier_Modifier_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "5 * 2");
        var m = f.ToModifier();

        // m.Inner is already Type=Modifier, so ToModifier() on it is a no-op
        var m2 = m.Inner.ToModifier();
        Assert.That(m2.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(m2.Count, Is.EqualTo(m.Count));
    }

    [Test]
    public void ToModifier_ChainFormula_ConvertsToAtomicFirst()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4").ToModifier();
        var chain = fA.Connect(fB);

        var m = chain.ToModifier();
        Assert.That(m.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(m.IsChained, Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // ToFormula
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToFormula_Modifier_BecomesFormula()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "4 + 5");
        var m = f.ToModifier();

        var result = m.ToFormula("CHAIN_LINK_INTERNAL_0");
        Assert.That(result.Type, Is.EqualTo(FluxType.Formula));
        Assert.That(result.ImmediateCount, Is.EqualTo(m.ImmediateCount + 1));
    }

    [Test]
    public void ToFormula_Formula_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "42");

        var f2 = f.ToFormula("unused");
        Assert.That(f2.Type, Is.EqualTo(FluxType.Formula));
    }

    // ═══════════════════════════════════════════════════════
    // Round-trip
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToModifier_ToFormula_RoundTrip_DifferentVarName()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "7 + 3");

        var modifier = f.ToModifier();
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var restored = modifier.ToFormula("test_var");
        Assert.That(restored.Type, Is.EqualTo(FluxType.Formula));

        // 通过新变量名注入原值求值应等价
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored).Set("test_var", 7f);

        Assert.That(inst.Run(), Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void ToModifier_ToFormula_RoundTrip_WithTwoVariables()
    {
        var lex = CreateVarLexer("[", "]");
        var f = Compile(lex, "[x] + [y]");
        // f = x + y, ImmediateCount = 2 (x, y)

        var modifier = f.ToModifier();
        // modifier = R1 + y, ImmediateCount = 1 (just y)
        Assert.That(modifier.ImmediateCount, Is.EqualTo(1));
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var restored = modifier.ToFormula("INTERNAL_X");
        // restored = INTERNAL_X + y, ImmediateCount = 2
        Assert.That(restored.ImmediateCount, Is.EqualTo(2));
        Assert.That(restored.Type, Is.EqualTo(FluxType.Formula));

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored)
            .Set("INTERNAL_X", 7f)
            .Set("y", 3f);

        Assert.That(inst.Run(), Is.EqualTo(10f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 求值正确性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToModifier_ModifierNeedsToFormulaToRun()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2");
        var m = f.ToModifier();

        // Modifier 必须通过 ToFormula 才能求值
        var formula = m.ToFormula("x");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(formula).Set("x", 1f);
        Assert.That(inst.Run(), Is.EqualTo(3f).Within(1e-6f));
    }

    [Test]
    public void ToFormula_WithVariable_CanRun()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "10 + 5");
        var m = f.ToModifier();
        var restored = m.ToFormula("my_input");

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored).Set("my_input", 10f);

        Assert.That(inst.Run(), Is.EqualTo(15f).Within(1e-6f));
    }

    [Test]
    public void ToModifier_ReducesInstructionCount()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2");
        var m = f.ToModifier();

        // Modifier 少了一个 Immediate + dataSlots
        int dataSlots = 1; // float occupies 1 Instruction slot (4 bytes < 8 bytes)
        Assert.That(m.Count, Is.EqualTo(f.Count - 1 - dataSlots));
    }

    // ═══════════════════════════════════════════════════════
    // 边界
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToModifier_TooSmallFormula_Throws()
    {
        // Count < 2 的公式无法转为 Modifier
        var empty = FluxFormula<float, FloatMathDef>.Empty;
        Assert.Throws<InvalidOperationException>(() => empty.ToModifier());
    }

    [Test]
    public void ToFormula_KeepsVarName_WithCorrectPrefix()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "3 * 4");
        var m = f.ToModifier();
        var restored = m.ToFormula(ChainReserved.InternalPrefix + "0");

        // 变量名应保持用户指定的前缀
        Assert.That(restored.VariableSlots[0].Name,
            Is.EqualTo(ChainReserved.InternalPrefix + "0"));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════
    // 边界路径
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DefaultConstructor_CreatesEmpty()
    {
        var f = new FluxFormula<float, FloatMathDef>();
        Assert.That(f.IsChained, Is.False);
    }

    [Test]
    public void Connect_ThreeWayChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4").ToModifier();
        var fC = Compile(lexer, "5 + 6").ToModifier();

        var chain = fA.Connect(fB).Connect(fC);
        Assert.That(chain.ChainLength, Is.EqualTo(3));
    }

    [Test]
    public void ToModifier_AlreadyModifier_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "2 * 3");
        var mod = f.ToModifier();
        var modAgain = mod.Inner.ToModifier();
        Assert.That(modAgain.IsChained, Is.EqualTo(mod.IsChained));
    }

    [Test]
    public void ToAtomic_AlreadyAtomic_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "42");
        var atomic = f.ToAtomic();
        Assert.That(atomic.IsChained, Is.False);
    }

    [Test]
    public void FromBytes_RoundTrip()
    {
        var lexer = CreateMathLexer();
        var orig = Compile(lexer, "3.14 + 2.718 * 1.414");
        byte[] bytes = orig.ToBytes();

        var restored = FluxFormula<float, FloatMathDef>.FromBytes(bytes);
        Assert.That(restored.IsChained, Is.False);

        float origVal = EvalFormula(orig);
        float restVal = EvalFormula(restored);
        Assert.That(restVal, Is.EqualTo(origVal).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // FluxModifier.Connect
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Modifier_Connect_TwoModifiers_ReturnsModifier()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "1 + 2").ToModifier();
        var mB = Compile(lexer, "3 + 4").ToModifier();

        var chain = mA.Connect(mB);

        Assert.That(chain.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(chain.IsChained, Is.True);
        // Can be promoted to Formula and evaluated
        var formula = chain.ToFormula("x");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var result = runner.Instantiate(formula).Set("x", 1f).Run();
        // mA="1+2"→"R1+2", mB="3+4"→"R1+4". Chain: (x+2)+4 = (1+2)+4 = 7
        Assert.That(result, Is.EqualTo(7f).Within(1e-5f));
    }

    [Test]
    public void Modifier_Connect_EmptyLeft_ReturnsRight()
    {
        var lexer = CreateMathLexer();
        var m = Compile(lexer, "5 + 6").ToModifier();
        var empty = FluxModifier<float, FloatMathDef>.Empty;

        var result = empty.Connect(m);
        Assert.That(result.Count, Is.GreaterThan(0));
        Assert.That(result.Type, Is.EqualTo(FluxType.Modifier));
    }

    [Test]
    public void Modifier_Connect_EmptyRight_ReturnsLeft()
    {
        var lexer = CreateMathLexer();
        var m = Compile(lexer, "5 + 6").ToModifier();

        var result = m.Connect(FluxModifier<float, FloatMathDef>.Empty);
        Assert.That(result.Count, Is.EqualTo(m.Count));
    }

    [Test]
    public void Modifier_Connect_ThreeModifierChain()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "1 + 2").ToModifier();
        var mB = Compile(lexer, "3 + 4").ToModifier();
        var mC = Compile(lexer, "5 + 6").ToModifier();

        var chain = mA.Connect(mB).Connect(mC);
        Assert.That(chain.IsChained, Is.True);
        Assert.That(chain.ChainLength, Is.EqualTo(3));
    }

    // ═══════════════════════════════════════════════════════
    // FluxModifier 序列化
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Modifier_ToBytes_FromBytes_RoundTrip()
    {
        var lexer = CreateMathLexer();
        var orig = Compile(lexer, "7 + 3").ToModifier();
        var bytes = orig.ToBytes();

        var restored = FluxModifier<float, FloatMathDef>.FromBytes(bytes);
        Assert.That(restored.Count, Is.EqualTo(orig.Count));
        Assert.That(restored.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(restored.ImmediateCount, Is.EqualTo(orig.ImmediateCount));
    }

    [Test]
    public void Modifier_FromBytes_RoundTrip_CanRunAfterToFormula()
    {
        var lexer = CreateMathLexer();
        var orig = Compile(lexer, "10 + 5").ToModifier();
        var bytes = orig.ToBytes();

        var restored = FluxModifier<float, FloatMathDef>.FromBytes(bytes);
        var formula = restored.ToFormula("y");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var result = runner.Instantiate(formula).Set("y", 10f).Run();

        Assert.That(result, Is.EqualTo(15f).Within(1e-5f));
    }

    [Test]
    public void Modifier_FromBytes_ReadOnlySpan_RoundTrip()
    {
        var lexer = CreateMathLexer();
        var orig = Compile(lexer, "2 * 8").ToModifier();
        var bytes = orig.ToBytes();

        var restored = FluxModifier<float, FloatMathDef>.FromBytes(new ReadOnlySpan<byte>(bytes));
        Assert.That(restored.Count, Is.EqualTo(orig.Count));
        Assert.That(restored.Type, Is.EqualTo(FluxType.Modifier));
    }

    // ═══════════════════════════════════════════════════════
    // FluxModifier 属性与方法
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Modifier_GetByteHash_SameBytecode_SameHash()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "3 * 7").ToModifier();
        var mB = Compile(lexer, "3 * 7").ToModifier();

        Assert.That(mA.GetByteHash(), Is.EqualTo(mB.GetByteHash()));
    }

    [Test]
    public void Modifier_GetByteHash_DifferentBytecode_DifferentHash()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "3 * 7").ToModifier();
        var mB = Compile(lexer, "4 + 9").ToModifier();

        Assert.That(mA.GetByteHash(), Is.Not.EqualTo(mB.GetByteHash()));
    }

    [Test]
    public void Modifier_Raw_ReturnsCorrectSpan()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2");
        var m = f.ToModifier();

        var raw = m.Raw();
        Assert.That(raw.Length, Is.EqualTo(m.Count));
    }

    [Test]
    public void Modifier_GetChainLinks_AtomicModifier_ReturnsEmpty()
    {
        var lexer = CreateMathLexer();
        var m = Compile(lexer, "1 + 2").ToModifier();

        var links = m.GetChainLinks();
        Assert.That(links.Length, Is.EqualTo(0));
    }

    [Test]
    public void Modifier_GetChainLinks_ChainedModifier_ReturnsLinks()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "1 + 2").ToModifier();
        var mB = Compile(lexer, "3 + 4").ToModifier();
        var chain = mA.Connect(mB);

        var links = chain.GetChainLinks();
        Assert.That(links.Length, Is.EqualTo(2));
    }

    [Test]
    public void Modifier_IsChained_AtomicModifier_ReturnsFalse()
    {
        var lexer = CreateMathLexer();
        var m = Compile(lexer, "5 * 3").ToModifier();
        Assert.That(m.IsChained, Is.False);
    }

    [Test]
    public void Modifier_Empty_HasZeroCount()
    {
        var empty = FluxModifier<float, FloatMathDef>.Empty;
        Assert.That(empty.Count, Is.EqualTo(0));
        Assert.That(empty.IsChained, Is.False);
    }

    [Test]
    public void Modifier_VariableSlots_Preserved()
    {
        var lex = CreateVarLexer("[", "]");
        var f = Compile(lex, "[x] + [y]");
        var m = f.ToModifier();

        // First variable [x] removed (was the first operand), [y] remains
        Assert.That(m.VariableSlots.Length, Is.EqualTo(1));
        Assert.That(m.VariableSlots[0].Name, Is.EqualTo("y"));
    }

    [Test]
    public void Modifier_MaxRegister_MatchesInner()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2 * 3");
        var m = f.ToModifier();

        Assert.That(m.MaxRegister, Is.EqualTo(f.MaxRegister));
    }

    // ═══════════════════════════════════════════════════════
    // P3-1 回归: Modifier 首链 Instantiate+Run 正确性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ModifierFirstChain_Instantiate_Run_Interpreter()
    {
        // 构造 Modifier 首链（模拟 VFF 反序列化的异常状态）
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "2 + 3").ToModifier();
        var mB = Compile(lexer, "4 + 5").ToModifier();
        var chain = mA.Connect(mB);
        var modFormula = chain.Inner; // Type=Modifier, IsChained=true

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(modFormula, jit: false);

        // 首 link 为 Modifier 时 Run() 应产出正确结果，而非静默错误
        // mA 去除首操作数后为 "R1 + 3"，mB 去除首操作数后为 "R1 + 5"
        // 串联后：R1 + 3 + 5，R1=default=0 → 8
        Assert.That(inst.Run(), Is.EqualTo(8f).Within(1e-5f));
    }

    [Test]
    public void ModifierFirstChain_Instantiate_Run_Jit()
    {
        var lexer = CreateMathLexer();
        var mA = Compile(lexer, "2 + 3").ToModifier();
        var mB = Compile(lexer, "4 + 5").ToModifier();
        var chain = mA.Connect(mB);
        var modFormula = chain.Inner;

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst = runner.Instantiate(modFormula, jit: true);
        Assert.That(inst.Run(), Is.EqualTo(8f).Within(1e-5f));
    }

    [Test]
    public void ModifierFirstChain_Instantiate_Run_Jit_WithVariable()
    {
        // 带变量的 Modifier 首链：JIT / 解释器一致性
        // "1+[x]" → ToModifier: "R1+[x]"（字面量 1 剥离，变量 [x] 保留）
        var lex = CreateVarLexer("[", "]");
        var fA = Compile(lex, "1 + [x]");
        var fB = Compile(lex, "2 + [y]");
        var chain = fA.ToModifier().Connect(fB.ToModifier());
        var modFormula = chain.Inner;

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var instJit = runner.Instantiate(modFormula, jit: true);
        var instInt = runner.Instantiate(modFormula, jit: false);

        // JIT 与解释器结果一致
        Assert.That(instJit.Run(), Is.EqualTo(instInt.Run()).Within(1e-5f));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    private static FluxFormula<float, FloatMathDef> Compile(
        FluxLexer<float> lexer, string expr)
    {
        return new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex(expr));
    }
}
