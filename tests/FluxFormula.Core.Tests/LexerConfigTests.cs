using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// LexerConfig 配置验证：默认 LiteralParser、null 守卫。
/// </summary>
public class LexerConfigTests
{
    [Test]
    public void DefaultLiteralParser_ReturnsDefault()
    {
        var cfg = new LexerConfig<float, FloatOp>();
        float result = cfg.LiteralParser("3.14");
        Assert.That(result, Is.EqualTo(0f));
    }

    [Test]
    public void NullLiteralParser_ThrowsOnConstruct()
    {
        var cfg = new LexerConfig<float, FloatOp> { LiteralParser = null! };
        Assert.That(() => new FluxLexer<float, FloatOp>(cfg),
            Throws.ArgumentException.With.Message.Contains("LiteralParser"));
    }
}
