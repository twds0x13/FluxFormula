using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class FluxInstanceTests
{
    [Test]
    public void Modifier_ToFormula_Then_Run()
    {
        // FluxModifier 必须通过 ToFormula 转为 Formula 后才能求值
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var modifier = runner.Compile(new[] { Op(FloatOp.Add), C(5f) }).ToModifier();
        var formula = modifier.ToFormula("input");
        float result = runner.Instantiate(formula).Set("input", 10f).Run();
        Assert.That(result, Is.EqualTo(15f).Within(1e-6f));
    }

    [Test]
    public void FormulaType_IsFormula_WhenStartsWithConst()
    {
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));
    }

    [Test]
    public void FormulaToString_ContainsTypeAndCount()
    {
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(new[] { C(1f) });
        string s = formula.ToString();
        Assert.That(s.Contains("Formula"), Is.True, $"Expected 'Formula' in: {s}");
        Assert.That(s.Contains("Single"), Is.True, $"Expected 'Single' in: {s}");
    }

    [Test]
    public void MultipleSetCalls_ReuseInstance()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(0f), Op(FloatOp.Add), C(0f) });
        var inst    = runner.Instantiate(formula, jit: false);

        Assert.That(inst.SetIndex(0, 10f).SetIndex(1, 20f).Run(), Is.EqualTo(30f).Within(1e-6f));
        Assert.That(inst.SetIndex(0, 100f).SetIndex(1, 200f).Run(), Is.EqualTo(300f).Within(1e-6f));
    }

    [Test]
    public void FloatOp_IsByteSized()
    {
        unsafe { Assert.That(sizeof(FloatOp), Is.EqualTo(1)); }
    }

    [Test]
    public void SetIndex_Negative_Throws()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });

        bool threw = false;
        try
        {
            runner.Instantiate(formula, jit: false).SetIndex(-1, 10f);
        }
        catch (IndexOutOfRangeException) { threw = true; }
        Assert.That(threw, Is.True, "Negative index should throw");
    }

    [Test]
    public void SetIndex_OutOfBounds_Throws()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f) }); // 只有 1 个数据槽

        bool threw = false;
        try
        {
            runner.Instantiate(formula, jit: false).SetIndex(999, 10f);
        }
        catch (IndexOutOfRangeException) { threw = true; }
        Assert.That(threw, Is.True, "Out-of-bounds index should throw");
    }

    // ═══════════════════════════════════════════════════════
    // 链式 buffer
    // ═══════════════════════════════════════════════════════

    [Test]
    public void BuildLinkBuffer_UsedViaChainRun()
    {
        var lexer = CreateMathLexer();
        var fA = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("7 + 3"));          // R1 = 10
        var fB = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("2 * 5")).ToModifier(); // modifier: R1 * 5
        var chain = fA.Connect(fB);                 // 10 * 5 = 50
        var inst = new FluxAssembler<float, FloatMathDef>(Def)
            .Instantiate(chain);
        Assert.That(inst.Run(), Is.EqualTo(50f).Within(1e-6f));
    }

    [Test]
    public void ChainInterpreter_ConstantBeforeVariable_InjectCorrectly()
    {
        // 常量在变量之前：BuildLinkBuffer 不能把常量误认为变量
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        // fA: 1 + [x] → Immediates=2 (常量1, 变量x), VarSlots=[{"x", SlotIndex=1}]
        var fA = runner.Compile(lexer.Lex("1 + [x]"));
        // fB_raw: [y] + 2 → ToModifier 移除首 Immediate [y]，剩余 + 2 (VarSlots=[])
        var fB = runner.Compile(lexer.Lex("[y] + 2")).ToModifier();
        var chain = fA.Connect(fB);  // (1 + [x]) + 2

        // Bug 表现: 旧代码常数 1 被 Set("x", 3) 的值 3 覆盖 → (3+0)+2=5
        // 修复后: 常数 1 保持原值 → (1+3)+2=6
        float result = runner.Instantiate(chain).Set("x", 3f).Run();
        Assert.That(result, Is.EqualTo(6f).Within(1e-6f),
            "常量不应被变量值覆盖: (1+3)+2 = 6");
    }

    [Test]
    public void ChainInterpreter_VariableBeforeConstant_StillWorks()
    {
        // 变量在常量之前：回归测试，确保修复不破坏原有正确行为
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        // fA: [x] + 1 → Immediates=2 (变量x, 常量1), VarSlots=[{"x", SlotIndex=0}]
        var fA = runner.Compile(lexer.Lex("[x] + 1"));
        // fB: 2 * 5 → ToModifier → * 5
        var fB = runner.Compile(lexer.Lex("2 * 5")).ToModifier();
        var chain = fA.Connect(fB);  // ([x] + 1) * 5

        float result = runner.Instantiate(chain).Set("x", 5f).Run();
        Assert.That(result, Is.EqualTo(30f).Within(1e-6f),
            "(5+1)*5 = 30");
    }

    [Test]
    public void ChainInterpreter_AllVariables_MultiLink_Works()
    {
        // 全变量链：回归测试 LegoBricks 模式不受修复影响
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        // fA: [a] * [b] → Immediates=2, VarSlots=[{"a",0},{"b",1}]
        var fA = runner.Compile(lexer.Lex("[a] * [b]"));
        // fB_raw: [d] * [c] → ToModifier 移除 [d], VarSlots=[{"c",0}]
        var fB = runner.Compile(lexer.Lex("[d] * [c]")).ToModifier();
        var chain = fA.Connect(fB);  // [a] * [b] * [c]

        float result = runner.Instantiate(chain)
            .Set("a", 2f).Set("b", 3f).Set("c", 4f).Run();
        Assert.That(result, Is.EqualTo(24f).Within(1e-6f),
            "2*3*4 = 24");
    }
}
