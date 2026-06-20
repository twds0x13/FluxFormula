using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// 冒烟测试：验证全链路（Lexer → Compile → Run）无崩溃。
/// 详细测试见各分类文件。
/// </summary>
public class SmokeTest
{
    [Test]
    public void FullPipeline_LexerToResult()
    {
        var lexResult = CreateMathLexer().Lex("(1 + 2) * 3");
        Assert.That(Eval(lexResult.Tokens, jit: false), Is.EqualTo(9f).Within(1e-6f));
        Assert.That(Eval(lexResult.Tokens, jit: true), Is.EqualTo(9f).Within(1e-6f));
    }
}
