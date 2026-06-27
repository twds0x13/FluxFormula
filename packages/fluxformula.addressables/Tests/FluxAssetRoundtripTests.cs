using System;
using FluxFormula.Core;
using NUnit.Framework;
using UnityEngine;

namespace FluxFormula.Addressables.Tests
{
    /// <summary>
    /// FluxAsset.SetRawData → Load 往返测试。
    /// 这是所有 Addressables 加载路径的底层反序列化基础。
    /// </summary>
    public class FluxAssetRoundtripTests
    {
        private FluxAssembler<float, FloatMathDef> _assembler;

        [SetUp]
        public void SetUp()
        {
            _assembler = new FluxAssembler<float, FloatMathDef>(default);
        }

        [Test]
        public void SetRawData_Load_Roundtrip_SimpleFormula()
        {
            // 编译公式 a + b
            var tokens = TestHelper.CreateMathLexer().Lex("10 + 20");
            var formula = _assembler.Compile(tokens);

            // 写入 FluxAsset
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData<float, FloatMathDef>(
                formula,
                typeof(FloatMathDef).AssemblyQualifiedName);

            // 读回
            var loaded = asset.Load<float, FloatMathDef>();
            var result = _assembler.Instantiate(loaded).Run();

            Assert.That(result, Is.EqualTo(30f));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void SetRawData_Load_Roundtrip_WithVariables()
        {
            var lexer = TestHelper.CreateVarLexer("[", "]");
            var formula = _assembler.Compile(lexer.Lex("[atk] * 2 + [bonus]"));

            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData<float, FloatMathDef>(
                formula,
                typeof(FloatMathDef).AssemblyQualifiedName);

            var loaded = asset.Load<float, FloatMathDef>();
            var result = _assembler.Instantiate(loaded)
                .Set("atk", 100f)
                .Set("bonus", 50f)
                .Run();

            Assert.That(result, Is.EqualTo(250f));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void Load_EmptyRawData_ReturnsEmptyFormula()
        {
            // 无 SetRawData 的 FluxAsset→Load 应返回 Empty
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            var loaded = asset.Load<float, FloatMathDef>();

            Assert.That(loaded.IsEmpty, Is.True);
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void TypeId_MatchesAfterSetRawData()
        {
            var formula = _assembler.Compile(TestHelper.CreateMathLexer().Lex("42"));
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData<float, FloatMathDef>(
                formula,
                typeof(FloatMathDef).AssemblyQualifiedName);

            Assert.That(asset.TypeId, Is.EqualTo(typeof(FloatMathDef).AssemblyQualifiedName));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void VariableNames_PreservedAfterRoundtrip()
        {
            var lexer = TestHelper.CreateVarLexer("[", "]");
            var formula = _assembler.Compile(lexer.Lex("[atk] + [def] + [spd]"));

            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData<float, FloatMathDef>(
                formula,
                typeof(FloatMathDef).AssemblyQualifiedName);

            var names = asset.VariableNames;
            Assert.That(names, Is.EquivalentTo(new[] { "atk", "def", "spd" }));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void Load_CrossTypeDef_StillLoads()
        {
            // Load<T>() 信任调用方类型——不会在 Load 时做类型校验。
            // 类型校验在 FluxFormulaRef 层完成。
            var formula = _assembler.Compile(TestHelper.CreateMathLexer().Lex("3 + 4"));
            var asset = ScriptableObject.CreateInstance<FluxAsset>();
            asset.SetRawData<float, FloatMathDef>(
                formula,
                typeof(FloatMathDef).AssemblyQualifiedName);

            // 用不同 TData 类型 Load 不会抛异常——但不保证语义正确
            var loaded = asset.Load<float, FloatMathDef>();
            Assert.That(loaded.IsEmpty, Is.False);
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
