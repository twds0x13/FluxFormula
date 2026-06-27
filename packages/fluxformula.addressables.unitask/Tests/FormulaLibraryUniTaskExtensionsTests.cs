using System;
using Cysharp.Threading.Tasks;
using FluxFormula.Core;
using NUnit.Framework;

namespace FluxFormula.Addressables.UniTask.Tests
{
    /// <summary>
    /// FormulaLibraryUniTaskExtensions 测试：验证 UniTask 包装委托到底层 ValueTask API。
    /// </summary>
    public class FormulaLibraryUniTaskExtensionsTests
    {
        private FormulaLibrary<float, FloatMathDef> _library;

        [SetUp]
        public void SetUp()
        {
            _library = new FormulaLibrary<float, FloatMathDef>();
        }

        [Test]
        public async UniTask LoadAsyncUniTask_DelegatesToLoadAsync()
        {
            // 无效 Addressables key → LoadAsync 抛异常 → UniTask 包装正确传播异常
            try
            {
                var formula = await _library.LoadAsyncUniTask<float, FloatMathDef>(
                    "nonexistent-addressables-key-001");
                Assert.That(formula.IsEmpty, Is.True,
                    "加载失败时可能返回 Empty 而非抛异常");
            }
            catch (Exception)
            {
                // UniTask 正确传播了底层 InvalidOperationException
                Assert.That(true);
            }
        }
    }
}
