using System;
using FluxFormula.Core;
using NUnit.Framework;

namespace FluxFormula.Addressables.Tests
{
    /// <summary>
    /// FluxFormulaRef 构造与类型校验测试。
    /// Addressables 加载本身的集成测试需在完整 Addressables 环境下运行；
    /// 此处覆盖可独立验证的逻辑。
    /// </summary>
    public class FluxFormulaRefTests
    {
        [Test]
        public void Constructor_AcceptsValidGuid()
        {
            var guid = "abc123def456";
            var reference = new FluxFormulaRef<float, FloatMathDef>(guid);

            Assert.That(reference, Is.Not.Null);
            Assert.That(reference.RuntimeKeyIsValid(), Is.False,
                "假 GUID 在 Addressables 中无效，但构造不应抛异常");
        }

        [Test]
        public void Constructor_EmptyGuid_DoesNotThrow()
        {
            Assert.That(
                () => new FluxFormulaRef<float, FloatMathDef>(""),
                Throws.Nothing);
        }
    }
}
