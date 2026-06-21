using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class FluxInstanceTests
{
    [Test]
    public void ModifierCannotRunStandalone()
    {
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Build(new[] { Op(FloatOp.Add), C(5f) }, jit: false);
        bool threw = false;
        try { inst.Run(); }
        catch (InvalidOperationException) { threw = true; }
        Assert.That(threw, Is.True, "Modifier should throw when run standalone");
    }

    [Test]
    public void FormulaType_IsFormula_WhenStartsWithConst()
    {
        var formula = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula));
    }

    [Test]
    public void FormulaToString_ContainsTypeAndCount()
    {
        var formula = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(new[] { C(1f) });
        string s = formula.ToString();
        Assert.That(s.Contains("Formula"), Is.True, $"Expected 'Formula' in: {s}");
        Assert.That(s.Contains("Single"), Is.True, $"Expected 'Single' in: {s}");
    }

    [Test]
    public void MultipleSetCalls_ReuseInstance()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
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
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
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
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
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
        var fA = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("7 + 3"));          // R1 = 10
        var fB = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex("2 * 5")).ToMultiplier(); // modifier: R1 * 5
        var chain = fA.Connect(fB);                 // 10 * 5 = 50
        var inst = new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Instantiate(chain);
        Assert.That(inst.Run(), Is.EqualTo(50f).Within(1e-6f));
    }
}
