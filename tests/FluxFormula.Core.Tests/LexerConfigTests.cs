using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// LexerConfig 配置验证：LiteralScanner 必须设置守卫。
/// </summary>
public class LexerConfigTests
{
    // 未注册任何 ISerializerBlock 的测试类型，用于验证"无扫描器可用"的报错路径
    private struct TestNoScanner { public int Dummy; }

    [Test]
    public void NullLiteralScanner_ThrowsOnConstruct()
    {
        var cfg = new LexerConfig<TestNoScanner> { LiteralScanner = null! };
        Assert.That(() => new FluxLexer<TestNoScanner>(cfg),
            Throws.ArgumentException.With.Message.Contains("LiteralScanner"));
    }

    [Test]
    public void CreateDefaultNumberScanner_ProducesWorkingScanner()
    {
        var scanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s));
        float value;
        int end = scanner("3.14", 0, out value);
        Assert.That(end, Is.GreaterThan(0));
        Assert.That(value, Is.EqualTo(3.14f).Within(1e-5f));
    }
}
