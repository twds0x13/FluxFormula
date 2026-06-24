using System.Reflection;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

/// <summary>
/// FluxLexer 内部方法：IsWordChar、ExtractVarName、隐式乘法、变量扫描。
/// 这些通过反射或完整的 Lex 路径测试。
/// </summary>
public class LexerEdgeTests
{
    // ═══════════════════════════════════════════════════════
    // IsWordChar (private static)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void IsWordChar_Letter_ReturnsTrue()
    {
        var method = typeof(FluxLexer<float>).GetMethod("IsWordChar",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)method.Invoke(null, new object[] { 'a' }), Is.True);
        Assert.That((bool)method.Invoke(null, new object[] { 'Z' }), Is.True);
    }

    [Test]
    public void IsWordChar_Digit_ReturnsTrue()
    {
        var method = typeof(FluxLexer<float>).GetMethod("IsWordChar",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)method.Invoke(null, new object[] { '0' }), Is.True);
    }

    [Test]
    public void IsWordChar_Underscore_ReturnsTrue()
    {
        var method = typeof(FluxLexer<float>).GetMethod("IsWordChar",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)method.Invoke(null, new object[] { '_' }), Is.True);
    }

    [Test]
    public void IsWordChar_Operator_ReturnsFalse()
    {
        var method = typeof(FluxLexer<float>).GetMethod("IsWordChar",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That((bool)method.Invoke(null, new object[] { '+' }), Is.False);
        Assert.That((bool)method.Invoke(null, new object[] { ' ' }), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // ExtractVarName (private static)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ExtractVarName_StripsPrefixAndSuffix()
    {
        var method = typeof(FluxLexer<float>).GetMethod("ExtractVarName",
            BindingFlags.NonPublic | BindingFlags.Static);
        var rule = new VariablePatternRule { Prefix = "${", Suffix = "}" };
        string result = (string)method.Invoke(null, new object[] { "${damage}", rule });
        Assert.That(result, Is.EqualTo("damage"));
    }

    // ═══════════════════════════════════════════════════════
    // 隐式乘法
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ImplicitMultiplication_Compiles()
    {
        var lexer = CreateImplicitMulLexer();
        var f = new FluxAssembler<float, FloatMathDef>(Def)
            .Compile(lexer.Lex("2(3+4)"));
        float result = new FluxAssembler<float, FloatMathDef>(Def).Instantiate(f).Run();
        Assert.That(result, Is.EqualTo(14f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 变量扫描
    // ═══════════════════════════════════════════════════════

    [Test]
    public void VariableScanning_ProducesVarNames()
    {
        var lexer = CreateVarLexer("${", "}");
        var result = lexer.Lex("${damage} + ${multiplier}");

        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.VarNames, Does.Contain("damage"));
        Assert.That(result.VarNames, Does.Contain("multiplier"));
    }
}
