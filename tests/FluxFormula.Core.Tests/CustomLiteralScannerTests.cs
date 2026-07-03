using System;
using FluxFormula.Core;
using NUnit.Framework;

public class CustomLiteralScannerTests
{
    private static LiteralScanner<int> HexScanner =>
        (ReadOnlySpan<char> src, int pos, out int value) =>
        {
            value = 0;
            if (pos + 2 >= src.Length) return pos;
            if (src[pos] != '0' || (src[pos + 1] != 'x' && src[pos + 1] != 'X'))
                return pos;

            int end = pos + 2;
            while (end < src.Length && IsHexDigit(src[end]))
                end++;

            if (end == pos + 2) return pos; // no digits after 0x

            value = (int)ParseHex(src.Slice(pos + 2, end - pos - 2));
            return end;
        };

    private static bool IsHexDigit(char c) =>
        char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static uint ParseHex(ReadOnlySpan<char> hex)
    {
        uint v = 0;
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            v = v * 16 + (c >= '0' && c <= '9' ? (uint)(c - '0')
                       : c >= 'a' && c <= 'f' ? (uint)(c - 'a' + 10)
                       : (uint)(c - 'A' + 10));
        }
        return v;
    }

    // ================================================================
    // 基本测试
    // ================================================================

    [Test]
    public void DefaultScanner_WorksForDecimalNumbers()
    {
        var config = new LexerConfig<int>
        {
            LiteralOper   = 0,
            LiteralScanner = LexerConfig<int>.CreateDefaultNumberScanner(s => int.Parse(s)),
            Operators     = { new("+", 1) },
        };
        var lexer = new FluxLexer<int>(config);
        var result = lexer.Lex("42 + 7");

        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.Tokens[0].Oper, Is.EqualTo((byte)0));
        Assert.That(result.Tokens[0].Data, Is.EqualTo(42));
        Assert.That(result.Tokens[2].Data, Is.EqualTo(7));
    }

    [Test]
    public void DefaultScanner_HandlesFloatsWithFSuffix()
    {
        var config = new LexerConfig<float>
        {
            LiteralOper   = 0,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f', 'F'))),
            Operators     = { new("+", 1) },
        };
        var lexer = new FluxLexer<float>(config);
        var result = lexer.Lex("3.14f + 2.5F");

        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.Tokens[0].Data, Is.EqualTo(3.14f).Within(1e-5f));
        Assert.That(result.Tokens[2].Data, Is.EqualTo(2.5f).Within(1e-5f));
    }

    // ================================================================
    // 自定义扫描器
    // ================================================================

    [Test]
    public void CustomHexScanner_ParsesHexLiterals()
    {
        var config = new LexerConfig<int>
        {
            LiteralOper    = 0,
            LiteralScanner = HexScanner,
            Operators      = { new("+", 1), new("-", 2) },
        };
        var lexer = new FluxLexer<int>(config);
        var result = lexer.Lex("0xFF + 0x10 - 0x0");

        Assert.That(result.Tokens.Length, Is.EqualTo(5));
        Assert.That(result.Tokens[0].Data, Is.EqualTo(255));  // 0xFF
        Assert.That(result.Tokens[2].Data, Is.EqualTo(16));   // 0x10
        Assert.That(result.Tokens[4].Data, Is.EqualTo(0));    // 0x0
    }

    [Test]
    public void CustomScanner_NoMatch_FallsThroughToNextPhase()
    {
        int scanCalls = 0;
        var config = new LexerConfig<float>
        {
            LiteralOper    = 0,
            LiteralScanner = (ReadOnlySpan<char> src, int pos, out float v) =>
            {
                scanCalls++;
                v = 0;
                return pos;
            },
            Operators = { new("pi", 0) },
        };
        var lexer = new FluxLexer<float>(config);
        var result = lexer.Lex("pi");

        // Should fall through to operator matching
        Assert.That(result.Tokens.Length, Is.EqualTo(1));
        Assert.That(result.Tokens[0].Oper, Is.EqualTo((byte)0));
    }

    [Test]
    public void CustomScanner_MatchesAtSpecificPositions()
    {
        // Scanner that only matches the keyword "true"/"false" as bool→int
        var config = new LexerConfig<int>
        {
            LiteralOper    = 0,
            LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
            {
                value = 0;
                if (pos + 4 <= src.Length && src.Slice(pos, 4).SequenceEqual("true"))
                {
                    value = 1;
                    return pos + 4;
                }
                if (pos + 5 <= src.Length && src.Slice(pos, 5).SequenceEqual("false"))
                {
                    value = 0;
                    return pos + 5;
                }
                return pos;
            },
            Operators = { new("+", 1) },
        };
        var lexer = new FluxLexer<int>(config);
        var result = lexer.Lex("true + false");

        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.Tokens[0].Data, Is.EqualTo(1));
        Assert.That(result.Tokens[2].Data, Is.EqualTo(0));
    }

    // ================================================================
    // 边界条件
    // ================================================================

    [Test]
    public void CustomScanner_EmptyInput_ReturnsPos()
    {
        var config = new LexerConfig<int>
        {
            LiteralOper    = 0,
            LiteralScanner = (ReadOnlySpan<char> src, int pos, out int v) => { v = 0; return pos; },
        };
        var lexer = new FluxLexer<int>(config);

        // Empty string should produce empty result
        var result = lexer.Lex("");
        Assert.That(result.Tokens.Length, Is.EqualTo(0));
    }

    [Test]
    public void CustomScanner_WithVariablePatterns_StillWorks()
    {
        var config = new LexerConfig<float>
        {
            LiteralOper    = 0,
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
                s => float.Parse(s.TrimEnd('f', 'F'))),
            Operators       = { new("+", 1) },
            VariablePatterns = { new("[", "]") },
        };
        var lexer = new FluxLexer<float>(config);
        var result = lexer.Lex("[atk] + 10.5f");

        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.VarNames[0], Is.EqualTo("atk"));
        Assert.That(result.Tokens[2].Data, Is.EqualTo(10.5f).Within(1e-5f));
    }
}
