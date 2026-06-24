using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FormulaCache 重置后的恢复路径测试。
/// 验证：缓存清空后，JIT delegate 能重新编译并正确执行。
/// </summary>
public class FormulaCacheResetTests
{
    [SetUp]
    public void SetUp()
    {
        FormulaCache.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        FormulaCache.Reset();
    }

    [Test]
    public void AfterReset_Interpreter_StillWorks()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });

        // 预热
        Assert.That(runner.Instantiate(formula, jit: false).Run(),
            Is.EqualTo(3f).Within(1e-6f));

        FormulaCache.Reset();

        // 重置后解释器仍应正常工作
        Assert.That(runner.Instantiate(formula, jit: false).Run(),
            Is.EqualTo(3f).Within(1e-6f));
    }

    [Test]
    public void AfterReset_Jit_RecompilesSuccessfully()
    {
        var runner  = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(new[] { C(10f), Op(FloatOp.Mul), C(3f) });

        // 预热 JIT（写入 delegate 缓存）
        Assert.That(runner.Instantiate(formula, jit: true).Run(),
            Is.EqualTo(30f).Within(1e-6f));

        long hitsBefore = FormulaCache.HitCount;
        long missesBefore = FormulaCache.MissCount;

        // 第二次 Instantiate 应命中缓存
        Assert.That(runner.Instantiate(formula, jit: true).Run(),
            Is.EqualTo(30f).Within(1e-6f));
        Assert.That(FormulaCache.HitCount, Is.GreaterThan(hitsBefore),
            "Second JIT instantiation should hit delegate cache");

        // 重置
        FormulaCache.Reset();

        // 重置后应重新编译（miss + compile），不应崩溃
        Assert.That(runner.Instantiate(formula, jit: true).Run(),
            Is.EqualTo(30f).Within(1e-6f));
        Assert.That(FormulaCache.MissCount, Is.GreaterThan(0),
            "After reset, cache should miss and recompile");
    }

    [Test]
    public void AfterReset_ConnectChain_JitRecovers()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA   = CreateVarLexer("[", "]").Lex("[a] + 0");
        var lexB   = CreateVarLexer("[", "]").Lex("+ [b]");
        var fA     = runner.Compile(lexA);
        var fB     = runner.Compile(lexB);
        var chain  = fA.Connect(fB);

        // 预热 JIT
        float expected = runner.Instantiate(chain, jit: true)
            .Set("a", 5f).Set("b", 7f).Run();

        FormulaCache.Reset();

        float result = runner.Instantiate(chain, jit: true)
            .Set("a", 5f).Set("b", 7f).Run();
        Assert.That(result, Is.EqualTo(expected).Within(1e-6f));
    }
}
