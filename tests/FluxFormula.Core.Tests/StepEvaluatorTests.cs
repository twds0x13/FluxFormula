using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class StepEvaluatorTests
{
    [Test]
    public void Step_BasicFormula_AdvancesIP()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });

        var step = runner.StepDebug(formula);
        Assert.That(step.IsCompleted, Is.False);
        Assert.That(step.CurrentIP, Is.EqualTo(0));

        step = step.Step(); // Immediate: load 1
        Assert.That(step.CurrentIP, Is.GreaterThan(0));

        step = step.Step(); // Add
        Assert.That(step.CurrentIP, Is.GreaterThan(1));
    }

    [Test]
    public void Step_RunToEnd_ProducesCorrectResult()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(7f), Op(FloatOp.Add), C(3f) });

        var step = runner.StepDebug(formula).RunToEnd();
        Assert.That(step.IsCompleted, Is.True);
        Assert.That(step.Result, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Step_MatchesInstantiate()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(5f), Op(FloatOp.Mul), C(3f) });

        float std = runner.Instantiate(formula).Run();
        float step = runner.StepDebug(formula).RunToEnd().Result;

        Assert.That(step, Is.EqualTo(std).Within(1e-6f));
    }

    [Test]
    public void Step_InstructionCount_Correct()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f) }); // 一条 Immediate + Return

        var step = runner.StepDebug(formula);
        Assert.That(step.InstructionCount, Is.GreaterThan(0));

        int steps = 0;
        while (!step.IsCompleted)
        {
            step = step.Step();
            steps++;
        }
        Assert.That(steps, Is.EqualTo(2)); // Immediate + Return
    }

    [Test]
    public void Step_CurrentOpCode_BeforeFirstStep()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(42f), Op(FloatOp.Add), C(1f) });

        var step = runner.StepDebug(formula);
        Assert.That(step.IsCompleted, Is.False);
        // 第一条指令是 Immediate (Const)
        Assert.That(step.CurrentInstruction.OpCode, Is.EqualTo((byte)FloatOp.Const));
    }

    [Test]
    public void Step_AfterCompletion_ReturnsSelf()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f) });

        var step = runner.StepDebug(formula).RunToEnd();
        Assert.That(step.IsCompleted, Is.True);

        var same = step.Step();
        // 已完成的 state 上 Step 应返回自身
        Assert.That(same.IsCompleted, Is.True);
        Assert.That(same.Result, Is.EqualTo(step.Result));
    }

    [Test]
    public void Step_WithVariables()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        // 先注入值再步进调试
        var inst = runner.Instantiate(formula).Set("x", 3f).Set("y", 4f);
        // Step evaluator 直接使用公式的原始 bytecode（变量值是 default）
        // 这里只验证步进不崩溃
        var step = runner.StepDebug(formula);
        Assert.That(step.IsCompleted, Is.False);

        step = step.RunToEnd();
        Assert.That(step.IsCompleted, Is.True);
    }

    [Test]
    public void Step_RegsAccessible()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(7f) });

        var step = runner.StepDebug(formula);
        Assert.That(step.Regs.Length, Is.GreaterThan(0));

        step = step.Step(); // Immediate: 加载 7
        // 寄存器文件应该有值
        Assert.That(step.Regs.Length, Is.GreaterThan(0));
    }
}
