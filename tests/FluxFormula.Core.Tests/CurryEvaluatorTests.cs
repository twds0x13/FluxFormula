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
    public void ForceComplete_FillsUnboundWithDefault()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        // 只绑 x，不绑 y
        var curry = runner.Curry(formula).Bind(5f);
        Assert.That(curry.IsCompleted, Is.False);

        // Result 掩码未满时抛异常
        Assert.Throws<InvalidOperationException>(() => { var _ = curry.Result; });

        // ForceComplete 显式填充 default
        var complete = curry.ForceComplete();
        Assert.That(complete.IsCompleted, Is.True);
        Assert.That(complete.Result, Is.EqualTo(5f).Within(1e-6f),
            "5 + 0 = 5");
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

    // ═══════════════════════════════════════════════════════
    // Bind(name, value) — 乱序绑定
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Bind_ByName_OutOfOrder_ReturnsCorrectResult()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        // 乱序：先绑第三个，再绑第一个，最后第二个
        var curry = runner.Curry(formula)
            .Bind("c", 4f)
            .Bind("a", 2f)
            .Bind("b", 3f);

        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f),
            "2*3+4 = 10");
    }

    [Test]
    public void Bind_ByName_ThenParams_MixedOrder()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        // 混合：按名绑 a/c，params 填剩余 (b)
        var curry = runner.Curry(formula)
            .Bind("c", 4f)
            .Bind(2f, 3f); // 顺序填充下一个未绑定位置 (a, b)

        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void Bind_ByName_NotFound_Throws()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        var curry = runner.Curry(formula);
        var ex = Assert.Throws<ArgumentException>(() => curry.Bind("z", 1f));
        Assert.That(ex.Message, Does.Contain("'z'"));
    }

    [Test]
    public void Bind_ByName_AlreadyBound_Throws()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        var curry = runner.Curry(formula).Bind("x", 1f);
        var ex = Assert.Throws<ArgumentException>(() => curry.Bind("x", 99f));
        Assert.That(ex.Message, Does.Contain("already bound"));
    }

    [Test]
    public void Result_WhenIncomplete_Throws()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        var curry = runner.Curry(formula).Bind("x", 5f);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.Throws<InvalidOperationException>(() => { var _ = curry.Result; });
    }

    [Test]
    public void Bind_ByName_CrossFork_IndependentResults()
    {
        var lexer  = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[base] * [mult]"));

        var branch1 = runner.Curry(formula).Bind("base", 10f).Bind("mult", 2f);
        var branch2 = runner.Curry(formula).Bind("mult", 5f).Bind("base", 3f);

        Assert.That(branch1.Result, Is.EqualTo(20f).Within(1e-6f));
        Assert.That(branch2.Result, Is.EqualTo(15f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // TryBind — 静默注入，不抛异常
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TryBind_ByName_ValidName_WritesValue()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        var curry = runner.Curry(formula)
            .TryBind("x", 3f)
            .TryBind("y", 7f);

        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void TryBind_ByName_NotFound_ReturnsSelf()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        // 不存在的变量名，静默返回自己
        var curry = runner.Curry(formula).TryBind("z", 99f);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.That(curry.BoundCount, Is.EqualTo(0));
    }

    [Test]
    public void TryBind_ByName_AlreadyBound_ReturnsSelf()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + [y]"));

        var curry = runner.Curry(formula).Bind("x", 5f);
        // 重复绑定同一变量，静默返回
        var unchanged = curry.TryBind("x", 99f);
        Assert.That(unchanged.BoundCount, Is.EqualTo(1));
        // 值未被覆盖
        var complete = unchanged.TryBind("y", 3f);
        Assert.That(complete.Result, Is.EqualTo(8f).Within(1e-6f), "5+3=8, x 应保持 5");
    }

    [Test]
    public void TryBind_Params_SingleVariable_ReturnsCorrectResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + 1"));

        var curry = runner.Curry(formula).TryBind(3f);
        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(4f).Within(1e-6f));
    }

    [Test]
    public void TryBind_Params_MultipleVariables_ReturnsCorrectResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        var curry = runner.Curry(formula).TryBind(2f, 3f, 4f);
        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f), "2*3+4=10");
    }

    [Test]
    public void TryBind_Params_ExcessValues_Clamped()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x]"));

        // 只有 1 个变量，bind 5 个值——多余的被忽略
        var curry = runner.Curry(formula).TryBind(1f, 2f, 3f, 4f, 5f);
        Assert.That(curry.Result, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void TryBind_ByName_ThenTryBindParams_MixedOrder()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));

        var curry = runner.Curry(formula)
            .TryBind("c", 4f)
            .TryBind(2f, 3f); // 顺序填充 a, b

        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(curry.Result, Is.EqualTo(10f).Within(1e-6f));
    }

    [Test]
    public void TryBind_Forking_ProducesIndependentResults()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b]"));

        var base_ = runner.Curry(formula).TryBind("a", 3f);

        var branch1 = base_.TryBind("b", 4f);
        Assert.That(branch1.Result, Is.EqualTo(12f).Within(1e-6f));

        var branch2 = base_.TryBind("b", 7f);
        Assert.That(branch2.Result, Is.EqualTo(21f).Within(1e-6f));

        // base_ 不受影响
        Assert.That(base_.BoundCount, Is.EqualTo(1));
    }

    // ── Record 序列化往返 ──

    [Test]
    public void ToRecord_ZeroVariable_RoundTrip_Completes()
    {
        var lexer = CreateMathLexer();
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("1 + 2"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var original = runner.Curry(formula);
        Assert.That(original.VariableCount, Is.EqualTo(0));
        Assert.That(original.IsCompleted, Is.True);
        Assert.That(original.Result, Is.EqualTo(3f).Within(1e-6f));

        byte[] record = original.ToRecord();
        var restored = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def);

        Assert.That(restored.VariableCount, Is.EqualTo(0));
        Assert.That(restored.IsCompleted, Is.True);
        Assert.That(restored.Result, Is.EqualTo(3f).Within(1e-6f));
    }

    [Test]
    public void ToRecord_HalfBound_RoundTrip_ResumesCorrectly()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        // 绑定前两个变量后挂起
        var original = runner.Curry(formula).Bind(2f, 3f); // a=2, b=3, 等 c
        Assert.That(original.IsCompleted, Is.False);
        Assert.That(original.BoundCount, Is.EqualTo(2));

        // 序列化 → 反序列化
        byte[] record = original.ToRecord();
        var restored = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def);

        Assert.That(restored.IsCompleted, Is.False);
        Assert.That(restored.BoundCount, Is.EqualTo(2));
        Assert.That(restored.VariableCount, Is.EqualTo(3));

        // 继续绑定剩余变量
        var completed = restored.Bind(4f); // c=4
        Assert.That(completed.IsCompleted, Is.True);
        Assert.That(completed.Result, Is.EqualTo(10f).Within(1e-6f), "2*3+4 = 10");
    }

    [Test]
    public void ToRecord_FullyCompleted_RoundTrip_PreservesResult()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] * [y]"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var original = runner.Curry(formula).Bind(7f, 3f);
        Assert.That(original.IsCompleted, Is.True);
        Assert.That(original.Result, Is.EqualTo(21f).Within(1e-6f));

        byte[] record = original.ToRecord();
        var restored = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def);

        Assert.That(restored.IsCompleted, Is.True);
        Assert.That(restored.Result, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void ToRecord_AfterFork_EachBranchSerializesIndependently()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b]"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var base_ = runner.Curry(formula).Bind(3f); // a=3

        // 分叉序列化各自独立
        byte[] rec1 = base_.Bind(4f).ToRecord();
        byte[] rec2 = base_.Bind(7f).ToRecord();

        var restored1 = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(rec1, Def);
        var restored2 = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(rec2, Def);

        Assert.That(restored1.Result, Is.EqualTo(12f).Within(1e-6f));
        Assert.That(restored2.Result, Is.EqualTo(21f).Within(1e-6f));
    }

    [Test]
    public void FromRecord_CacheMiss_ThrowsInvalidOperationException()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[x] + 1"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var original = runner.Curry(formula).Bind(5f);
        byte[] record = original.ToRecord();

        // 清空缓存模拟 cache miss
        FormulaCache.Reset();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def));
        Assert.That(ex.Message, Does.Contain("FormulaCache"));
    }

    [Test]
    public void FromRecord_MagicMismatch_ThrowsFormatException()
    {
        var lexer = CreateMathLexer();
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("1 + 2"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var original = runner.Curry(formula);
        byte[] record = original.ToRecord();

        // 损坏 magic 字节
        record[0] = 0xFF;

        Assert.Throws<FormatException>(() =>
            FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def));
    }

    [Test]
    public void FromRecord_VersionMismatch_ThrowsFormatException()
    {
        var lexer = CreateMathLexer();
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("1 + 2"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        var original = runner.Curry(formula);
        byte[] record = original.ToRecord();

        // 损坏 version 字节
        record[4] = 0xFF;

        Assert.Throws<FormatException>(() =>
            FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def));
    }

    [Test]
    public void ToRecord_JitInterpreterCrossBackend_PreservesConsistency()
    {
        var lexer = CreateVarLexer("[", "]");
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        var formula = runner.Compile(lexer.Lex("[a] * [b] + [c]"));
        FormulaCache.Instance.PutBytes(formula.GetByteHash(), formula.ToBytes());

        // 发送方：解释器路径（curry 总是解释器）
        var curry = runner.Curry(formula).Bind("a", 3f).Bind("b", 5f);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.That(curry.BoundCount, Is.EqualTo(2));

        byte[] record = curry.ToRecord();

        // 接收方：从 Record 恢复，继续用解释器完成
        var restored = FluxCurryEvaluator<float, FloatMathDef>.FromRecord(record, Def);
        var completed = restored.Bind("c", 2f);
        float interpretResult = completed.Result;

        // 接收方：直接用 JIT 求完整的 [3] * [5] + [2]
        var jitInstance = runner.Instantiate(formula, jit: true);
        jitInstance.Set("a", 3f).Set("b", 5f).Set("c", 2f);
        float jitResult = jitInstance.Run();

        Assert.That(interpretResult, Is.EqualTo(jitResult).Within(1e-6f),
            $"Record-restored result ({interpretResult}) should match JIT result ({jitResult})");
    }

    // ── Error 寄存器短路 ──

    private enum ErrorTestOp : byte { Const, Boom, Return }

    private readonly struct ErrorTestDef : IFluxExprDefinition<float>
    {
        public byte GetReturnOp() => (byte)ErrorTestOp.Return;
        public int GetArity(byte op) => ((ErrorTestOp)op) switch { ErrorTestOp.Boom => 1, _ => 0 };
        public OpType GetKind(byte op) => ((ErrorTestOp)op) switch
        {
            ErrorTestOp.Const => OpType.Immediate,
            ErrorTestOp.Return => OpType.Return,
            _ => OpType.Instruction,
        };
        public int GetPrecedence(byte op) => 0;
        public OpPair GetPair(byte op) => default;
        public Associativity GetAssociativity(byte op) => Associativity.Left;
        public byte ResolveToken(byte oper, TokenContext context) => oper;

        public float Compute(byte op, Instruction inst, Span<float> regs)
        {
            if ((ErrorTestOp)op == ErrorTestOp.Boom)
            {
                regs[Registers.Error] = float.NaN;
                return float.NaN;
            }
            return default;
        }

        public System.Linq.Expressions.Expression GetExpression(byte op, Instruction inst,
            System.Linq.Expressions.ParameterExpression[] regs)
        {
            throw new NotSupportedException();
        }
    }

    [Test]
    public void Resume_ErrorRegister_CompletesWithErrorValue()
    {
        var runner = new FluxAssembler<float, ErrorTestDef>(default);
        // 手动构造: 第一个 Const 是变量占位，Boom 写入 Error 寄存器触发短路
        var tokens = new FluxToken<float>[]
        {
            new() { Oper = (byte)ErrorTestOp.Const, Data = 0f },
            new() { Oper = (byte)ErrorTestOp.Boom },
        };
        var formula = runner.Compile(tokens).ToModifier().ToFormula("x");

        var curry = runner.Curry(formula);
        Assert.That(curry.IsCompleted, Is.False);
        Assert.That(curry.VariableCount, Is.EqualTo(1));

        // 绑定变量后 Resume 遇到 Boom → Error 寄存器非 default → 完成
        curry = curry.Bind(42f);
        Assert.That(curry.IsCompleted, Is.True);
        Assert.That(float.IsNaN(curry.Result), Is.True);
    }
}
