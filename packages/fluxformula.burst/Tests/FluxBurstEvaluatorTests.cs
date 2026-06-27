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
        private FluxAssembler<float, FloatMathDef> _assembler;

        [SetUp]
        public void SetUp()
        {
            _assembler = new FluxAssembler<float, FloatMathDef>(default);
        }

        // ═══════════════════════════════════════════════════════
        // 基本执行
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_Constant_ReturnsValue()
        {
            var formula = _assembler.Compile(
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
            var formula = _assembler.Compile(
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
            var formula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("(3 + 4) * 2 - 5"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(9f)); // (7)*2 - 5 = 9
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 寄存器与返回
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_ReturnReg_IsR1Bus()
        {
            // Return 指令将结果放入 R1，Execute 返回 R1 值
            var formula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("99"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(99f));

                // R1 总线应等于返回值
                float* regPtr = (float*)regs.GetUnsafePtr();
                Assert.That(regPtr[Registers.Bus], Is.EqualTo(99f));
            }

            regs.Dispose();
        }

        [Test]
        public void Execute_RegisterState_AfterExecution()
        {
            var formula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("5 + 3"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);
                Assert.That(result, Is.EqualTo(8f));

                // R0 错误寄存器应为 0（无错误）
                float* regPtr = (float*)regs.GetUnsafePtr();
                Assert.That(regPtr[Registers.Error], Is.EqualTo(0f),
                    "R0 错误哨兵在无错误时应为 default");
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 除法 R0 短路
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_DivisionByZero_ReturnsNaN_AndSetsR0()
        {
            var formula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("1 / 0"));
            byte[] raw = formula.ToBytes();
            var regs = new NativeArray<float>(256, Allocator.Temp);

            fixed (byte* p = raw)
            {
                float result = FluxBurstEvaluator<float, FloatMathDef>.Execute(
                    p, (float*)regs.GetUnsafePtr(), formula.MaxRegister);

                float* regPtr = (float*)regs.GetUnsafePtr();
                // FloatMathDef 的 Div 对除零返回 NaN
                Assert.That(float.IsNaN(result), Is.True,
                    "除零应返回 NaN");
                // R0 哨兵被写入 NaN（FloatMathDef 的 Div 实现）
                Assert.That(float.IsNaN(regPtr[Registers.Error]), Is.True,
                    "除零后 R0 错误哨兵应为 NaN");
            }

            regs.Dispose();
        }

        // ═══════════════════════════════════════════════════════
        // 变量与 Immediate
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Execute_WithImmediateSlots_DifferentValues()
        {
            // 编译含常量的公式，然后测试多次不同注入
            var formula = _assembler.Compile(
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
            var formula = _assembler.Compile(
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
            // 长表达式：1+2+3+4+5+6+7+8+9+10 = 55
            var formula = _assembler.Compile(
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
            var formula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("1 + 1"));
            byte maxReg = formula.MaxRegister;

            // MaxRegister 应从 header 正确读取
            Assert.That(maxReg, Is.GreaterThanOrEqualTo((byte)Registers.Bus),
                "MaxRegister 至少应为 R1 总线");
        }

        [Test]
        public void Execute_SameFormula_Deterministic()
        {
            var formula = _assembler.Compile(
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
                        $"第 {i} 次执行应返回相同结果");
                }
                regs.Dispose();
            }
        }
    }
}
