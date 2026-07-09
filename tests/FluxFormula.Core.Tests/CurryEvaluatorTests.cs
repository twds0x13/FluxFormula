using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public class CurryEvaluatorTests
{
    [Test]
    public void Bind_SingleVariable_ReturnsCorrectResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + 1"));

        var curry = runner.Curry(formula);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.That(curry.VariableCount, Is.EqualTo(1));

        curry = curry.Bind(3f);
        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(4f).Within(1e-6f));
    }

    [Test]
    public void Bind_MultipleVariables_Params_ReturnsCorrectResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        // params Bind: 一次绑定多个
        var curry = runner.Curry(formula);
        Assert.That(curry.VariableCount, Is.EqualTo(3));

        curry = curry.Bind(2f, 3f, 4f);
        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f),
            "2*3+4 = 10");
    }

    [Test]
    public void Bind_Incremental_ReachesSameResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        var curry = runner.Curry(formula);
        curry = curry.Bind(2f);       // a=2
        Assert.That(curry.BoundCount, Is.EqualTo(1));
        Assert.That(curry.IsCompleted, Is.False);

        curry = curry.Bind(3f);       // b=3
        Assert.That(curry.BoundCount, Is.EqualTo(2));
        Assert.That(curry.IsCompleted, Is.False);

        curry = curry.Bind(4f);       // c=4
        Assert.That(curry.BoundCount, Is.EqualTo(3));
        Assert.That(curry.IsCompleted, Is.True);

        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f),
            "2*3+4 = 10");
    }

    [Test]
    public void Bind_Forking_ProducesDifferentResults()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b]"));

        var base_ = runner.Curry(formula).Bind(3f); // a=3，挂起等 b

        // 分叉 1: b=4
        var branch1 = base_.Bind(4f);
        Assert.That(branch1.Result, Is.EqualTo(12f).Within(1e-6f));

        // 分叉 2: b=7
        var branch2 = base_.Bind(7f);
        Assert.That(branch2.Result, Is.EqualTo(21f).Within(1e-6f));

        // base_ 不受影响
        Assert.That(base_.BoundCount, Is.EqualTo(1));
    }

    [Test]
    public void Bind_ConstantBeforeVariable_InjectCorrectly()
    {
        // 常量在变量之前：确保柯里化不被常量干扰
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("1 + [x]"));

        var curry = runner.Curry(formula);
        Assert.That(curry.VariableCount, Is.EqualTo(1));

        curry = curry.Bind(3f);
        Assert.That(curry.Result, Is.EqualTo(4f).Within(1e-6f),
            "常量 1 不应被变量覆盖: 1+3 = 4");
    }

    [Test]
    public void Bind_ExcessValues_Clamped()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x]"));

        var curry = runner.Curry(formula);
        // 只有 1 个变量，但绑了 5 个值——多余的值被忽略
        curry = curry.Bind(1f, 2f, 3f, 4f, 5f);
        Assert.That(curry.Result, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void Result_UnboundVariables_FilledWithDefault()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        // 只绑 x，不绑 y——y 填 default(0)
        var curry = runner.Curry(formula).Bind(5f);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.That(curry.Result, Is.EqualTo(5f).Within(1e-6f),
            "5 + 0 = 5");

        // 再次访问 Result 返回相同值
        Assert.That(curry.Result, Is.EqualTo(5f).Within(1e-6f));
    }

    [Test]
    public void Curry_MatchesStandardEval()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        float stdResult = runner.Instantiate(formula)
            .Set("a", 2f).Set("b", 3f).Set("c", 4f).Run();
        float curryResult = runner.Curry(formula)
            .Bind(2f, 3f, 4f).Result;

        Assert.That(curryResult, Is.EqualTo(stdResult).Within(1e-6f));
    }

    [Test]
    public void Curry_ChainFormula()
    {
        // 链式公式 + 柯里化：验证它们可以配合
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var fA = runner.Compile(lexer.Lex("[a] + [b]"));
        var fB = runner.Compile(lexer.Lex("[d] * [c]")).ToModifier();
        var chain = fA.Connect(fB); // [a] * [b] * [c]? No — this is ([a]+[b]) * [c]

        // 合并为原子公式后柯里化
        var curry = runner.Curry(chain.ToAtomic());
        Assert.That(curry.VariableCount, Is.EqualTo(3)); // a, b, c

        float result = curry.Bind(2f, 3f, 4f).Result;
        Assert.That(result, Is.EqualTo(20f).Within(1e-6f),
            "(2+3)*4 = 20");
    }
}
