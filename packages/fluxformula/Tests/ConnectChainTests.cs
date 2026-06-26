using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// ChainLink 多层嵌套与边界测试。
/// 核心风险：多层 Connect 后 VariableSlot 的 SlotIndex 累积右移错误、
/// 合并去重遗漏、以及链式求值 vs 原子求值的一致性。
/// </summary>
public class ConnectChainTests
{
    // ═══════════════════════════════════════════════════════
    // 多层 Connect — 变量槽偏移
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ThreeDeepConnect_SlotIndicesAccumulateCorrectly()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA = CreateVarLexer("[", "]").Lex("[a] + 0");
        var lexB = CreateVarLexer("[", "]").Lex("+ [b]");
        var lexC = CreateVarLexer("[", "]").Lex("+ [c]");

        var fA = runner.Compile(lexA);
        var fB = runner.Compile(lexB);
        var fC = runner.Compile(lexC);

        var chain = fA.Connect(fB.ToModifier()).Connect(fC.ToModifier());
        var slots = chain.ToAtomic().VariableSlots;
        Assert.That(slots.Length, Is.EqualTo(3));

        Assert.That(slots[0].Name, Is.EqualTo("a"));
        Assert.That(slots[0].SlotIndex, Is.EqualTo(0));

        Assert.That(slots[1].Name, Is.EqualTo("b"));
        Assert.That(slots[1].SlotIndex,
            Is.EqualTo(fB.VariableSlots[0].SlotIndex + fA.ImmediateCount));

        Assert.That(slots[2].Name, Is.EqualTo("c"));
        Assert.That(slots[2].SlotIndex,
            Is.EqualTo(fC.VariableSlots[0].SlotIndex + fA.ImmediateCount + fB.ImmediateCount));
    }

    [Test]
    public void FiveDeepConnect_JitAndInterpreterAgree()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lex = CreateVarLexer("[", "]").Lex("[a]");

        var f = runner.Compile(lex);
        var chain = f.Connect(FluxModifier<float, FloatMathDef>.Empty); // start as chain
        string[] vars = { "b", "c", "d", "e" };
        for (int i = 0; i < vars.Length; i++)
        {
            var lexNext = CreateVarLexer("[", "]").Lex($"+ [{vars[i]}]");
            var modifier = runner.Compile(lexNext);
            chain = chain.Connect(modifier.ToModifier());
        }

        var slots = chain.ToAtomic().VariableSlots;
        Assert.That(slots.Length, Is.EqualTo(5));

        float result = runner.Instantiate(chain, jit: false)
            .Set("a", 1f).Set("b", 2f).Set("c", 3f).Set("d", 4f).Set("e", 5f).Run();

        Assert.That(result, Is.EqualTo(15f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 混合类型 Connect
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Connect_FormulaThenModifier_TypesAreCorrect()
    {
        var runner   = new FluxAssembler<float, FloatMathDef>(Def);
        var lexF     = CreateVarLexer("[", "]").Lex("[a] + [b]");
        var formula  = runner.Compile(lexF);
        var modifier = runner.Compile(new[] { Op(FloatOp.Mul), C(2f) });

        var chain = formula.Connect(modifier.ToModifier());
        Assert.That(chain.GetLinks()[0].Type, Is.EqualTo(FluxType.Formula));
        Assert.That(chain.ToAtomic().VariableSlots.Length, Is.EqualTo(2));

        float result = runner.Instantiate(chain, jit: false)
            .Set("a", 3f).Set("b", 4f).Run();
        Assert.That(result, Is.EqualTo(14f).Within(1e-6f));
    }

    [Test]
    public void Connect_ModifierThenModifier_ChainOfTwo()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var mod1 = runner.Compile(new[] { Op(FloatOp.Mul), C(2f) });
        var mod2 = runner.Compile(new[] { Op(FloatOp.Add), C(1f) });

        var chain = mod1.Connect(mod2.ToModifier());
        Assert.That(chain.GetLinks()[0].Type, Is.EqualTo(FluxType.Modifier));

        var provider = runner.Compile(new[] { C(10f) });
        var fullChain = provider.Connect(chain.ToAtomic().ToModifier());
        Assert.That(runner.Instantiate(fullChain, jit: false).Run(),
            Is.EqualTo(21f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 链式 → 原子转换
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToAtomic_PreservesCorrectness()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA = CreateVarLexer("[", "]").Lex("[a]");
        var lexB = CreateVarLexer("[", "]").Lex("+ [b]");
        var lexC = CreateVarLexer("[", "]").Lex("* [c]");

        var chain = runner.Compile(lexA)
            .Connect(runner.Compile(lexB).ToModifier())
            .Connect(runner.Compile(lexC).ToModifier());

        Assert.That(chain.Length, Is.EqualTo(3));

        float chainedResult = runner.Instantiate(chain, jit: false)
            .Set("a", 2f).Set("b", 3f).Set("c", 4f).Run();

        var atomic = chain.ToAtomic();

        float atomicResult = runner.Instantiate(atomic, jit: false)
            .Set("a", 2f).Set("b", 3f).Set("c", 4f).Run();

        Assert.That(atomicResult, Is.EqualTo(chainedResult).Within(1e-6f));
    }

    [Test]
    public void ToAtomic_VariableSlotsMerged()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA = CreateVarLexer("[", "]").Lex("[x] + 0");
        var lexB = CreateVarLexer("[", "]").Lex("+ [y]");
        var lexC = CreateVarLexer("[", "]").Lex("+ [z]");

        var chain = runner.Compile(lexA)
            .Connect(runner.Compile(lexB).ToModifier())
            .Connect(runner.Compile(lexC).ToModifier());

        var atomic = chain.ToAtomic();

        Assert.That(atomic.VariableSlots.Length, Is.EqualTo(3));
        Assert.That(atomic.VariableSlots[0].Name, Is.EqualTo("x"));
        Assert.That(atomic.VariableSlots[1].Name, Is.EqualTo("y"));
        Assert.That(atomic.VariableSlots[2].Name, Is.EqualTo("z"));
    }

    // ═══════════════════════════════════════════════════════
    // 边界情况
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Connect_SingleImmediateCount1_ReturnsNext()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var base_ = runner.Compile(CreateVarLexer("[", "]").Lex("[a] + 0"));
        var mod   = runner.Compile(CreateVarLexer("[", "]").Lex("+ [b]"));

        var chain = base_.Connect(mod.ToModifier());
        Assert.That(chain.ToAtomic().VariableSlots.Length, Is.EqualTo(2));

        float result = runner.Instantiate(chain, jit: false)
            .Set("a", 10f).Set("b", 5f).Run();
        Assert.That(result, Is.EqualTo(15f).Within(1e-6f));
    }

    [Test]
    public void Connect_ChainedWithVariables_PreservesSetIndexOrder()
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var lexA = CreateVarLexer("[", "]").Lex("[first] + 0");
        var lexB = CreateVarLexer("[", "]").Lex("+ [second]");
        var lexC = CreateVarLexer("[", "]").Lex("+ [third]");

        var chain = runner.Compile(lexA)
            .Connect(runner.Compile(lexB).ToModifier())
            .Connect(runner.Compile(lexC).ToModifier());

        float result = runner.Instantiate(chain, jit: false)
            .SetIndex(0, 100f)
            .SetIndex(1, 10f)
            .SetIndex(2, 1f)
            .Run();

        Assert.That(result, Is.GreaterThan(0f));
    }
}
