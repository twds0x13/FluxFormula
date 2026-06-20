using System;
using FluxFormula.Core;
using NUnit.Framework;
using static TestHelper;

public unsafe class ConnectCacheTests
{
    [SetUp]
    public void SetUp()
    {
        ConnectCache.Reset();
    }

    // ═══════════════════════════════════════════════════════
    // 链式 Connect 基本行为
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Connect_TwoAtomicFormulas_CreatesChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "10 + 5");
        var fB = Compile(lexer, "2 * 3");

        var result = fA.Connect(fB);

        // 短链（2 links ≤ 8）应为链式
        Assert.That(result.IsChained, Is.True,
            "两个原子公式 Connect 应产生链式公式");
        Assert.That(result.ChainLength, Is.EqualTo(2));
    }

    [Test]
    public void Connect_AlwaysCreatesChains_RegardlessOfLength()
    {
        var lexer = CreateMathLexer();
        var formulas = new FluxFormula<float, FloatOp>[ChainReserved.MergeThreshold + 2];
        for (int i = 0; i < formulas.Length; i++)
            formulas[i] = Compile(lexer, $"{i} + {i + 1}");

        // Connect 始终产链，不再自动合并——合并决策在 Instantiate
        var current = formulas[0];
        for (int i = 1; i < formulas.Length; i++)
            current = current.Connect(formulas[i]);

        // 10 个链接的链
        Assert.That(current.IsChained, Is.True);
        Assert.That(current.ChainLength, Is.EqualTo(formulas.Length));

        // Instantiate 时自动合并长链（interpreter 路径 > 8 触发 ToAtomic）
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst = runner.Instantiate(current);
        Assert.That(inst.Run(), Is.Not.EqualTo(0f));
    }

    [Test]
    public void Connect_ChainPlusAtomic_ExtendsChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4");
        var fC = Compile(lexer, "5 + 6");

        var chain = fA.Connect(fB); // 链长 2
        var longer = chain.Connect(fC); // 链长 3

        Assert.That(longer.IsChained, Is.True);
        Assert.That(longer.ChainLength, Is.EqualTo(3));
    }

    [Test]
    public void Connect_DifferentOrder_ProducesDifferentChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "10 + 5");
        var fB = Compile(lexer, "2 * 3");

        var chainAB = fA.Connect(fB);
        var chainBA = fB.Connect(fA);

        // 不同顺序应产生不同哈希
        Assert.That(chainAB.GetByteHash(), Is.Not.EqualTo(chainBA.GetByteHash()),
            "Connect(A, B) 和 Connect(B, A) 应产生不同标识");
    }

    // ═══════════════════════════════════════════════════════
    // ToAtomic vs per-link 语义一致性（解决 semantic gap）
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToAtomic_And_ChainInterpreter_ProduceSameResult_FormulaChain()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "10 + 5");
        var fB = Compile(lexer, "2 * 3");

        var chain = fA.Connect(fB);
        Assert.That(chain.IsChained, Is.True);

        // 两条路径应产出一致结果
        float atomicResult = EvalFormula(chain.ToAtomic());
        float chainResult  = EvalFormula(chain); // goes through RunChainInterpreter

        Assert.That(atomicResult, Is.EqualTo(chainResult).Within(1e-6f),
            "ToAtomic 和 per-link 解释器求值对 Formula 链应一致");
    }

    [Test]
    public void ToAtomic_And_ChainInterpreter_ProduceSameResult_WithModifier()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "7 + 3");
        var fB = Compile(lexer, "2 * 2");

        // 显式 ToMultiplier: B 消费 A 的输出
        var chain = fA.Connect(fB.ToMultiplier());
        Assert.That(chain.IsChained, Is.True);

        // 两条路径应产出一致结果：B(A 的输出) = (7+3) * 2 = 20
        float atomicResult = EvalFormula(chain.ToAtomic());
        float chainResult  = EvalFormula(chain);

        Assert.That(atomicResult, Is.EqualTo(20f).Within(1e-6f));
        Assert.That(chainResult,  Is.EqualTo(20f).Within(1e-6f));
        Assert.That(atomicResult, Is.EqualTo(chainResult).Within(1e-6f),
            "ToAtomic 和 per-link 解释器对 Modifier 链应一致");
    }

    [Test]
    public void ToAtomic_And_ChainInterpreter_ProduceSameResult_LongChain()
    {
        var lexer = CreateMathLexer();
        var fBase = Compile(lexer, "1 + 2"); // = 3

        // 构建 5-link 链：每个 link 是乘 3 的 modifier（f="2 * 3"→ToMultiplier = R1*3）
        var current = fBase;
        for (int i = 0; i < 4; i++)
            current = current.Connect(Compile(lexer, "2 * 3").ToMultiplier());
        // 语义：(((3) * 3) * 3) * 3) * 3 = 3 * 81 = 243

        float perLinkResult = EvalFormula(current);
        float atomicResult  = EvalFormula(current.ToAtomic());

        Assert.That(perLinkResult, Is.EqualTo(243f).Within(1e-6f));
        Assert.That(atomicResult,  Is.EqualTo(243f).Within(1e-6f));
        Assert.That(perLinkResult, Is.EqualTo(atomicResult).Within(1e-6f),
            "长链 per-link 和 ToAtomic 结果应一致");
    }

    // ═══════════════════════════════════════════════════════
    // 链式公式 → 原子转换
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ToAtomic_ReturnsEquivalentFormula()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "7 + 3");
        var fB = Compile(lexer, "5 * 2");

        var chain = fA.Connect(fB);
        Assert.That(chain.IsChained, Is.True);

        var atomic = chain.ToAtomic();
        Assert.That(atomic.IsChained, Is.False);

        // 两种表示求值相同
        Assert.That(EvalFormula(atomic), Is.EqualTo(EvalFormula(chain)).Within(1e-6f));
    }

    [Test]
    public void ToAtomic_SingleLink_ReturnsEquivalent()
    {
        var lexer = CreateMathLexer();
        var f = Compile(lexer, "42");

        var link = FluxFormula<float, FloatOp>.Empty.Connect(f);
        // Empty.Connect(f) returns f directly (Count zero check) → f is atomic
        // Force a chain: Connect two non-empty formulas
        var fA = Compile(lexer, "10");
        var chain = fA.Connect(Compile(lexer, "5"));
        var atomic = chain.ToAtomic();

        Assert.That(EvalFormula(atomic), Is.EqualTo(EvalFormula(chain)).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // GetByteHash 链式 vs 原子
    // ═══════════════════════════════════════════════════════

    [Test]
    public void GetByteHash_SameFormula_ReturnsSameHash()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "3.14 * 2");
        var fB = Compile(lexer, "3.14 * 2");

        Assert.That(fA.GetByteHash(), Is.EqualTo(fB.GetByteHash()),
            "编译相同表达式的两个公式应产生相同哈希");
    }

    [Test]
    public void GetByteHash_DifferentFormula_ReturnsDifferentHash()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4");

        Assert.That(fA.GetByteHash(), Is.Not.EqualTo(fB.GetByteHash()));
    }

    [Test]
    public void GetByteHash_EmptyFormula_ReturnsNonZero()
    {
        var empty = FluxFormula<float, FloatOp>.Empty;
        var h = empty.GetByteHash();
        Assert.That(h.XxHash64, Is.Not.EqualTo(0UL));
        Assert.That(h.FnvHash64, Is.Not.EqualTo(0UL));
    }

    [Test]
    public void GetByteHash_Chain_DifferentFromAtomic()
    {
        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "1 + 2");
        var fB = Compile(lexer, "3 + 4");

        var chain = fA.Connect(fB);
        var atomic = chain.ToAtomic();

        // 链式公式和对应原子公式的哈希应不同（表示形式不同）
        Assert.That(chain.GetByteHash(), Is.Not.EqualTo(atomic.GetByteHash()),
            "链式表示和原子表示的哈希应可区分");
    }

    // ═══════════════════════════════════════════════════════
    // Delegate 缓存验证
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Instantiate_CachesJitDelegate()
    {
        ConnectCache.Reset();

        var lexer = CreateMathLexer();
        var f = Compile(lexer, "2 + 3");

        // 首次 JIT Instantiate → 应触发编译并缓存 delegate
        var runner1 = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst1 = runner1.Instantiate(f, jit: true);
        var result1 = inst1.Run();

        // 第二次 JIT Instantiate → 应从缓存中取 delegate
        var runner2 = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst2 = runner2.Instantiate(f, jit: true);
        var result2 = inst2.Run();

        Assert.That(result2, Is.EqualTo(result1).Within(1e-6f),
            "缓存 delegate 的求值结果应与首次 JIT 编译一致");
    }

    [Test]
    public void Instantiate_ChainFormula_UsesJitCache()
    {
        ConnectCache.Reset();

        var lexer = CreateMathLexer();
        var fA = Compile(lexer, "6 + 4");
        var fB = Compile(lexer, "3 * 2");

        var chain = fA.Connect(fB);
        Assert.That(chain.IsChained, Is.True);

        // 链式公式的 JIT Instantiate → ToAtomic + JIT compile + cache
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var inst1 = runner.Instantiate(chain, jit: true);
        var r1 = inst1.Run();

        // 第二次 → 应命中 delegate 缓存
        var inst2 = runner.Instantiate(chain, jit: true);
        var r2 = inst2.Run();

        Assert.That(r2, Is.EqualTo(r1).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // 与变量公式的 Connect
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Connect_WithVariables_PreservesSlotsInChain()
    {
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        var lex   = CreateVarLexer("[", "]");
        var fA    = runner.Compile(lex.Lex("[x] + [y]"));
        var fB    = runner.Compile(lex.Lex("[z]"));

        // Connect: fA = x+y, fB = z
        var chain = fA.Connect(fB);
        Assert.That(chain.IsChained, Is.True);

        // 变量槽合并验证：fA 有 [x, y]，fB 有 [z]
        var atomic = chain.ToAtomic();
        Assert.That(atomic.VariableSlots.Length, Is.EqualTo(3));

        // Set 三个变量后求值
        var inst = runner.Instantiate(atomic).Set("x", 4f).Set("y", 6f).Set("z", 2f);
        // Connect(A,B) 返回 B=z（不消费 A 的输出）
        Assert.That(inst.Run(), Is.EqualTo(2f).Within(1e-6f));
    }

    // ═══════════════════════════════════════════════════════
    // ConnectCache buffer 重置
    // ═══════════════════════════════════════════════════════

    [Test]
    public void BufferFull_ResetsAndClearsBytecodeCache()
    {
        ConnectCache.Reset();

        var data1 = new byte[600_000];
        new Random(42).NextBytes(data1);

        var key1 = DualHash64.Compute(data1);
        ConnectCache.Put(key1, data1);
        Assert.That(ConnectCache.TryGet(key1, out _, out _), Is.True);
        long used1 = ConnectCache.BufferUsed;

        // 再写 500K → 600K + 500K > 1MB → 满重置
        var data2 = new byte[500_000];
        new Random(99).NextBytes(data2);

        var key2 = DualHash64.Compute(data2);
        ConnectCache.Put(key2, data2);

        Assert.That(ConnectCache.TryGet(key2, out _, out _), Is.True);
        Assert.That(ConnectCache.BufferUsed, Is.LessThan(used1));
        Assert.That(ConnectCache.TryGet(key1, out _, out _), Is.False);
    }

    // ═══════════════════════════════════════════════════════
    // 自定义 IFluxCacheProvider 注入
    // ═══════════════════════════════════════════════════════

    private class TestCache : IFluxCacheProvider
    {
        private readonly System.Collections.Generic.Dictionary<string, (IntPtr, int)> _dict = new();
        private readonly System.Collections.Generic.Dictionary<string, IntPtr> _delegateDict = new();

        public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
        {
            string k = key.ToString();
            if (_dict.TryGetValue(k, out var v))
            {
                ptr = v.Item1; length = v.Item2; return true;
            }
            ptr = IntPtr.Zero; length = 0; return false;
        }

        public void Put(DualHash64 key, IntPtr ptr, int length)
            => _dict[key.ToString()] = (ptr, length);

        public bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
            => _delegateDict.TryGetValue(key.ToString(), out gcHandle);

        public void PutDelegate(DualHash64 key, IntPtr gcHandle)
            => _delegateDict[key.ToString()] = gcHandle;
    }

    [Test]
    public void CustomCacheProvider_WorksWithFormulaInstantiate()
    {
        var original = ConnectCache.Cache;
        var custom   = new TestCache();

        try
        {
            ConnectCache.Cache = custom;
            ConnectCache.Reset();

            var lexer = CreateMathLexer();
            var f = Compile(lexer, "5 + 5");

            var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
            var r1 = runner.Instantiate(f, jit: true).Run();

            // 第二次 — 通过自定义缓存取 delegate
            ConnectCache.Reset();
            ConnectCache.Cache = custom;
            var r2 = runner.Instantiate(f, jit: true).Run();

            Assert.That(r2, Is.EqualTo(r1).Within(1e-6f));
        }
        finally
        {
            ConnectCache.Cache = original;
            ConnectCache.Reset();
        }
    }

    // ═══════════════════════════════════════════════════════
    // CHAIN_LINK_INTERNAL 前缀告警
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ChainReserved_HasNonEmptyPrefix()
    {
        Assert.That(ChainReserved.InternalPrefix, Is.Not.Empty);
        Assert.That(ChainReserved.InternalPrefix, Does.Contain("INTERNAL"));
    }

    [Test]
    public void ChainReserved_MergeThreshold_IsReasonable()
    {
        Assert.That(ChainReserved.MergeThreshold, Is.GreaterThan(1));
        Assert.That(ChainReserved.MergeThreshold, Is.LessThan(64));
    }

    // ═══════════════════════════════════════════════════════
    // 辅助
    // ═══════════════════════════════════════════════════════

    private static FluxFormula<float, FloatOp> Compile(
        FluxLexer<float, FloatOp> lexer, string expr)
    {
        return new FluxAssembler<float, FloatOp, FloatMathDef>(Def)
            .Compile(lexer.Lex(expr));
    }
}
