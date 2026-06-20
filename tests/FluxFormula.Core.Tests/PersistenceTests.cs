using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class PersistenceTests
{
    // ═══════════════════════════════════════════════════════════════
    // 基础往返测试
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public void Roundtrip_SimpleFormula_EvaluatesSame()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(new[] { C(1f), Op(FloatOp.Add), C(2f) });
        float expected = runner.Instantiate(before, jit: false).Run();

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        float actual = runner.Instantiate(after, jit: false).Run();
        Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void Roundtrip_ComplexFormula_EvaluatesSame()
    {
        // (1.5 + 2.5) * (3 - 1) / 2 + 5 * 3 = 19
        var tokens = new[]
        {
            C(1.5f), Op(FloatOp.Add), C(2.5f),
            Op(FloatOp.Mul), C(3f), Op(FloatOp.Sub), C(1f),
            Op(FloatOp.Div), C(2f), Op(FloatOp.Add),
            C(5f), Op(FloatOp.Mul), C(3f),
        };
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(tokens);
        float expected = runner.Instantiate(before, jit: false).Run();

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        float actual = runner.Instantiate(after, jit: false).Run();
        Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void Roundtrip_EmptyFormula_ReturnsEmpty()
    {
        var before = FluxFormula<float, FloatOp>.Empty;
        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        Assert.That(after.Count, Is.EqualTo(0));
        Assert.That(after.Type, Is.EqualTo(FluxType.Formula));
    }

    // ═══════════════════════════════════════════════════════════════
    // 类型与元数据保真测试
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public void Roundtrip_PreservesFluxType()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        // Modifier: 二元算术运算符后跟立即数——arity≠1 且 PairRole≠Left
        var before  = runner.Compile(new[] { Op(FloatOp.Add), C(3f) });
        Assert.That(before.Type, Is.EqualTo(FluxType.Modifier),
            "Precondition: should be Modifier");

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        Assert.That(after.Type, Is.EqualTo(before.Type));
    }

    [Test]
    public void Roundtrip_PreservesInstructionCount()
    {
        var tokens = new[]
        {
            C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.Mul), C(3f),
        };
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(tokens);

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        Assert.That(after.Count, Is.EqualTo(before.Count));
    }

    // ═══════════════════════════════════════════════════════════════
    // 变量保真测试
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public void Roundtrip_WithVariables_PreservesNames()
    {
        var lex  = CreateVarLexer("[", "]");
        var lr   = lex.Lex("[atk] * (1 + [crit_rate]) - [def]");
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(lr);

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        Assert.That(after.VariableSlots.Length, Is.EqualTo(before.VariableSlots.Length));
        for (int i = 0; i < before.VariableSlots.Length; i++)
        {
            Assert.That(after.VariableSlots[i].Name,
                Is.EqualTo(before.VariableSlots[i].Name));
            Assert.That(after.VariableSlots[i].SlotIndex,
                Is.EqualTo(before.VariableSlots[i].SlotIndex));
        }
    }

    [Test]
    public void Roundtrip_WithVariables_SetByName_Works()
    {
        var lex  = CreateVarLexer("[", "]");
        var lr   = lex.Lex("[a] * [b] + [c]");
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(lr);

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        float actual = runner.Instantiate(after, jit: false)
            .Set("a", 2f).Set("b", 3f).Set("c", 1f).Run();

        Assert.That(actual, Is.EqualTo(7f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════════════
    // LEGO 积木测试：存盘 → 加载 → Connect → 求值
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public void LegoBricks_LoadAndConnect()
    {
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);

        // 片段 A: [a] * [b]
        var lexA = CreateVarLexer("[", "]");
        var lrA  = lexA.Lex("[a] * [b]");
        var brickA = runner.Compile(lrA);

        // 片段 B: + [c]
        var lexB = CreateVarLexer("[", "]");
        var lrB  = lexB.Lex("+ [c]");
        var brickB = runner.Compile(lrB);

        // 各自存盘
        byte[] savedA = brickA.ToBytes();
        byte[] savedB = brickB.ToBytes();

        // 从磁盘加载并拼接
        var loadedA = FluxFormula<float, FloatOp>.FromBytes(savedA);
        var loadedB = FluxFormula<float, FloatOp>.FromBytes(savedB);
        var combined = loadedA.Connect(loadedB);

        float result = runner.Instantiate(combined, jit: false)
            .Set("a", 2f).Set("b", 3f).Set("c", 1f).Run();

        Assert.That(result, Is.EqualTo(7f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════════════
    // JIT 路径
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public void Roundtrip_JitPath_EvaluatesCorrectly()
    {
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(new[] { C(100f), Op(FloatOp.Add), C(23f) });

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        float actual = runner.Instantiate(after, jit: true).Run();
        Assert.That(actual, Is.EqualTo(123f).Within(1e-6f));
    }

    [Test]
    public void Roundtrip_JitPath_WithVars()
    {
        var lex  = CreateVarLexer("[", "]");
        var lr   = lex.Lex("[x] + [y]");
        var runner  = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var before  = runner.Compile(lr);

        byte[] raw = before.ToBytes();
        var after = FluxFormula<float, FloatOp>.FromBytes(raw);

        float actual = runner.Instantiate(after, jit: true)
            .Set("x", 10f).Set("y", 32f).Run();

        Assert.That(actual, Is.EqualTo(42f).Within(1e-6f));
    }
}
