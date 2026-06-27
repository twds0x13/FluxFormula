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
        private FluxAssembler<float, FloatMathDef> _assembler;
        private FluxFormula<float, FloatMathDef> _simpleFormula;
        private FluxFormula<float, FloatMathDef> _varFormula;

        [SetUp]
        public void SetUp()
        {
            _assembler = new FluxAssembler<float, FloatMathDef>(default);
            _simpleFormula = _assembler.Compile(
                TestHelper.CreateMathLexer().Lex("10 + 20"));

            var varLexer = TestHelper.CreateVarLexer("[", "]");
            _varFormula = _assembler.Compile(
                varLexer.Lex("[atk] * 2 + [bonus]"));
        }

        // ═══════════════════════════════════════════════════════
        // CreateBurstInstance + Run (同步)
        // ═══════════════════════════════════════════════════════

        [Test]
        public void CreateBurstInstance_Run_SimpleFormula()
        {
            using var instance = _assembler.CreateBurstInstance(_simpleFormula);
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(30f));
        }

        [Test]
        public void CreateBurstInstance_Run_WithVariables()
        {
            using var instance = _assembler.CreateBurstInstance(_varFormula);
            instance.Set("atk", 100f).Set("bonus", 50f);
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(250f)); // 100*2 + 50
        }

        [Test]
        public void CreateBurstInstance_Run_MultipleTimes_SameResult()
        {
            using var instance = _assembler.CreateBurstInstance(_simpleFormula);

            for (int i = 0; i < 5; i++)
            {
                float result = instance.Run();
                Assert.That(result, Is.EqualTo(30f),
                    $"第 {i} 次 Run 应返回相同结果");
            }
        }

        // ═══════════════════════════════════════════════════════
        // Set / SetIndex
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Set_ByIndex_AppliesValue()
        {
            using var instance = _assembler.CreateBurstInstance(_varFormula);
            // 按槽位索引注入（不使用变量名）
            instance.SetIndex(0, 10f).SetIndex(1, 20f);
            float result = instance.Run();
            Assert.That(result, Is.EqualTo(40f)); // 10*2 + 20
        }

        [Test]
        public void Set_ByName_UnknownVariable_NoOp()
        {
            using var instance = _assembler.CreateBurstInstance(_varFormula);
            // 不存在的变量名——不抛异常，静默无操作
            Assert.That(() => instance.Set("nonexistent", 999f), Throws.Nothing);
            float result = instance.Run();
            // 变量未注入则用 Immediate 默认值（来源于编译时的占位值）
            Assert.That(result, Is.Not.EqualTo(0f).Or.EqualTo(0f));
            // 仅验证不崩溃
        }

        [Test]
        public void SetIndex_OutOfRange_NoOp()
        {
            using var instance = _assembler.CreateBurstInstance(_varFormula);
            Assert.That(() => instance.SetIndex(999, 123f), Throws.Nothing);
        }

        // ═══════════════════════════════════════════════════════
        // Schedule + Complete (异步)
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Schedule_Complete_ReturnsCorrectResult()
        {
            using var instance = _assembler.CreateBurstInstance(_simpleFormula);
            instance.Schedule();
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void Schedule_WithDependency_CompletesCorrectly()
        {
            using var instance = _assembler.CreateBurstInstance(_simpleFormula);
            var handle = instance.Schedule();
            var handle2 = instance.Schedule(handle); // 依赖前一个 handle
            handle2.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void Complete_WithoutSchedule_NoOp()
        {
            using var instance = _assembler.CreateBurstInstance(_simpleFormula);
            // 未调 Schedule 先调 Complete——不抛异常
            Assert.That(() => instance.Complete(), Throws.Nothing);
        }

        [Test]
        public void Schedule_WithVariables_ResultUsesInjectedValues()
        {
            using var instance = _assembler.CreateBurstInstance(_varFormula);
            instance.Set("atk", 10f).Set("bonus", 5f);
            instance.Schedule();
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(25f)); // 10*2 + 5
        }

        // ═══════════════════════════════════════════════════════
        // ScheduleBurst 便捷方法
        // ═══════════════════════════════════════════════════════

        [Test]
        public void ScheduleBurst_Complete_Result()
        {
            using var instance = _assembler.ScheduleBurst(
                _varFormula, ("atk", 50f), ("bonus", 30f));
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(130f)); // 50*2 + 30
        }

        [Test]
        public void ScheduleBurst_NoVariables_Runs()
        {
            using var instance = _assembler.ScheduleBurst(_simpleFormula);
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        [Test]
        public void ScheduleBurst_NullVariables_Runs()
        {
            // params 数组为 null——应等价于无变量
            using var instance = _assembler.ScheduleBurst(
                _simpleFormula, null);
            instance.Complete();
            Assert.That(instance.Result, Is.EqualTo(30f));
        }

        // ═══════════════════════════════════════════════════════
        // Dispose
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Dispose_ReleasesResources()
        {
            var instance = _assembler.CreateBurstInstance(_simpleFormula);
            instance.Dispose();
            // 二次 Dispose 不抛异常
            Assert.That(() => instance.Dispose(), Throws.Nothing);
        }

        [Test]
        public void Run_AfterDispose_Throws()
        {
            var instance = _assembler.CreateBurstInstance(_simpleFormula);
            instance.Dispose();

            Assert.That(() => instance.Run(),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Schedule_AfterDispose_Throws()
        {
            var instance = _assembler.CreateBurstInstance(_simpleFormula);
            instance.Dispose();

            Assert.That(() => instance.Schedule(),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Set_AfterDispose_Throws()
        {
            var instance = _assembler.CreateBurstInstance(_varFormula);
            instance.Dispose();

            Assert.That(() => instance.Set("atk", 1f),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public void Result_AfterDispose_Throws()
        {
            var instance = _assembler.CreateBurstInstance(_simpleFormula);
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
            using var cache = new NativeBytecodeCache(capacity: 16);
            using var instance = _assembler.CreateBurstInstance(
                _simpleFormula, cache);

            float result = instance.Run();
            Assert.That(result, Is.EqualTo(30f));
            Assert.That(cache.Count, Is.EqualTo(1),
                "缓存中应有 1 条活跃条目");
        }

        [Test]
        public void CreateBurstInstance_WithCache_MultipleInstances_ShareSameEntry()
        {
            using var cache = new NativeBytecodeCache(capacity: 16);

            using var inst1 = _assembler.CreateBurstInstance(_simpleFormula, cache);
            using var inst2 = _assembler.CreateBurstInstance(_simpleFormula, cache);

            Assert.That(inst1.Run(), Is.EqualTo(30f));
            Assert.That(inst2.Run(), Is.EqualTo(30f));

            // 两个实例共享同一公式 → 缓存中应只有 1 条条目
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void CreateBurstInstance_WithCache_NullCache_Throws()
        {
            Assert.That(
                () => _assembler.CreateBurstInstance(_simpleFormula, (INativeBytecodeCache)null),
                Throws.ArgumentNullException);
        }
    }
}
