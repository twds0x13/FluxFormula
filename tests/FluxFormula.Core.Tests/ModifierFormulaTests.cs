using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ModifierFormulaTests
{
    // ═══════════════════════════════════════════════════════
    // ToMultiplier
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToMultiplier_Formula_BecomesModifier()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "2 + 3");

        var m = f.ToMultiplier();
        Assert.That(m.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(m.ImmediateCount, Is.EqualTo(f.ImmediateCount - 1));
    }

    [Test]
    public void ToMultiplier_Modifier_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "5 * 2");
        var m = f.ToMultiplier();

        var m2 = m.ToMultiplier();
        // Same bytecode, same type
        Assert.That(m2.Type, Is.EqualTo(FluxType.Modifier));
        Assert.That(m2.Count, Is.EqualTo(m.Count));
    }

    [Test]
    public void ToMultiplier_ChainFormula_ConvertsToAtomicFirst()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4").ToMultiplier();
        var chain = fA.Connect(fB);

        var m = chain.ToMultiplier();
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
        var m = f.ToMultiplier();

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
    public void ToMultiplier_ToFormula_RoundTrip_DifferentVarName()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "7 + 3");

        var modifier = f.ToMultiplier();
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var restored = modifier.ToFormula("test_var");
        Assert.That(restored.Type, Is.EqualTo(FluxType.Formula));

        // 通过新变量名注入原值求值应等价
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored).Set("test_var", 7f);

        Assert.That(inst.Run(), Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void ToMultiplier_ToFormula_RoundTrip_WithTwoVariables()
    {
        var lex = CreateVarLexer("[", "]");
        var f = Compile(lex, "[x] + [y]");
        // f = x + y, ImmediateCount = 2 (x, y)

        var modifier = f.ToMultiplier();
        // modifier = R1 + y, ImmediateCount = 1 (just y)
        Assert.That(modifier.ImmediateCount, Is.EqualTo(1));
        Assert.That(modifier.Type, Is.EqualTo(FluxType.Modifier));

        var restored = modifier.ToFormula("INTERNAL_X");
        // restored = INTERNAL_X + y, ImmediateCount = 2
        Assert.That(restored.ImmediateCount, Is.EqualTo(2));
        Assert.That(restored.Type, Is.EqualTo(FluxType.Formula));

        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored)
            .Set("INTERNAL_X", 7f)
            .Set("y", 3f);

        Assert.That(inst.Run(), Is.EqualTo(10f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 求值正确性
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToMultiplier_ModifierCannotRunStandalone()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2");
        var m = f.ToMultiplier();

        Assert.That(m.Type, Is.EqualTo(FluxType.Modifier));

        // Modifier 在 Run() 时抛异常
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Instantiate(m);
        bool threw = false;
        try { inst.Run(); } catch (InvalidOperationException) { threw = true; }
        Assert.That(threw, Is.True, "Modifier should throw when Run() is called");
    }

    [Test]
    public void ToFormula_WithVariable_CanRun()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "10 + 5");
        var m = f.ToMultiplier();
        var restored = m.ToFormula("my_input");

        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Instantiate(restored).Set("my_input", 10f);

        Assert.That(inst.Run(), Is.EqualTo(15f).Within(1e-6f));
    }

    [Test]
    public void ToMultiplier_ReducesInstructionCount()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "1 + 2");
        var m = f.ToMultiplier();

        // Modifier 少了一个 Immediate + dataSlots
        int dataSlots = 1; // float occupies 1 Instruction slot (4 bytes < 8 bytes)
        Assert.That(m.Count, Is.EqualTo(f.Count - 1 - dataSlots));
    }

    // ═══════════════════════════════════════════════════════
    // 边界
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToMultiplier_TooSmallFormula_Throws()
    {
        // Count < 2 的公式无法转为 Modifier
        var empty = FluxFormula<float, FloatOp>.Empty;
        Assert.Throws<InvalidOperationException>(() => empty.ToMultiplier());
    }

    [Test]
    public void ToFormula_KeepsVarName_WithCorrectPrefix()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "3 * 4");
        var m = f.ToMultiplier();
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
        var f = new FluxFormula<float, FloatOp>();
        Assert.That(f.IsChained, Is.False);
    }

    [Test]
    public void Connect_ThreeWayChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4").ToMultiplier();
        var fC = Compile(lexer, "5 + 6").ToMultiplier();

        var chain = fA.Connect(fB).Connect(fC);
        Assert.That(chain.ChainLength, Is.EqualTo(3));
    }

    [Test]
    public void ToMultiplier_AlreadyModifier_ReturnsSelf()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "2 * 3");
        var mod = f.ToMultiplier();
        var modAgain = mod.ToMultiplier();
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

        var restored = FluxFormula<float, FloatOp>.FromBytes(bytes);
        Assert.That(restored.IsChained, Is.False);

        float origVal = EvalFormula(orig);
        float restVal = EvalFormula(restored);
        Assert.That(restVal, Is.EqualTo(origVal).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    private static FluxFormula<float, FloatOp> Compile(
        FluxLexer<float, FloatOp> lexer, string expr)
    {
        return new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex(expr));
    }
}
