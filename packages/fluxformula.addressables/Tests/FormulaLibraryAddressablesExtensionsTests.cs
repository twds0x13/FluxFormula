using System;
using FluxFormula.Core;
using NUnit.Framework;
using UnityEngine;

namespace FluxFormula.Addressables.Tests
{
    /// <summary>
    /// FormulaLibraryAddressablesExtensions 类型校验逻辑测试。
    /// 实际的 Addressables 异步加载在集成测试中覆盖。
    /// </summary>
    public class FormulaLibraryAddressablesExtensionsTests
    {
        private FormulaLibrary<float, FloatMathDef> _library;

        [SetUp]
        public void SetUp()
        {
            _library = new FormulaLibrary<float, FloatMathDef>();
        }

        [Test]
        public void CreateAsset_TypeId_AutoMatchesTDef()
        {
            var assembler = new FluxAssembler<float, FloatMathDef>(default);
            var formula = assembler.Compile(
                TestHelper.CreateMathLexer().Lex("1 + 1"));

            var asset = _library.CreateAsset(formula);

            Assert.That(asset.TypeId, Is.EqualTo(typeof(FloatMathDef).AssemblyQualifiedName));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void CreateAsset_Load_Roundtrip_ViaLibrary()
        {
            var assembler = new FluxAssembler<float, FloatMathDef>(default);
            var formula = assembler.Compile(
                TestHelper.CreateMathLexer().Lex("7 * 8"));

            var asset = _library.CreateAsset(formula);
            var loaded = asset.Load<float, FloatMathDef>();
            var result = assembler.Instantiate(loaded).Run();

            Assert.That(result, Is.EqualTo(56f));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void CreateAsset_WrongTypeDef_TypeIdMismatch()
        {
            // 场景：TDef 是 FloatMathDef，但手动传了不同的 typeId
            var assembler = new FluxAssembler<float, FloatMathDef>(default);
            var formula = assembler.Compile(
                TestHelper.CreateMathLexer().Lex("99"));

            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            // 故意写入错误的 TypeId
            asset.SetRawData<float, FloatMathDef>(
                formula, "SomeOtherNamespace.OtherDef, SomeAssembly");

            Assert.That(asset.TypeId, Is.Not.EqualTo(
                typeof(FloatMathDef).AssemblyQualifiedName));

            // Load 本身不校验——校验在 FluxFormulaRef 层
            var loaded = asset.Load<float, FloatMathDef>();
            Assert.That(loaded.IsEmpty, Is.False,
                "Load 信任调用方，不依赖 TypeId");
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
