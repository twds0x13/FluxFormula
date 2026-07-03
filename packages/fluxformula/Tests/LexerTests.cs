using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class LexerTests
{
    // ── 基础 Lexer ────────────────────────────────

    [Test]
    public void Lexer_SimpleNumber()
    {
        var tokens = CreateMathLexer().Lex("42f").Tokens;
        Assert.That(tokens.Length, Is.EqualTo(1));
        Assert.That((FloatOp)tokens[0].Oper, Is.EqualTo(FloatOp.Const));
        Assert.That(tokens[0].Data, Is.EqualTo(42f).Within(1e-6f));
    }

    [Test]
    public void Lexer_SimpleExpression()
    {
        var tokens = CreateMathLexer().Lex("1f + 2f").Tokens;
        Assert.That(tokens.Length, Is.EqualTo(3));
        Assert.That((FloatOp)tokens[0].Oper, Is.EqualTo(FloatOp.Const));
        Assert.That((FloatOp)tokens[1].Oper, Is.EqualTo(FloatOp.Add));
        Assert.That((FloatOp)tokens[2].Oper, Is.EqualTo(FloatOp.Const));
    }

    [Test]
    public void Lexer_ParenthesesExpression()
    {
        var tokens = CreateMathLexer().Lex("(1 + 2) * 3").Tokens;
        Assert.That(tokens.Length, Is.EqualTo(7));
        Assert.That((FloatOp)tokens[0].Oper, Is.EqualTo(FloatOp.LParen));
        Assert.That((FloatOp)tokens[4].Oper, Is.EqualTo(FloatOp.RParen));
        Assert.That((FloatOp)tokens[5].Oper, Is.EqualTo(FloatOp.Mul));
    }

    [Test]
    public void Lexer_WhitespaceInsignificant()
    {
        var tight  = CreateMathLexer().Lex("1+2").Tokens;
        var spaced = CreateMathLexer().Lex("1  +  2").Tokens;
        Assert.That(tight.Length, Is.EqualTo(spaced.Length));
        for (int i = 0; i < tight.Length; i++)
            Assert.That(tight[i].Oper, Is.EqualTo(spaced[i].Oper));
    }

    [Test]
    public void Lexer_EmptyOrWhitespace()
    {
        Assert.That(CreateMathLexer().Lex("").Tokens.Length, Is.EqualTo(0));
        Assert.That(CreateMathLexer().Lex("   \t  ").Tokens.Length, Is.EqualTo(0));
    }

    [Test]
    public void Lexer_LexThenCompileAndRun()
    {
        var tokens = CreateMathLexer().Lex("1 + 2 * 3").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void Lexer_LexComplexExpression()
    {
        var tokens = CreateMathLexer().Lex("(1.5 + 2.5) * 3 - 1").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(11f).Within(1e-6f));
    }

    [Test]
    public void Lexer_InvalidChar_ThrowsFormatException()
    {
        var lexer = CreateMathLexer();
        Assert.That(() => lexer.Lex("1 @ 2"), Throws.TypeOf<FormatException>());
    }

    // ── ResolveToken: Sub/Neg 同符号消歧 ──────────

    [Test]
    public void Lexer_UnaryNegate_ResolvedFromSub()
    {
        var tokens = CreateMathLexer().Lex("-5").Tokens;
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(-5f).Within(1e-6f));
    }

    [Test]
    public void Lexer_InfixSub_StaysAsSub()
    {
        var tokens = CreateMathLexer().Lex("5 - 3").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(2f).Within(1e-6f));
    }

    [Test]
    public void Lexer_NegateParenthesizedExpression()
    {
        var tokens = CreateMathLexer().Lex("-(1 + 2)").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(-3f).Within(1e-6f));
    }

    [Test]
    public void Lexer_MixedNegateAndSub()
    {
        var tokens = CreateMathLexer().Lex("-5 + 3 - 2").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(-4f).Within(1e-6f));
    }

    [Test]
    public void Lexer_DoubleNegate()
    {
        var tokens = CreateMathLexer().Lex("--5").Tokens;
        Assert.That(Eval(tokens, jit: false), Is.EqualTo(5f).Within(1e-6f));
    }

    [Test]
    public void Lexer_NegateWithJit()
    {
        var tokens = CreateMathLexer().Lex("-5 * 3").Tokens;
        float interp = Eval(tokens, jit: false);
        float jit    = Eval(tokens, jit: true);
        Assert.That(jit, Is.EqualTo(interp).Within(1e-6f));
        Assert.That(interp, Is.EqualTo(-15f).Within(1e-6f));
    }

    // ── 隐式乘法 ────────────────────────────────

    [Test]
    public void ImplicitMul_NumberBeforeParen()
    {
        var tokens = CreateImplicitMulLexer().Lex("2(3+4)").Tokens;
        Assert.That(Eval(tokens), Is.EqualTo(14f).Within(1e-6f));
    }

    [Test]
    public void ImplicitMul_ParenBeforeParen()
    {
        var tokens = CreateImplicitMulLexer().Lex("(2+3)(4+5)").Tokens;
        Assert.That(Eval(tokens), Is.EqualTo(45f).Within(1e-6f));
    }

    [Test]
    public void ImplicitMul_ParenBeforeNumber()
    {
        var tokens = CreateImplicitMulLexer().Lex("(2+3)4").Tokens;
        Assert.That(Eval(tokens), Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void ImplicitMul_Chained()
    {
        // 2(3)4 → 2 * 3 * 4 = 24
        var tokens = CreateImplicitMulLexer().Lex("2(3)4").Tokens;
        Assert.That(Eval(tokens), Is.EqualTo(24f).Within(1e-6f));
    }

    [Test]
    public void ImplicitMul_WithoutImplicitOperator_ReturnsFirstExprOnly()
    {
        // 默认 Lexer 无隐式乘法，2(3+4) 被解释为"2" + 独立的"(3+4)"，返回 2
        var tokens = CreateMathLexer().Lex("2(3+4)").Tokens;
        Assert.That(Eval(tokens), Is.EqualTo(2f).Within(1e-6f));
    }

    [Test]
    public void ImplicitMul_RespectsExplicitOperator()
    {
        // 2 * (3+4) 和 2(3+4) 结果相同
        float withStar = Eval(CreateMathLexer().Lex("2*(3+4)").Tokens);
        float implicitR = Eval(CreateImplicitMulLexer().Lex("2(3+4)").Tokens);
        Assert.That(withStar, Is.EqualTo(implicitR).Within(1e-6f));
    }

    // ── 变量（未知数）模式匹配 ─────────────────

    [Test]
    public void VariablePattern_BracketStyle()
    {
        var result = CreateVarLexer("[", "]").Lex("[self.atk] - [enemy.def]");
        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.VarNames[0], Is.EqualTo("self.atk"));
        Assert.That(result.VarNames[2], Is.EqualTo("enemy.def"));
    }

    [Test]
    public void VariablePattern_BracesStyle()
    {
        var result = CreateVarLexer("{var:", "}").Lex("{var:a} + {var:b}");
        Assert.That(result.Tokens.Length, Is.EqualTo(3));
        Assert.That(result.VarNames[0], Is.EqualTo("a"));
        Assert.That(result.VarNames[2], Is.EqualTo("b"));
    }

    [Test]
    public void Variable_SetByName_Run()
    {
        var result = CreateVarLexer("[", "]").Lex("[atk] - [def]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        float v = inst.Set("atk", 100f).Set("def", 50f).Run();
        Assert.That(v, Is.EqualTo(50f).Within(1e-6f));
    }

    [Test]
    public void Variable_SetByName_MissingVariable_Throws()
    {
        var result = CreateVarLexer("[", "]").Lex("[x] + [y]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        bool threw = false;
        try { inst.Set("z", 1f); } catch (ArgumentException) { threw = true; }
        Assert.That(threw, Is.True, "Should throw for undefined variable name");
    }

    [Test]
    public void Variable_SetByName_JitPath()
    {
        var result = CreateVarLexer("[", "]").Lex("[a] * [b]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: true);

        float v = inst.Set("a", 7f).Set("b", 3f).Run();
        Assert.That(v, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void Variable_NoPatterns_ReturnsNullNames()
    {
        var result = CreateMathLexer().Lex("1 + 2");
        Assert.That(result.VarNames.Length, Is.EqualTo(result.Tokens.Length));
        Assert.That(result.VarNames[0], Is.Null);
        Assert.That(result.VarNames[1], Is.Null);
        Assert.That(result.VarNames[2], Is.Null);
    }

    // ── 同名变量共享值 ─────────────────────────

    [Test]
    public void Variable_SameName_SharesValue_Interpreter()
    {
        // [x] + [x] 中两个 x 共享同一注入值
        var result = CreateVarLexer("[", "]").Lex("[x] + [x]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        float v = inst.Set("x", 5f).Run();
        Assert.That(v, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Variable_SameName_SharesValue_Jit()
    {
        var result = CreateVarLexer("[", "]").Lex("[x] * [x]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: true);

        float v = inst.Set("x", 3f).Run();
        Assert.That(v, Is.EqualTo(9f).Within(1e-6f));
    }

    [Test]
    public void Variable_SameName_ThreeOccurrences()
    {
        var result = CreateVarLexer("[", "]").Lex("[a] + [a] + [a]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        float v = inst.Set("a", 10f).Run();
        Assert.That(v, Is.EqualTo(30f).Within(1e-6f));
    }

    [Test]
    public void Variable_SameName_MixedWithDifferent()
    {
        // [x] + [x] + [y] — x 共用，y 独立
        var result = CreateVarLexer("[", "]").Lex("[x] + [x] + [y]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        float v = inst.Set("x", 2f).Set("y", 3f).Run();
        Assert.That(v, Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void Variable_SameName_StillThrowsForUndefined()
    {
        var result = CreateVarLexer("[", "]").Lex("[x] + [x]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);

        bool threw = false;
        try { inst.Set("z", 1f); } catch (ArgumentException) { threw = true; }
        Assert.That(threw, Is.True, "Unknown variable should still throw");
    }

    // ── 隐式运算符歧义 ─────────────────────────

    [Test]
    public void ImplicitMul_Ambiguous_ThrowsFormatException()
    {
        // 配置多个隐式运算符，遇到无法消歧的邻接时报错
        var lexer = new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            ImplicitOperators = { (byte)FloatOp.Mul, (byte)FloatOp.Add },
        });

        Assert.That(
            () => lexer.Lex("2 3"),
            Throws.TypeOf<FormatException>()
                .And.Message.Contains("Ambiguous"));
    }

    // ── 多变量模式共存 ─────────────────────────

    [Test]
    public void VariablePattern_MultiplePatterns_SameLexer()
    {
        // 同一个 Lexer 支持 [...] 和 {var:...} 两种语法
        var lexer = new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            VariablePatterns =
            {
                new("[", "]"),
                new("{var:", "}"),
            },
        });

        var result = lexer.Lex("[a] + {var:b}");
        Assert.That(result.VarNames[0], Is.EqualTo("a"));
        Assert.That(result.VarNames[2], Is.EqualTo("b"));

        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var inst   = runner.Instantiate(runner.Compile(result), jit: false);
        float v = inst.Set("a", 10f).Set("b", 7f).Run();
        Assert.That(v, Is.EqualTo(17f).Within(1e-6f));
    }
}
