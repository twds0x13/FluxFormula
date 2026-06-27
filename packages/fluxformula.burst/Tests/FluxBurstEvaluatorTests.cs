using FluxFormula.Core;
using FluxFormula.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace FluxFormula.Burst.Tests
{
    /// <summary>
    /// FluxBurstEvaluator 的 unsafe 指针级独立测试。
    /// 绕过 FluxBurstInstance 胶水层，直接测试 byte* → Execute → result 路径。
    /// </summary>
    public unsafe class FluxBurstEvaluatorTests
    {
        private static FluxAssembler<float, FloatMathDef> CreateAssembler() =>
            new FluxAssembler<float, FloatMathDef>(default);

        // ═══════════════════════════════════════════════════════
        // 基本执行
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_Constant_ReturnsValue()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("42"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(42f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_Addition_ReturnsSum()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("10 + 20"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(30f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_ComplexExpression_ReturnsCorrect()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("(3 + 4) * 2 - 5"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(9f));
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 寄存器与返回
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_ReturnReg_IsR1Bus()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("99"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(99f));

                float* regPtr = (float*)regs.GetUnsafePtr();
                Assert.That(regPtr[Registers.Bus], Is.EqualTo(99f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_RegisterState_AfterExecution()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("5 + 3"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(8f));

                float* regPtr = (float*)regs.GetUnsafePtr();
                Assert.That(regPtr[Registers.Error], Is.EqualTo(0f));
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 除法 R0 短路
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_DivisionByZero_ReturnsNaN_AndSetsR0()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("1 / 0"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);

                float* regPtr = (float*)regs.GetUnsafePtr();
                Assert.That(float.IsNaN(result), Is.True);
                Assert.That(float.IsNaN(regPtr[Registers.Error]), Is.True);
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 变量与 Immediate
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_WithImmediateSlots_DifferentValues()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("100 + 200"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(300f));
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 边界
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_MinimalFormula_OneConstant()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("0"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(0f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_LargeBytecode_ManyOperations()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("1+2+3+4+5+6+7+8+9+10"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(55f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_MaxRegister_Respected()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("1 + 1"));
            byte maxReg = formula.MaxRegister;

            Assert.That(maxReg, Is.GreaterThanOrEqualTo((byte)Registers.Bus));
        }

        [Test]
        public void Execute_SameFormula_Deterministic()
        {
            var formula = CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("7 * 11"));
            byte[] raw = formula.ToBytes();

            for (int i = 0; i < 10; i++)
            {
                var regs = new NativeArray<float>(256, Allocator.Temp);
                fixed (byte* p = raw)
                {
                    float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                        p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                    Assert.That(result, Is.EqualTo(77f),
                        $"iteration {i} should return the same result");
                }
                regs.Dispose();
            }
        }
    }
}
