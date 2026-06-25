using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if FLUX_FAST_EXPRESSION_COMPILER
using FastExpressionCompiler;
#endif
using FluxFormula.Compiler;
using FluxFormula.Core;
using NUnit.Framework;

// ============================================================
// FEC × Explicit struct 兼容性验证
// 核心问题：Mono 运行时下 Expression.Field + [StructLayout(LayoutKind.Explicit)]
// → Expression.Compile() 抛 InvalidOperationException
// → CompileFast() 用自己的 IL emitter 绕过 Mono 的实现
// ============================================================

/// <summary>
/// 模拟 Unity 诊断报告中 <c>Damage</c> 的布局——显式偏移，8 字节。
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct ExplicitData
{
    [FieldOffset(0)]
    public readonly float Amount;

    [FieldOffset(4)]
    public readonly int Element;

    public ExplicitData(float amount, int element)
    {
        Amount  = amount;
        Element = element;
    }

    public override readonly string ToString() => $"{Amount}|{Element}";
}

public enum ExpOp : byte
{
    Const, Add, Mul, Return,
}

public readonly struct ExplicitDef : IFluxJITDefinition<ExplicitData>
{
    private static readonly ConstructorInfo s_ctor = typeof(ExplicitData)
        .GetConstructor(new[] { typeof(float), typeof(int) });

    private static readonly FieldInfo s_amount  = typeof(ExplicitData).GetField(nameof(ExplicitData.Amount));
    private static readonly FieldInfo s_element = typeof(ExplicitData).GetField(nameof(ExplicitData.Element));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetReturnOp() => (byte)ExpOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => (ExpOp)op switch { ExpOp.Add => 2, ExpOp.Mul => 2, _ => 0 };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpType GetKind(byte op) => (ExpOp)op switch
    {
        ExpOp.Const  => OpType.Immediate,
        ExpOp.Return => OpType.Return,
        _            => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op) => (ExpOp)op switch { ExpOp.Mul => 2, _ => 1 };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op) => new() { PairRole = Pair.None };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op) => Associativity.Left;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ExplicitData Compute(byte op, Instruction inst, ReadOnlySpan<ExplicitData> regs)
    {
        return (ExpOp)op switch
        {
            ExpOp.Add => new ExplicitData(regs[inst.Arg0].Amount + regs[inst.Arg1].Amount, 0),
            ExpOp.Mul => new ExplicitData(regs[inst.Arg0].Amount * regs[inst.Arg1].Amount, 0),
            _         => default,
        };
    }

    /// <summary>
    /// 使用 Expression.Field 访问显式布局结构体的字段——这是 Mono 抛出异常的确切位置。
    /// </summary>
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return (ExpOp)op switch
        {
            ExpOp.Add => Expression.New(s_ctor,
                Expression.Add(
                    Expression.Field(regs[inst.Arg0], s_amount),
                    Expression.Field(regs[inst.Arg1], s_amount)),
                Expression.Constant(0)),
            ExpOp.Mul => Expression.New(s_ctor,
                Expression.Multiply(
                    Expression.Field(regs[inst.Arg0], s_amount),
                    Expression.Field(regs[inst.Arg1], s_amount)),
                Expression.Constant(0)),
            _ => Expression.Constant(default(ExplicitData)),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ResolveToken(byte oper, TokenContext context) => 0;

    public string GetOperatorName(byte op) => ((ExpOp)op).ToString();
}

public class FecExplicitStructTests
{
    private static readonly ExplicitDef s_def = default;

    [SetUp]
    public void SetUp()
    {
        // 每个测试从干净状态开始：重置缓存和 JIT 状态
        FormulaCache.Reset();
        FluxPlatform.ResetJit();
    }

    // ═══════════════════════════════════════════════════════
    // Test 1: bare Expression.Field + Compile vs CompileFast
    // ═══════════════════════════════════════════════════════

    [Test]
    public void BareExpressionField_CompileFast_CompilesAndRuns()
    {
        var p0 = Expression.Variable(typeof(ExplicitData), "x");
        var field = typeof(ExplicitData).GetField(nameof(ExplicitData.Amount));
        var expr = Expression.Field(p0, field);
        var lambda = Expression.Lambda<Func<ExplicitData, float>>(expr, p0);

#if FLUX_FAST_EXPRESSION_COMPILER
        var compiled = lambda.CompileFast();
#else
        var compiled = lambda.Compile();
#endif

        Assert.That(compiled(new ExplicitData(3.5f, 99)), Is.EqualTo(3.5f));
    }

    [Test]
    public void BareExpressionField_StandardCompile_CompilesAndRuns()
    {
        var p0 = Expression.Variable(typeof(ExplicitData), "x");
        var field = typeof(ExplicitData).GetField(nameof(ExplicitData.Amount));
        var expr = Expression.Field(p0, field);
        var lambda = Expression.Lambda<Func<ExplicitData, float>>(expr, p0);
        var compiled = lambda.Compile();

        Assert.That(compiled(new ExplicitData(3.5f, 99)), Is.EqualTo(3.5f));
    }

    // ═══════════════════════════════════════════════════════
    // Test 2: Expression.New(ctor, args) with Explicit struct
    // ═══════════════════════════════════════════════════════

    [Test]
    public void ExpressionNew_ExplicitStruct_CompileFast_CompilesAndRuns()
    {
        var ctor = typeof(ExplicitData).GetConstructor(new[] { typeof(float), typeof(int) });
        var expr = Expression.New(ctor, Expression.Constant(4.5f), Expression.Constant(42));
        var lambda = Expression.Lambda<Func<ExplicitData>>(expr);

#if FLUX_FAST_EXPRESSION_COMPILER
        var compiled = lambda.CompileFast();
#else
        var compiled = lambda.Compile();
#endif

        var result = compiled();
        Assert.That(result.Amount,  Is.EqualTo(4.5f));
        Assert.That(result.Element, Is.EqualTo(42));
    }

    [Test]
    public void ExpressionNew_ExplicitStruct_StandardCompile_CompilesAndRuns()
    {
        var ctor = typeof(ExplicitData).GetConstructor(new[] { typeof(float), typeof(int) });
        var expr = Expression.New(ctor, Expression.Constant(4.5f), Expression.Constant(42));
        var lambda = Expression.Lambda<Func<ExplicitData>>(expr);
        var compiled = lambda.Compile();

        var result = compiled();
        Assert.That(result.Amount,  Is.EqualTo(4.5f));
        Assert.That(result.Element, Is.EqualTo(42));
    }

    // ═══════════════════════════════════════════════════════
    // Test 3: full JIT pipeline with explicit TData
    // ═══════════════════════════════════════════════════════

    [Test]
    public void JitPipeline_ExplicitTData_CompilesAndMatchesInterpreter()
    {
        // 中缀顺序: "[10.5] * [3] + [2]" → (10.5*3)+2 = 33.5
        // Mul precedence=2, Add precedence=1
        var tokens = new FluxToken<ExplicitData>[]
        {
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(10.5f, 0) },
            new() { Oper = (byte)ExpOp.Mul },
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(3.0f,  0) },
            new() { Oper = (byte)ExpOp.Add },
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(2.0f,  0) },
        };

        var assembler = new FluxAssembler<ExplicitData, ExplicitDef>(s_def);

        // 解释器（基准）
        var formula = assembler.Compile(tokens);
        var interpResult = assembler.Instantiate(formula, jit: false).Run();
        Assert.That(interpResult.Amount, Is.EqualTo(33.5f).Within(1e-5f));

        // JIT — FEC 应成功编译 explicit struct 的 Expression.Field
        var jitResult = assembler.Instantiate(formula, jit: true).Run();
        Assert.That(jitResult.Amount, Is.EqualTo(33.5f).Within(1e-5f));
    }

    // ═══════════════════════════════════════════════════════
    // Test 4: JIT delegate is cached (验证 PutDelegate)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void JitPipeline_CachesDelegate_SecondRunUsesCache()
    {
        Assert.That(FluxPlatform.IsJitDisabled, Is.False, "JIT should be enabled after SetUp");

        string jitError = null;
        FluxPlatform.OnJitDisabled += msg => jitError = msg;

        // 中缀: "[7] + [3]"
        var tokens = new FluxToken<ExplicitData>[]
        {
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(7f, 0) },
            new() { Oper = (byte)ExpOp.Add },
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(3f, 0) },
        };

        var assembler = new FluxAssembler<ExplicitData, ExplicitDef>(s_def);
        var formula = assembler.Compile(tokens);

        var r1 = assembler.Instantiate(formula, jit: true).Run();
        Assert.That(r1.Amount, Is.EqualTo(10f).Within(1e-5f));

        if (FluxPlatform.IsJitDisabled)
        {
            Assert.Fail($"JIT was unexpectedly disabled. Error: {jitError ?? "(no message)"}");
        }

        int countAfterFirst = FormulaCache.Instance.Count;

        // JIT #2: 应命中 delegate cache，不新增条目
        var r2 = assembler.Instantiate(formula, jit: true).Run();
        Assert.That(r2.Amount, Is.EqualTo(r1.Amount).Within(1e-5f));
        Assert.That(FormulaCache.Instance.Count, Is.EqualTo(countAfterFirst),
            "Second JIT should hit cache, not create new entries");
    }

    // ═══════════════════════════════════════════════════════
    // Test 5: bytecode is cached on JIT success (验证 PutBytes)
    // ═══════════════════════════════════════════════════════

    [Test]
    public void JitPipeline_CachesBytecode_ResolveBytecodeSpanHits()
    {
        // 中缀: "[5] * [2]"
        var tokens = new FluxToken<ExplicitData>[]
        {
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(5f, 0) },
            new() { Oper = (byte)ExpOp.Mul },
            new() { Oper = (byte)ExpOp.Const, Data = new ExplicitData(2f, 0) },
        };

        var assembler = new FluxAssembler<ExplicitData, ExplicitDef>(s_def);
        var formula = assembler.Compile(tokens);
        var hash = formula.GetByteHash();

        // 缓存初始为空
        Assert.That(FormulaCache.Instance.TryGet(hash, out _, out _), Is.False);

        // JIT 成功后应同时缓存字节码
        assembler.Instantiate(formula, jit: true).Run();

        Assert.That(FormulaCache.Instance.TryGet(hash, out _, out int len), Is.True,
            "Bytecode should be cached after successful JIT compilation");
        Assert.That(len, Is.GreaterThan(0));
    }
}
