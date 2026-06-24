using System;
using FluxFormula.Compiler;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FluxJITCompiler 直接单元测试。
/// 目标：覆盖 pruneRegisters 路径和异常抛出路径。
/// </summary>
public unsafe class FluxJITCompilerTests
{
    // ═══════════════════════════════════════════════════════
    // Compile 基本行为
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Compile_SimpleExpression_ReturnsFunc()
    {
        // 编译 "2 + 3"，直接调用 delegate
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("2 + 3"));
        byte[] bytes = formula.ToBytes();
        var instSpan = FormulaFormat.GetInstructionSpan(bytes);

        var func = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payload);

        Assert.That(payload, Is.Not.Null);
        float result = func(payload);
        Assert.That(result, Is.EqualTo(5f).Within(1e-6f));
    }

    [Test]
    public void Compile_WithImmediate_Works()
    {
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("3.14"));
        byte[] bytes = formula.ToBytes();
        var instSpan = FormulaFormat.GetInstructionSpan(bytes);

        var func = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payload);

        float result = func(payload);
        Assert.That(result, Is.EqualTo(3.14f).Within(1e-6f));
    }

    [Test]
    public void Compile_ModifierType_Works()
    {
        // modifier 编译为 JIT delegate —— R1 的输出依赖上游
        var lexer = CreateMathLexer();
        var f = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("2 * 3"));
        var mod = f.ToMultiplier(); // * 6 modifier
        byte[] bytes = mod.ToBytes();
        var instSpan = FormulaFormat.GetInstructionSpan(bytes);

        var func = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payload);

        Assert.That(payload, Is.Not.Null);
        // modifier 输出 = R1 * 3（这里 R1 未初始化所以值是随机的，但不应崩溃）
        float result = func(payload);
        Assert.That(float.IsNaN(result), Is.False,
            "modifier 编译不应抛异常");
    }

    // ═══════════════════════════════════════════════════════
    // pruneRegisters 路径
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Compile_PruneRegisters_SameResult()
    {
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("1 + 2 * 3 - 4 / 5"));
        byte[] bytes = formula.ToBytes();
        var instSpan = FormulaFormat.GetInstructionSpan(bytes);

        // 不裁剪 vs 裁剪
        var funcNormal = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payloadNormal, pruneRegisters: false);
        var funcPruned = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payloadPruned, pruneRegisters: true);

        float rNormal = funcNormal(payloadNormal);
        float rPruned = funcPruned(payloadPruned);

        Assert.That(rPruned, Is.EqualTo(rNormal).Within(1e-6f),
            "pruneRegisters 不应改变计算结果");
    }

    [Test]
    public void Compile_PruneRegisters_MaxRegisterParam_StillWorks()
    {
        // pruneRegisters + maxRegister 组合使用
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("10 + 20 + 30 + 40"));
        byte[] bytes = formula.ToBytes();
        var instSpan = FormulaFormat.GetInstructionSpan(bytes);

        var func = FluxJITCompiler<float, FloatMathDef>.Compile(
            instSpan, Def, out var payload,
            pruneRegisters: true,
            maxRegister: formula.MaxRegister);

        float result = func(payload);
        Assert.That(result, Is.EqualTo(100f).Within(1e-6f));
    }

    [Test]
    public void Compile_PruneRegisters_CompilationSucceeds()
    {
        // 确保 pruneRegisters 扫描路径不会因遇到 Return 或其他 opcode 而崩溃
        var lexer = CreateMathLexer();
        var formula = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("42"));
        byte[] bytes = formula.ToBytes();
        ReadOnlySpan<Instruction> instSpan = FormulaFormat.GetInstructionSpan(bytes);

        // ref struct 不能用于 lambda——直接用 try-catch 验证
        try
        {
            FluxJITCompiler<float, FloatMathDef>.Compile(
                instSpan, Def, out _, pruneRegisters: true);
        }
        catch (Exception ex)
        {
            Assert.Fail($"pruneRegisters 编译不应抛异常，实际抛出: {ex}");
        }
    }
}
