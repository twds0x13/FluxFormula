using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FluxFormula.Compiler;
using FluxFormula.Core;
using static TestHelper;

namespace FluxFormula.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromTypes(new[]
            {
                typeof(LexerBenchmarks),
                typeof(CompileBenchmarks),
                typeof(InterpreterBenchmarks),
                typeof(JitBenchmarks),
                typeof(InjectionBenchmarks),
                typeof(CacheBenchmarks),
            }).Run(args);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Lexer（一次性 setup 操作，直接测）
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class LexerBenchmarks
    {
        private FluxLexer<float> _mathLexer;
        private FluxLexer<float> _varLexer;
        private FluxLexer<float> _implicitMulLexer;

        [GlobalSetup]
        public void Setup()
        {
            _mathLexer       = CreateMathLexer();
            _varLexer        = CreateVarLexer("[", "]");
            _implicitMulLexer = CreateImplicitMulLexer();
        }

        [Benchmark] public void Simple()          => _mathLexer.Lex("1 + 2 * 3");
        [Benchmark] public void Complex()         => _mathLexer.Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3");
        [Benchmark] public void WithVariables()   => _varLexer.Lex("[atk] * (1 + [crit_rate]) - [target_def]");
        [Benchmark] public void ImplicitMul()     => _implicitMulLexer.Lex("2(3+4) + (1+2)(3+4)");
        [Benchmark] public void ManyTokens()      => _mathLexer.Lex("1+2+3+4+5+6+7+8+9+10+11+12+13+14+15");
    }

    // ═══════════════════════════════════════════════════════════════
    // 编译（一次性 setup 操作，直接测）
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class CompileBenchmarks
    {
        private FluxToken<float>[] _simple;
        private FluxToken<float>[] _complex;
        private LexResult<float> _withVars;
        private FloatMathDef _def;

        [GlobalSetup]
        public void Setup()
        {
            _def      = Def;
            _simple   = CreateMathLexer().Lex("1 + 2 * 3").Tokens;
            _complex  = CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3").Tokens;
            _withVars = CreateVarLexer("[", "]").Lex("[a] * [b] + [c] - [d]");
        }

        [Benchmark]
        public FluxFormula<float, FloatMathDef> Simple()
        {
            var a = new FluxAssembler<float, FloatMathDef>(_def);
            return a.Compile(_simple);
        }

        [Benchmark]
        public FluxFormula<float, FloatMathDef> Complex()
        {
            var a = new FluxAssembler<float, FloatMathDef>(_def);
            return a.Compile(_complex);
        }

        [Benchmark]
        public FluxFormula<float, FloatMathDef> WithVariables()
        {
            var a = new FluxAssembler<float, FloatMathDef>(_def);
            return a.Compile(_withVars);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 解释器热路径：纯 Compute()——buffer 在 Setup 中预分配并注入
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class InterpreterBenchmarks
    {
        private FloatMathDef _def;
        private Instruction[] _simpleBuf;
        private Instruction[] _complexBuf;
        private int _simpleCount;
        private int _complexCount;

        [GlobalSetup]
        public void Setup()
        {
            _def = Def;
            var a = new FluxAssembler<float, FloatMathDef>(_def);

            // 编译 + 实例化 + 注入 → 全部在 Setup 中完成
            var fSimple  = a.Compile(CreateMathLexer().Lex("1 + 2 * 3").Tokens);
            var fComplex = a.Compile(CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3").Tokens);

            var instS = a.Instantiate(fSimple, jit: false);
            var instC = a.Instantiate(fComplex, jit: false);

            // 捕获已注入的 buffer（Instruction[] 是堆对象，生存期不受 ref struct 限制）
            _simpleBuf   = instS.GetBuffer();
            _complexBuf  = instC.GetBuffer();
            _simpleCount = fSimple.Count;
            _complexCount = fComplex.Count;
        }

        [Benchmark(Baseline = true)]
        public float Simple()
        {
            var eval = new FluxEvaluator<float, FloatMathDef>(_def);
            return eval.Compute(_simpleBuf.AsSpan(0, _simpleCount));
        }

        [Benchmark]
        public float Complex()
        {
            var eval = new FluxEvaluator<float, FloatMathDef>(_def);
            return eval.Compute(_complexBuf.AsSpan(0, _complexCount));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // JIT 热路径：纯委托调用——编译 + 注入在 Setup 中完成
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class JitBenchmarks
    {
        private CompiledFunc<float> _jitSimple;
        private CompiledFunc<float> _jitComplex;
        private Instruction[] _simplePayload;
        private Instruction[] _complexPayload;

        [GlobalSetup]
        public void Setup()
        {
            var a = new FluxAssembler<float, FloatMathDef>(Def);

            var fSimple  = a.Compile(CreateMathLexer().Lex("1 + 2 * 3").Tokens);
            var fComplex = a.Compile(CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3").Tokens);

            _jitSimple  = FluxExprCompiler<float, FloatMathDef>.Compile(
                fSimple.Raw(), Def, out _simplePayload, pruneRegisters: true);
            _jitComplex = FluxExprCompiler<float, FloatMathDef>.Compile(
                fComplex.Raw(), Def, out _complexPayload, pruneRegisters: true);
        }

        [Benchmark(Baseline = true)]
        public float Simple() => _jitSimple(_simplePayload);

        [Benchmark]
        public float Complex() => _jitComplex(_complexPayload);
    }

    // ═══════════════════════════════════════════════════════════════
    // 注入 + Run：解释器 vs JIT，SetByIndex vs SetByName
    // JIT 委托预编译；每轮 copy payload + 注入 + Run
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class InjectionBenchmarks
    {
        private FloatMathDef _def;
        private FluxFormula<float, FloatMathDef> _formula;

        // JIT 预编译缓存
        private CompiledFunc<float> _jitFunc;
        private Instruction[] _jitPayloadTemplate;
        private int _dataSlots;

        [GlobalSetup]
        public void Setup()
        {
            _def     = Def;
            var a    = new FluxAssembler<float, FloatMathDef>(_def);
            _formula = a.Compile(CreateVarLexer("[", "]").Lex("[a] + [b] * [c]"));

            // JIT 编译只发生一次
            _jitFunc = FluxExprCompiler<float, FloatMathDef>.Compile(
                _formula.Raw(), _def, out _jitPayloadTemplate, pruneRegisters: true);

            unsafe { _dataSlots = (sizeof(float) + sizeof(Instruction) - 1) / sizeof(Instruction); }
        }

        // ── 解释器 ──────────────────────────────

        [Benchmark(Baseline = true)]
        public float Interp_SetByIndex()
        {
            var a    = new FluxAssembler<float, FloatMathDef>(_def);
            var inst = a.Instantiate(_formula, jit: false);
            return inst.SetIndex(0, 10f).SetIndex(1, 30f).SetIndex(2, 2f).Run();
        }

        [Benchmark]
        public float Interp_SetByName()
        {
            var a    = new FluxAssembler<float, FloatMathDef>(_def);
            var inst = a.Instantiate(_formula, jit: false);
            return inst.Set("a", 10f).Set("b", 30f).Set("c", 2f).Run();
        }

        // ── JIT（委托已预编译，仅测注入 + 调用）──

        [Benchmark]
        public float Jit_SetByIndex()
        {
            // Copy payload（Set 会覆写，不能污染模板）
            var payload = new Instruction[_jitPayloadTemplate.Length];
            Array.Copy(_jitPayloadTemplate, payload, _jitPayloadTemplate.Length);

            // 模拟 Instantiate(jit:true) 的 injector 构建（不含 Expression.Compile）
            var injector = new FluxInjector<float>(payload, null, _formula.VariableSlots);
            injector.SetIndex(0, 10f).SetIndex(1, 30f).SetIndex(2, 2f);
            return _jitFunc(injector.GetBuffer());
        }

        [Benchmark]
        public float Jit_SetByName()
        {
            var payload = new Instruction[_jitPayloadTemplate.Length];
            Array.Copy(_jitPayloadTemplate, payload, _jitPayloadTemplate.Length);

            var injector = new FluxInjector<float>(payload, null, _formula.VariableSlots);
            injector.Set("a", 10f).Set("b", 30f).Set("c", 2f);
            return _jitFunc(injector.GetBuffer());
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 编译缓存管线：冷启动 vs 缓存命中（JIT + 解释器）
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class CacheBenchmarks
    {
        private FloatMathDef _def;
        private FluxFormula<float, FloatMathDef> _fSimple;
        private FluxFormula<float, FloatMathDef> _fComplex;
        private FluxChain<float, FloatMathDef> _fChain;

        [GlobalSetup]
        public void Setup()
        {
            _def = Def;
            var a = new FluxAssembler<float, FloatMathDef>(_def);
            _fSimple  = a.Compile(CreateMathLexer().Lex("1 + 2 * 3").Tokens);
            _fComplex = a.Compile(CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3").Tokens);

            var varLexer = CreateVarLexer("[", "]");
            var fA = a.Compile(varLexer.Lex("[atk] * 1.5").Tokens, new[] { "atk" });
            var fB = a.Compile(varLexer.Lex("[def] * 0.3").Tokens, new[] { "def" });
            _fChain  = fA.Connect(fB.ToModifier());
        }

        private FluxAssembler<float, FloatMathDef> A() => new(_def);

        // ── JIT 冷启动：首次编译 + 缓存写入 + 求值 ──

        [IterationSetup(Targets = new[] {
            nameof(JitColdSimple), nameof(JitColdComplex), nameof(JitColdChain)
        })]
        public void ResetCache() => FormulaCache.Reset();

        [Benchmark(Baseline = true)]
        public float JitColdSimple()
        {
            var inst = A().Instantiate(_fSimple, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float JitColdComplex()
        {
            var inst = A().Instantiate(_fComplex, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float JitColdChain()
        {
            var chainInst = A().Instantiate(_fChain, jit: true);
            return chainInst.SetIndex(0, 100f).SetIndex(1, 50f).Run();
        }

        // ── JIT 缓存命中：delegate 已在缓存中 → 直接复用 ──

        [IterationSetup(Targets = new[] {
            nameof(JitWarmSimple), nameof(JitWarmComplex), nameof(JitWarmChain)
        })]
        public void PrimeJitCache()
        {
            FormulaCache.Reset();
            var a = A();
            a.Instantiate(_fSimple,  jit: true);
            a.Instantiate(_fComplex, jit: true);
            a.Instantiate(_fChain,   jit: true);
        }

        [Benchmark]
        public float JitWarmSimple()
        {
            var inst = A().Instantiate(_fSimple, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float JitWarmComplex()
        {
            var inst = A().Instantiate(_fComplex, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float JitWarmChain()
        {
            var chainInst = A().Instantiate(_fChain, jit: true);
            return chainInst.SetIndex(0, 100f).SetIndex(1, 50f).Run();
        }

        // ── 解释器冷/热（解释器不走 delegate 缓存，仅测 baseline）──

        [IterationSetup(Targets = new[] {
            nameof(InterpColdSimple), nameof(InterpColdComplex), nameof(InterpColdChain)
        })]
        public void ResetCacheForInterp() => FormulaCache.Reset();

        [Benchmark]
        public float InterpColdSimple()
        {
            var inst = A().Instantiate(_fSimple, jit: false);
            return inst.Run();
        }

        [Benchmark]
        public float InterpColdComplex()
        {
            var inst = A().Instantiate(_fComplex, jit: false);
            return inst.Run();
        }

        [Benchmark]
        public float InterpColdChain()
        {
            var chainInst = A().Instantiate(_fChain, jit: false);
            return chainInst.SetIndex(0, 100f).SetIndex(1, 50f).Run();
        }
    }
}
