using System;
using BenchmarkDotNet.Attributes;
using FluxFormula.Compiler;
using FluxFormula.Core;
using static TestHelper;

namespace FluxFormula.Benchmarks
{
    /// <summary>
    /// 编译吞吐量基准 —— 模拟游戏加载时批量编译上百条公式的场景。
    /// 不计入 Git（仅本地比对 LuaJIT 编译耗时）。
    /// </summary>

    // ═══════════════════════════════════════════════════════════════
    // 单条公式完整编译链：Lex → Compile → [JIT]
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class SingleFormulaCompileBenchmarks
    {
        private FloatMathDef _def;

        [GlobalSetup]
        public void Setup() => _def = Def;

        // ── Shunting-yard 编译（无 JIT）──

        [Benchmark(Baseline = true)]
        public FluxFormula<float, FloatOp> Simple_CompileOnly()
        {
            var tokens = CreateMathLexer().Lex("1 + 2 * 3").Tokens;
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            return a.Compile(tokens);
        }

        [Benchmark]
        public FluxFormula<float, FloatOp> Complex_CompileOnly()
        {
            var tokens = CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3 - 7 + 2 * (4+1)").Tokens;
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            return a.Compile(tokens);
        }

        [Benchmark]
        public FluxFormula<float, FloatOp> WithVars_CompileOnly()
        {
            var lexResult = CreateVarLexer("[", "]").Lex("[a] * [b] + [c] / ([d] - [e])");
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            return a.Compile(lexResult);
        }

        // ── 完整编译 + JIT ──

        [Benchmark]
        public float Simple_CompileAndJit()
        {
            var tokens = CreateMathLexer().Lex("1 + 2 * 3").Tokens;
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            var f = a.Compile(tokens);
            FluxJITCompiler<float, FloatOp, FloatMathDef>.Compile(f.Raw(), _def, out var p);
            var inst = a.Instantiate(f, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float Complex_CompileAndJit()
        {
            var tokens = CreateMathLexer().Lex("(1.5 + 2.5) * (3 - 1) / 2 + 5 * 3 - 7 + 2 * (4+1)").Tokens;
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            var f = a.Compile(tokens);
            var inst = a.Instantiate(f, jit: true);
            return inst.Run();
        }

        [Benchmark]
        public float WithVars_CompileAndJit()
        {
            var lexResult = CreateVarLexer("[", "]").Lex("[a] * [b] + [c] / ([d] - [e])");
            var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
            var f = a.Compile(lexResult);
            var inst = a.Instantiate(f, jit: true);
            return inst.Run();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 批量编译：模拟加载阶段编译几百条公式
    // ═══════════════════════════════════════════════════════════════

    [ShortRunJob]
    [MemoryDiagnoser]
    public class BatchCompileBenchmarks
    {
        private static readonly string[] Formulas =
        {
            "[atk] + [bonus] * 2",
            "[hp] - [dmg] * (1 - [armor] / ([armor] + 100))",
            "[crit_rate] * [crit_mult] + (1 - [crit_rate])",
            "[base] * (1 + [scaling] / 100) * [mult]",
            "([a] + [b]) * ([c] - [d]) / 2",
            "[x] * [x] + [y] * [y]",
            "[speed] * (1 - [slow_pct] / 100)",
            "[p_atk] * [skill_mult] - [m_def] * 0.5",
            "([hp_max] - [hp_cur]) / [hp_max] * 100",
            "[lvl] * 50 + [str] * 2 + [agi] * 1.5",
        };

        private FloatMathDef _def;
        private FluxLexer<float, FloatOp> _lexer;

        [GlobalSetup]
        public void Setup()
        {
            _def   = Def;
            _lexer = CreateVarLexer("[", "]");
        }

        [Benchmark]
        public FluxFormula<float, FloatOp>[] Compile10_ShuntingYard()
        {
            var results = new FluxFormula<float, FloatOp>[10];
            for (int i = 0; i < 10; i++)
            {
                var lexResult = _lexer.Lex(Formulas[i]);
                var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
                results[i] = a.Compile(lexResult);
            }
            return results;
        }

        [Benchmark]
        public float[] Compile10_WithJit()
        {
            var results = new float[10];
            for (int i = 0; i < 10; i++)
            {
                var lexResult = _lexer.Lex(Formulas[i]);
                var a = new FluxAssembler<float, FloatOp, FloatMathDef>(_def);
                var f = a.Compile(lexResult);
                results[i] = a.Instantiate(f, jit: true).Run();
            }
            return results;
        }
    }
}
