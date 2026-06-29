using System;
using FluxFormula.Core;
using FluxFormula.Burst;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace FluxFormula.Burst.Tests
{
    /// <summary>
    /// FluxBurstInstance 完整生命周期测试：构造 → Set/SetIndex → Run/Schedule → Complete → Dispose。
    /// 同时覆盖 CreateBurstInstance 和 ScheduleBurst 扩展方法。
    /// </summary>
    public class FluxBurstInstanceTests
    {
        private static FluxAssembler<float, FloatMathDef> CreateAssembler() =>
            new FluxAssembler<float, FloatMathDef>(default);

        private static FluxFormula<float, FloatMathDef> CompileSimple()
        {
            return CreateAssembler().Compile(
                TestHelper.CreateMathLexer().Lex("10 + 20"));
        }

        private static FluxFormula<float, FloatMathDef> CompileVar()
        {
            return CreateAssembler().Compile(
                TestHelper.CreateVarLexer("[", "]").Lex("[atk] * 2 + [bonus]"));
        }

        // ═══════════════════════════════════════════════════════
        // CreateBurstInstance + Run (同步)
        // ═══════════════════════════════════════════════════════

        [Test]
        public void CreateBurstInstance_Run_SimpleFormula()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileSimple());
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(30f));
        }

        [Test]
        public void CreateBurstInstance_Run_WithVariables()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileVar());
            instance.Set("atk", 100f).Set("bonus", 50f);
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(250f));
        }

        [Test]
        public void CreateBurstInstance_Run_MultipleTimes_SameResult()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileSimple());

            for (int i = 0; i < 5; i++)
            {
                float result = instance.Run();
                Assert.That(result, Is.EqualTo(30f),
                    $"iteration {i} should return the same result");
            }
        }

        // ═══════════════════════════════════════════════════════
        // Set / SetIndex
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Set_ByIndex_AppliesValue()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileVar());
            instance.SetIndex(0, 10f).SetIndex(2, 20f);
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(40f));
        }

        [Test]
        public void Set_ByName_UnknownVariable_NoOp()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileVar());
            Assert.That(() => instance.Set("nonexistent", 999f), Throws.Nothing);
        }

        [Test]
        public void SetIndex_OutOfRange_NoOp()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileVar());
            Assert.That(() => instance.SetIndex(999, 123f), Throws.Nothing);
        }

        // ═══════════════════════════════════════════════════════
        // Schedule + Complete (异步)
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Schedule_Complete_ReturnsCorrectResult()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileSimple());
            instance.Schedule();
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void Schedule_WithDependency_CompletesCorrectly()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileSimple());
            var handle = instance.Schedule();
            var handle2 = instance.Schedule(handle);
            handle2.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void Complete_WithoutSchedule_NoOp()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileSimple());
            Assert.That(() => instance.Complete(), Throws.Nothing);
        }

        [Test]
        public void Schedule_WithVariables_ResultUsesInjectedValues()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.CreateBurstInstance(CompileVar());
            instance.Set("atk", 10f).Set("bonus", 5f);
            instance.Schedule();
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(25f));
        }

        // ═══════════════════════════════════════════════════════
        // ScheduleBurst 便捷方法
        // ═══════════════════════════════════════════════════════

        [Test]
        public void ScheduleBurst_Complete_Result()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.ScheduleBurst(
                CompileVar(), ("atk", 50f), ("bonus", 30f));
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(130f));
        }

        [Test]
        public void ScheduleBurst_NoVariables_Runs()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.ScheduleBurst(CompileSimple());
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void ScheduleBurst_NullVariables_Runs()
        {
            var assembler = CreateAssembler();
            using var instance = assembler.ScheduleBurst(CompileSimple(), null);
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        // ═══════════════════════════════════════════════════════
        // Dispose
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Dispose_ReleasesResources()
        {
            var assembler = CreateAssembler();
            var instance = assembler.CreateBurstInstance(CompileSimple());
            instance.Dispose();
            Assert.That(() => instance.Dispose(), Throws.Nothing);
        }

        [Test]
        public void Run_AfterDispose_Throws()
        {
            var assembler = CreateAssembler();
            var instance = assembler.CreateBurstInstance(CompileSimple());
            instance.Dispose();

            Assert.That(() => instance.Run(),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Schedule_AfterDispose_Throws()
        {
            var assembler = CreateAssembler();
            var instance = assembler.CreateBurstInstance(CompileSimple());
            instance.Dispose();

            Assert.That(() => instance.Schedule(),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Set_AfterDispose_Throws()
        {
            var assembler = CreateAssembler();
            var instance = assembler.CreateBurstInstance(CompileVar());
            instance.Dispose();

            Assert.That(() => instance.Set("atk", 1f),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Result_AfterDispose_Throws()
        {
            var assembler = CreateAssembler();
            var instance = assembler.CreateBurstInstance(CompileSimple());
            instance.Dispose();

            Assert.That(() => { var _ = instance.Result; },
                Throws.InstanceOf<ObjectDisposedException>());
        }

        // ═══════════════════════════════════════════════════════
        // 缓存构造
        // ═══════════════════════════════════════════════════════

        [Test]
        public void CreateBurstInstance_WithCache_SharesBytecode()
        {
            var assembler = CreateAssembler();
            using var cache = new NativeBytecodeCache(capacity: 16);
            using var instance = assembler.CreateBurstInstance(
                CompileSimple(), cache);

            float result = instance.Run();
            Assert.That(result, Is.EqualTo(30f));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateBurstInstance_WithCache_MultipleInstances_ShareSameEntry()
        {
            var assembler = CreateAssembler();
            using var cache = new NativeBytecodeCache(capacity: 16);
            var formula = CompileSimple();

            using var inst1 = assembler.CreateBurstInstance(formula, cache);
            using var inst2 = assembler.CreateBurstInstance(formula, cache);

            Assert.That(inst1.Run(), Is.EqualTo(30f));
            Assert.That(inst2.Run(), Is.EqualTo(30f));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateBurstInstance_WithCache_NullCache_Throws()
        {
            try
            {
                CreateAssembler().CreateBurstInstance(CompileSimple(), (INativeBytecodeCache)null);
                Assert.Fail("Expected ArgumentNullException");
            }
            catch (ArgumentNullException) { }
        }
    }
}
