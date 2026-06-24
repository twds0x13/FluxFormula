using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class ArithmeticTests
{
    // ── 基础运算 ─────────────────────────────────

    [Test]
    public void ConstantValue()
        => Assert.That(Eval(new[] { C(42f) }), Is.EqualTo(42f).Within(1e-6f));

    [Test]
    public void SimpleAddition()
        => Assert.That(Eval(new[] { C(1f), Op(FloatOp.Add), C(2f) }), Is.EqualTo(3f).Within(1e-6f));

    [Test]
    public void SimpleSubtraction()
        => Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(3f) }), Is.EqualTo(7f).Within(1e-6f));

    [Test]
    public void SimpleMultiplication()
        => Assert.That(Eval(new[] { C(4f), Op(FloatOp.Mul), C(5f) }), Is.EqualTo(20f).Within(1e-6f));

    [Test]
    public void SimpleDivision()
        => Assert.That(Eval(new[] { C(10f), Op(FloatOp.Div), C(4f) }), Is.EqualTo(2.5f).Within(1e-6f));

    // ── 优先级 ───────────────────────────────────

    [Test]
    public void MultiplicationHasHigherPrecedence()
    {
        Assert.That(Eval(new[] { C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.Mul), C(3f) }),
            Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void DivisionHasHigherPrecedence()
    {
        Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(6f), Op(FloatOp.Div), C(2f) }),
            Is.EqualTo(7f).Within(1e-6f));
    }

    [Test]
    public void SamePrecedenceLeftAssociative()
    {
        Assert.That(Eval(new[] { C(10f), Op(FloatOp.Sub), C(3f), Op(FloatOp.Sub), C(2f) }),
            Is.EqualTo(5f).Within(1e-6f));
    }

    // ── 括号 ─────────────────────────────────────

    [Test]
    public void ParenthesesOverridePrecedence()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
            Op(FloatOp.Mul), C(3f),
        }), Is.EqualTo(9f).Within(1e-6f));
    }

    [Test]
    public void NestedParentheses()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen),
                Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
                Op(FloatOp.Mul),
                Op(FloatOp.LParen), C(3f), Op(FloatOp.Add), C(4f), Op(FloatOp.RParen),
            Op(FloatOp.RParen),
        }), Is.EqualTo(21f).Within(1e-6f));
    }

    // ── 一元取负 ─────────────────────────────────

    [Test]
    public void UnaryNegate()
        => Assert.That(Eval(new[] { Op(FloatOp.Neg), C(5f) }), Is.EqualTo(-5f).Within(1e-6f));

    [Test]
    public void DoubleNegate()
        => Assert.That(Eval(new[] { Op(FloatOp.Neg), Op(FloatOp.Neg), C(5f) }), Is.EqualTo(5f).Within(1e-6f));

    [Test]
    public void NegateWithMultiplication()
    {
        Assert.That(Eval(new[] { Op(FloatOp.Neg), C(3f), Op(FloatOp.Mul), C(4f) }),
            Is.EqualTo(-12f).Within(1e-6f));
    }

    // ── 复杂表达式 ───────────────────────────────

    [Test]
    public void ComplexExpression()
    {
        Assert.That(Eval(new FluxToken<float>[]
        {
            Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f), Op(FloatOp.RParen),
            Op(FloatOp.Mul),
            Op(FloatOp.LParen), C(3f), Op(FloatOp.Add), C(4f), Op(FloatOp.RParen),
            Op(FloatOp.Sub),
            C(5f), Op(FloatOp.Mul), C(2f),
        }), Is.EqualTo(11f).Within(1e-6f));
    }

    // ── 错误条件 ─────────────────────────────────

    [Test]
    public void DivisionByZero_ReturnsNaN()
    {
        Assert.That(float.IsNaN(Eval(new[] { C(1f), Op(FloatOp.Div), C(0f) })), Is.True);
    }

    [Test]
    public void DivisionByZero_NaNShortCircuits()
    {
        float r = Eval(new[] { C(1f), Op(FloatOp.Div), C(0f), Op(FloatOp.Add), C(5f) });
        Assert.That(float.IsNaN(r), Is.True, "NaN should short-circuit");
    }

    [Test]
    public void UnmatchedRightParenthesis_Throws()
    {
        Assert.That(
            () => Eval(new[] { Op(FloatOp.RParen), C(1f), Op(FloatOp.Add), C(2f) }),
            Throws.TypeOf<FormatException>()
        );
    }

    [Test]
    public void UnmatchedLeftParenthesis_Throws()
    {
        Assert.That(
            () => Eval(new[] { Op(FloatOp.LParen), C(1f), Op(FloatOp.Add), C(2f) }),
            Throws.TypeOf<FormatException>()
        );
    }

    // ── 多元运算符（Select / Lerp）─────────────────

    [Test]
    public void Select_WhenFirstArgNonzero_ReturnsSecond()
    {
        // select(1, 10, 20) → 10
        float r = Eval(new[] { C(1f), C(10f), C(20f), Op(FloatOp.Select) });
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Select_WhenFirstArgZero_ReturnsThird()
    {
        // select(0, 10, 20) → 20
        float r = Eval(new[] { C(0f), C(10f), C(20f), Op(FloatOp.Select) });
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void Select_JitPath()
    {
        float r = Eval(new[] { C(1f), C(10f), C(20f), Op(FloatOp.Select) }, jit: true);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));

        r = Eval(new[] { C(0f), C(10f), C(20f), Op(FloatOp.Select) }, jit: true);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void Lerp_InterpolatesCorrectly()
    {
        // lerp(0, 10, 0.5) → 5
        float r = Eval(new[] { C(0f), C(10f), C(0.5f), Op(FloatOp.Lerp) });
        Assert.That(r, Is.EqualTo(5f).Within(1e-6f));

        // lerp(0, 10, 0) → 0
        r = Eval(new[] { C(0f), C(10f), C(0f), Op(FloatOp.Lerp) });
        Assert.That(r, Is.EqualTo(0f).Within(1e-6f));

        // lerp(0, 10, 1) → 10
        r = Eval(new[] { C(0f), C(10f), C(1f), Op(FloatOp.Lerp) });
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Lerp_JitPath()
    {
        float r = Eval(new[] { C(0f), C(10f), C(0.5f), Op(FloatOp.Lerp) }, jit: true);
        Assert.That(r, Is.EqualTo(5f).Within(1e-6f));
    }

    [Test]
    public void Select_WithArithmeticInArgs()
    {
        // select(1+0, 2*5, 30) → 10  (1+0 != 0, pick 2*5)
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("select(1 + 0, 2 * 5, 30)");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Select_WithArithmeticInArgs_Jit()
    {
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("select(1 + 0, 2 * 5, 30)");
        float r = Eval(result.Tokens, jit: true);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void NestedSelects_Interpreter()
    {
        // select(0, 10, select(1, 20, 30)) → 20
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("select(0, 10, select(1, 20, 30))");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void NestedSelects_JitPath()
    {
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("select(0, 10, select(1, 20, 30))");
        float r = Eval(result.Tokens, jit: true);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void NestedLerp_InSelect()
    {
        // select(1, lerp(0, 10, 0.5), 100) → select(1, 5, 100) → 5
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("select(1, lerp(0, 10, 0.5), 100)");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(5f).Within(1e-6f));
    }

    // ── 三元表达式 A ? B : C ─────────────────────

    [Test]
    public void Question_GetPair_ReturnsEmitOnMatch()
    {
        var pair = Def.GetPair((byte)FloatOp.Question);
        Assert.That(pair.EmitOnMatch, Is.True, "Question.EmitOnMatch should be true");
        Assert.That(pair.EmitOpCode, Is.EqualTo(FloatOp.Select), "Question.EmitOpCode should be Select");
        Assert.That(pair.PairRole, Is.EqualTo(Pair.None), "Question.PairRole should be None");
    }

    [Test]
    public void Ternary_DirectTokens_DumpBytecode()
    {
        var tokens = new[]
        {
            C(1f), Op(FloatOp.Question), C(10f), Op(FloatOp.Colon), C(20f)
        };
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(tokens);
        var raw = formula.Raw();

        // Verify: should have 3 immediates + 1 select + 1 return = 5+dataSlots instructions
        Assert.That(formula.ImmediateCount, Is.EqualTo(3), "Should have 3 immediates");
        Assert.That(formula.Type, Is.EqualTo(FluxType.Formula), "Should be Formula");

        // Check last non-Return instruction is Select (opcode 6)
        Assert.That(raw[formula.Count - 2].OpCode, Is.EqualTo(6), "Second-to-last inst should be Select(op=6)");

        float r = runner.Instantiate(formula, jit: false).Run();
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Ternary_DirectTokens_False()
    {
        var tokens = new[]
        {
            C(0f), Op(FloatOp.Question), C(10f), Op(FloatOp.Colon), C(20f)
        };
        float r = Eval(tokens);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void Ternary_Basic_TrueBranch()
    {
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("1 ? 10 : 20");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Ternary_Basic_FalseBranch()
    {
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("0 ? 10 : 20");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void Ternary_JitPath()
    {
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("1 ? 10 : 20");
        float r = Eval(result.Tokens, jit: true);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Ternary_WithArithmeticInCondition()
    {
        // 2*3 ? 10 : 20 → 6 != 0 → 10
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("2 * 3 ? 10 : 20");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Ternary_ArithmeticInBranches()
    {
        // 1 ? 2+3 : 10*2 → 5
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("1 ? 2 + 3 : 10 * 2");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(5f).Within(1e-6f));

        // 0 ? 2+3 : 10*2 → 20
        result = lexer.Lex("0 ? 2 + 3 : 10 * 2");
        r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(20f).Within(1e-6f));
    }

    [Test]
    public void Ternary_LowestPrecedence()
    {
        // 1+1 ? 0 : 100 → (1+1)?0:100 → 2!=0 → 0
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("1 + 1 ? 0 : 100");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void Ternary_NestedInFunctionCall()
    {
        // lerp(0, 1 ? 10 : 20, 0.5) → lerp(0, 10, 0.5) → 5
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("lerp(0, 1 ? 10 : 20, 0.5)");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(5f).Within(1e-6f));
    }

    [Test]
    public void Ternary_NestedInParentheses()
    {
        // (1 ? 2 : 3) + 10 → 2 + 10 → 12
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("(1 ? 2 : 3) + 10");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(12f).Within(1e-6f));
    }

    [Test]
    public void Ternary_NestedTernary()
    {
        // 1 ? (0 ? 100 : 200) : 300 → 1!=0 → (0?100:200) → 0==0 → 200
        var lexer = CreateFuncLexer();
        var result = lexer.Lex("1 ? (0 ? 100 : 200) : 300");
        float r = Eval(result.Tokens);
        Assert.That(r, Is.EqualTo(200f).Within(1e-6f));
    }

    // ── 六元联合加法 Sum6 (arity 6, 测试 Arg3-Arg5 全部路径) ──

    [Test]
    public void Sum6_Basic_Interpreter()
    {
        // sum6(1, 2, 3, 4, 5, 6) → 21
        var tokens = new[]
        {
            C(1f), C(2f), C(3f), C(4f), C(5f), C(6f), Op(FloatOp.Sum6)
        };
        float r = Eval(tokens);
        Assert.That(r, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void Sum6_Basic_Jit()
    {
        var tokens = new[]
        {
            C(1f), C(2f), C(3f), C(4f), C(5f), C(6f), Op(FloatOp.Sum6)
        };
        float r = Eval(tokens, jit: true);
        Assert.That(r, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void Sum6_WithNegativeValues()
    {
        var tokens = new[]
        {
            C(10f), C(-2f), C(3f), C(-4f), C(5f), C(-6f), Op(FloatOp.Sum6)
        };
        float r = Eval(tokens);
        Assert.That(r, Is.EqualTo(6f).Within(1e-6f));
    }

    [Test]
    public void Sum6_AsPartOfLargerExpression()
    {
        // 10 + sum6(1,2,3,4,5,6) → 10 + 21 → 31
        // Use direct RPN: push sum6 result first, then add
        var tokens = new[]
        {
            C(10f),
            C(1f), C(2f), C(3f), C(4f), C(5f), C(6f), Op(FloatOp.Sum6),
            Op(FloatOp.Add)
        };
        float r = Eval(tokens);
        Assert.That(r, Is.EqualTo(31f).Within(1e-6f));
    }

    [Test]
    public void Sum6_Jit_PruneRegisters_ScansArg3To5()
    {
        // JIT compile with pruneRegisters: verifies Arg3/Arg4/Arg5 scanning path
        // sum6 uses all 6 register fields → pruneRegisters must scan Arg2-Arg5
        var tokens = new[]
        {
            C(1f), C(2f), C(3f), C(4f), C(5f), C(6f), Op(FloatOp.Sum6)
        };
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(tokens);

        // JIT with pruneRegisters=true (triggered via internal path)
        // The Instantiate JIT path uses maxRegister from formula — if > 2, pruneRegisters activates
        float r = runner.Instantiate(formula, jit: true).Run();
        Assert.That(r, Is.EqualTo(21f).Within(1e-6f));
    }

    // ── 编译器异常路径 ────────────────────────────

    [Test]
    public void Compiler_RegStackOverflow_Throws()
    {
        // 65 个连续 Immediate → 寄存器栈溢出 (MaxStackDepth=64)
        var tokens = new FluxToken<float>[65];
        for (int i = 0; i < 65; i++)
            tokens[i] = C(i);
        Assert.That(() => Eval(tokens),
            Throws.TypeOf<StackOverflowException>());
    }

    [Test]
    public void Compiler_OpStackOverflow_Throws()
    {
        // 65 层嵌套 '(' → 操作符栈溢出 (opTop 从 -1 到 63 触发)
        int depth = 65;
        var tokens = new FluxToken<float>[depth + 1 + depth];
        int t = 0;
        for (int i = 0; i < depth; i++)
            tokens[t++] = Op(FloatOp.LParen);
        tokens[t++] = C(1f);
        for (int i = 0; i < depth; i++)
            tokens[t++] = Op(FloatOp.RParen);
        Assert.That(() => Eval(tokens),
            Throws.TypeOf<StackOverflowException>());
    }

    [Test]
    public void Compiler_UnmatchedRightBracket_Throws()
    {
        Assert.That(
            () => Eval(new[] { Op(FloatOp.RParen), C(1f) }),
            Throws.TypeOf<FormatException>().With.Message.Contains("Unmatched"));
    }
}
