using NUnit.Framework;
using FluxFormula.Core;

namespace FluxFormula.Tests
{
    /// <summary>
    /// FluxModifier / FluxChain / Formula 互转 及 Connect 组合测试。
    /// v3.x FluxChain 分裂后，Connect 返回 FluxChain，Modifier 始终为原子。
    /// </summary>
    [TestFixture]
    public class ModifierFormulaTests
    {
        private static readonly FloatMathDef Def = new();
        private static FluxLexer<float> CreateMathLexer() => new(new LexerConfig<float>
        {
            LiteralOper    = (byte)FloatOp.Const,
            LiteralParser  = s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            Operators      = { new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                               new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div) },
            Brackets       = { new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen) },
        });

        private static FluxLexer<float> CreateVarLexer(string prefix, string suffix) => new(new LexerConfig<float>
        {
            LiteralOper    = (byte)FloatOp.Const,
            LiteralParser  = s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            Operators      = { new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                               new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div) },
            Brackets       = { new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen) },
            VariablePatterns = { new(prefix, suffix) },
        });

        // ═══════════════════════════════════════════════════════
        // FluxFormula → Modifier
        // ═══════════════════════════════════════════════════════

        [Test]
        public void ToModifier_RemovesFirstOperand()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "1 + 2");
            var m = f.ToModifier();
            Assert.That(m.Count, Is.EqualTo(f.Count - 1 - 1)); // -1 Immediate, -1 data slot
            Assert.That(m.Type, Is.EqualTo(FluxType.Modifier));
        }

        [Test]
        public void ToModifier_AlreadyModifier_ReturnsSelf()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "2 * 3");
            var mod = f.ToModifier();
            var modAgain = mod.ToFormula("x").ToModifier(); // Round-trip via Formula
            Assert.That(modAgain.Type, Is.EqualTo(FluxType.Modifier));
        }

        // ═══════════════════════════════════════════════════════
        // FluxFormula.Connect → FluxChain
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Connect_TwoModifiers_ReturnsChain()
        {
            var lexer = CreateMathLexer();
            var fA = Compile(lexer, "1 + 2");
            var fB = Compile(lexer, "3 + 4").ToModifier();
            var fC = Compile(lexer, "5 + 6").ToModifier();

            var chain = fA.Connect(fB).Connect(fC);
            Assert.That(chain.Length, Is.EqualTo(3));
        }

        [Test]
        public void Connect_SingleInstructionFormula_PromotesModifier()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "42");
            var m = Compile(lexer, "1 + 2").ToModifier();

            var chain = f.Connect(m);
            // Formula + Modifier = 2-link chain
            Assert.That(chain.Length, Is.EqualTo(2));
            var atomic = chain.ToAtomic();
            Assert.That(atomic.Type, Is.EqualTo(FluxType.Formula));
        }

        [Test]
        public void Connect_EmptyModifierChain_ReturnsSelf()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "1 + 2");
            var chain = f.Connect(FluxModifier<float, FloatMathDef>.Empty);
            Assert.That(chain.Length, Is.EqualTo(1));
        }

        // ═══════════════════════════════════════════════════════
        // FluxChain.ToAtomic
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Chain_ToAtomic_SingleLink_ReturnsSameFormula()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "42");
            var chain = f.Connect(FluxModifier<float, FloatMathDef>.Empty);
            var atomic = chain.ToAtomic();
            Assert.That(atomic.Count, Is.EqualTo(f.Count));
        }

        [Test]
        public void Chain_ToAtomic_MultiLink_MergesBytecode()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "1 + 2");
            var m = Compile(lexer, "3 + 4").ToModifier();
            var chain = f.Connect(m);
            var atomic = chain.ToAtomic();
            Assert.That(atomic.Count, Is.GreaterThan(f.Count));
        }

        // ═══════════════════════════════════════════════════════
        // FluxModifier.Connect → FluxChain
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Modifier_Connect_TwoModifiers_ReturnsChain()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "1 + 2").ToModifier();
            var mB = Compile(lexer, "3 + 4").ToModifier();

            var chain = mA.Connect(mB);

            Assert.That(chain.Length, Is.EqualTo(2));
            Assert.That(chain.GetLinks()[0].Type, Is.EqualTo(FluxType.Modifier));

            // Promote to Formula and evaluate
            var formula = chain.ToAtomic().ToFormula("x");
            var runner = new FluxAssembler<float, FloatMathDef>(Def);
            var result = runner.Instantiate(formula).Set("x", 1f).Run();
            Assert.That(result, Is.EqualTo(7f).Within(1e-5f));
        }

        [Test]
        public void Modifier_Connect_EmptyLeft_ReturnsRight()
        {
            var lexer = CreateMathLexer();
            var m = Compile(lexer, "5 + 6").ToModifier();
            var empty = FluxModifier<float, FloatMathDef>.Empty;

            var result = empty.Connect(m);
            Assert.That(result.Length, Is.EqualTo(1));
        }

        [Test]
        public void Modifier_Connect_EmptyRight_ReturnsLeft()
        {
            var lexer = CreateMathLexer();
            var m = Compile(lexer, "5 + 6").ToModifier();

            var result = m.Connect(FluxModifier<float, FloatMathDef>.Empty);
            Assert.That(result.Length, Is.EqualTo(1));
        }

        [Test]
        public void Modifier_Connect_ThreeModifierChain()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "1 + 2").ToModifier();
            var mB = Compile(lexer, "3 + 4").ToModifier();
            var mC = Compile(lexer, "5 + 6").ToModifier();

            var chain = mA.Connect(mB).Connect(mC);
            Assert.That(chain.Length, Is.EqualTo(3));
        }

        // ═══════════════════════════════════════════════════════
        // FluxModifier 序列化
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Modifier_ToBytes_FromBytes_RoundTrip()
        {
            var lexer = CreateMathLexer();
            var orig = Compile(lexer, "7 + 3").ToModifier();
            var bytes = orig.ToBytes();

            var restored = FluxModifier<float, FloatMathDef>.FromBytes(bytes);
            Assert.That(restored.Count, Is.EqualTo(orig.Count));
            Assert.That(restored.Type, Is.EqualTo(FluxType.Modifier));
            Assert.That(restored.ImmediateCount, Is.EqualTo(orig.ImmediateCount));
        }

        [Test]
        public void Modifier_FromBytes_RoundTrip_CanRunAfterToFormula()
        {
            var lexer = CreateMathLexer();
            var orig = Compile(lexer, "10 + 5").ToModifier();
            var bytes = orig.ToBytes();

            var restored = FluxModifier<float, FloatMathDef>.FromBytes(bytes);
            var formula = restored.ToFormula("y");
            var runner = new FluxAssembler<float, FloatMathDef>(Def);
            var result = runner.Instantiate(formula).Set("y", 10f).Run();

            Assert.That(result, Is.EqualTo(15f).Within(1e-5f));
        }

        [Test]
        public void Modifier_FromBytes_ReadOnlySpan_RoundTrip()
        {
            var lexer = CreateMathLexer();
            var orig = Compile(lexer, "2 * 8").ToModifier();
            var bytes = orig.ToBytes();

            var restored = FluxModifier<float, FloatMathDef>.FromBytes(new System.ReadOnlySpan<byte>(bytes));
            Assert.That(restored.Count, Is.EqualTo(orig.Count));
            Assert.That(restored.Type, Is.EqualTo(FluxType.Modifier));
        }

        // ═══════════════════════════════════════════════════════
        // FluxModifier 属性与方法
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Modifier_GetByteHash_SameBytecode_SameHash()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "3 * 7").ToModifier();
            var mB = Compile(lexer, "3 * 7").ToModifier();

            Assert.That(mA.GetByteHash(), Is.EqualTo(mB.GetByteHash()));
        }

        [Test]
        public void Modifier_GetByteHash_DifferentBytecode_DifferentHash()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "3 * 7").ToModifier();
            var mB = Compile(lexer, "4 + 9").ToModifier();

            Assert.That(mA.GetByteHash(), Is.Not.EqualTo(mB.GetByteHash()));
        }

        [Test]
        public void Modifier_Raw_ReturnsCorrectSpan()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "1 + 2");
            var m = f.ToModifier();

            var raw = m.Raw();
            Assert.That(raw.Length, Is.EqualTo(m.Count));
        }

        [Test]
        public void Chain_GetLinks_ReturnsLinks()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "1 + 2").ToModifier();
            var mB = Compile(lexer, "3 + 4").ToModifier();
            var chain = mA.Connect(mB);

            var links = chain.GetLinks();
            Assert.That(links.Length, Is.EqualTo(2));
        }

        [Test]
        public void Modifier_Empty_HasZeroCount()
        {
            var empty = FluxModifier<float, FloatMathDef>.Empty;
            Assert.That(empty.Count, Is.EqualTo(0));
        }

        [Test]
        public void Modifier_VariableSlots_Preserved()
        {
            var lex = CreateVarLexer("[", "]");
            var f = Compile(lex, "[x] + [y]");
            var m = f.ToModifier();

            Assert.That(m.VariableSlots.Length, Is.EqualTo(1));
            Assert.That(m.VariableSlots[0].Name, Is.EqualTo("y"));
        }

        [Test]
        public void Modifier_MaxRegister_MatchesInner()
        {
            var lexer = CreateMathLexer();
            var f = Compile(lexer, "1 + 2 * 3");
            var m = f.ToModifier();

            Assert.That(m.MaxRegister, Is.EqualTo(f.MaxRegister));
        }

        // ═══════════════════════════════════════════════════════
        // P3-1 回归：Modifier 首链 Instantiate+Run 正确性
        // ═══════════════════════════════════════════════════════

        [Test]
        public void ModifierFirstChain_Instantiate_Run_Interpreter()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "2 + 3").ToModifier();
            var mB = Compile(lexer, "4 + 5").ToModifier();
            var chain = mA.Connect(mB);

            var runner = new FluxAssembler<float, FloatMathDef>(Def);
            var inst = runner.Instantiate(chain, jit: false);

            Assert.That(inst.Run(), Is.EqualTo(8f).Within(1e-5f));
        }

        [Test]
        public void ModifierFirstChain_Instantiate_Run_Jit()
        {
            var lexer = CreateMathLexer();
            var mA = Compile(lexer, "2 + 3").ToModifier();
            var mB = Compile(lexer, "4 + 5").ToModifier();
            var chain = mA.Connect(mB);

            var runner = new FluxAssembler<float, FloatMathDef>(Def);
            var inst = runner.Instantiate(chain, jit: true);
            Assert.That(inst.Run(), Is.EqualTo(8f).Within(1e-5f));
        }

        [Test]
        public void ModifierFirstChain_Instantiate_Run_Jit_WithVariable()
        {
            var lex = CreateVarLexer("[", "]");
            var fA = Compile(lex, "1 + [x]");
            var fB = Compile(lex, "2 + [y]");
            var chain = fA.ToModifier().Connect(fB.ToModifier());

            var runner = new FluxAssembler<float, FloatMathDef>(Def);
            var instJit = runner.Instantiate(chain, jit: true);
            var instInt = runner.Instantiate(chain, jit: false);

            Assert.That(instJit.Run(), Is.EqualTo(instInt.Run()).Within(1e-5f));
        }

        // ═══════════════════════════════════════════════════════
        // FluxFormula 序列化（保持原子语义）
        // ═══════════════════════════════════════════════════════

        [Test]
        public void FromBytes_RoundTrip()
        {
            var lexer = CreateMathLexer();
            var orig = Compile(lexer, "3.14 + 2.718 * 1.414");
            byte[] bytes = orig.ToBytes();

            var restored = FluxFormula<float, FloatMathDef>.FromBytes(bytes);

            float origVal = EvalFormula(orig);
            float restVal = EvalFormula(restored);
            Assert.That(restVal, Is.EqualTo(origVal).Within(1e-6f));
        }

        // ═══════════════════════════════════════════════════════
        // 辅助
        // ═══════════════════════════════════════════════════════

        private static FluxFormula<float, FloatMathDef> Compile(
            FluxLexer<float> lexer, string expr)
        {
            return new FluxAssembler<float, FloatMathDef>(Def)
                .Compile(lexer.Lex(expr));
        }

        private static float EvalFormula(FluxFormula<float, FloatMathDef> f)
        {
            var assembler = new FluxAssembler<float, FloatMathDef>(Def);
            var inst = assembler.Instantiate(f);
            return inst.Run();
        }
    }
}
