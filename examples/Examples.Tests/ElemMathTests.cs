using System;
using System.Globalization;
using FluxFormula.Core;
using NUnit.Framework;

public class ElemMathTests
{
    private static readonly ElemDef Def = default;

    private static FluxLexer<ElemValue> CreateLexer()
    {
        return new FluxLexer<ElemValue>(new LexerConfig<ElemValue>
        {
            LiteralOper = (byte)ElemOp.Const,
            LiteralScanner = (ReadOnlySpan<char> src, int pos, out ElemValue value) =>
            {
                value = default;
                if (pos >= src.Length) return pos;

                bool isNeg = src[pos] == '-';
                if (isNeg && pos + 1 < src.Length && !char.IsDigit(src[pos + 1])) return pos;
                if (!char.IsDigit(src[pos]) && !isNeg) return pos;
                int start = pos;
                if (isNeg) pos++;
                while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                if (pos < src.Length && src[pos] == '.')
                {
                    pos++;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }
                float amount = float.Parse(src.Slice(start, pos - start), CultureInfo.InvariantCulture);

                Element elem = Element.Physical;
                if (pos < src.Length && src[pos] == ':')
                {
                    pos++;
                    int tagStart = pos;
                    while (pos < src.Length && char.IsLetter(src[pos])) pos++;
                    elem = src.Slice(tagStart, pos - tagStart).ToString() switch
                    {
                        "fire"  => Element.Fire,
                        "ice"   => Element.Ice,
                        "magic" => Element.Magic,
                        _      => Element.Physical,
                    };
                }
                value = new ElemValue(amount, elem);
                return pos;
            },
            Operators =
            {
                new("+", (byte)ElemOp.Add, slots: new sbyte[] { -1, +1 }),
                new("-", (byte)ElemOp.Sub, slots: new sbyte[] { -1, +1 }),
                new("*", (byte)ElemOp.Mul, slots: new sbyte[] { -1, +1 }),
                new("/", (byte)ElemOp.Div, slots: new sbyte[] { -1, +1 }),
            },
            Brackets = { new("(", ")", (byte)ElemOp.LParen, (byte)ElemOp.RParen) },
            VariablePatterns = { new("[", "]") },
        });
    }

    private static ElemValue Eval(string expr, bool jit = false)
    {
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<ElemValue, ElemDef>(Def);
        var f = runner.Compile(lexer.Lex(expr));
        return runner.Instantiate(f, jit).Run();
    }

    // ═══════════════════════════════════════════════════════
    // Single Operator
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Const_PlainNumber()
    {
        var v = Eval("42");
        Assert.That(v.Amount, Is.EqualTo(42f));
        Assert.That(v.Element, Is.EqualTo(Element.Physical));
    }

    [Test]
    public void Const_WithElementTag()
    {
        var v = Eval("1.5:fire");
        Assert.That(v.Amount, Is.EqualTo(1.5f));
        Assert.That(v.Element, Is.EqualTo(Element.Fire));
    }

    [Test]
    public void Add_SameElement_KeepsLeft()
    {
        var v = Eval("1:fire + 2:fire");
        Assert.That(v.Amount, Is.EqualTo(3f));
        Assert.That(v.Element, Is.EqualTo(Element.Fire));
    }

    [Test]
    public void Add_DifferentElements_SumAmounts()
    {
        var v = Eval("1:fire + 2:ice");
        Assert.That(v.Amount, Is.EqualTo(3f));
        Assert.That(v.Element, Is.EqualTo(Element.Fire)); // keep a.Element
    }

    [Test]
    public void Sub_SameElement_Deducts()
    {
        var v = Eval("10:fire - 3:fire");
        Assert.That(v.Amount, Is.EqualTo(7f));
    }

    [Test]
    public void Sub_DifferentElements_IgnoresDefense()
    {
        var v = Eval("10:fire - 3:ice");
        Assert.That(v.Amount, Is.EqualTo(10f)); // true damage
    }

    [Test]
    public void Mul_KeepsMultiplierElement()
    {
        var v = Eval("100:Physical * 1.5:fire");
        Assert.That(v.Amount, Is.EqualTo(150f));
        Assert.That(v.Element, Is.EqualTo(Element.Fire)); // b.Element
    }

    [Test]
    public void Div_KeepsDivisorElement()
    {
        var v = Eval("100:Physical / 2:ice");
        Assert.That(v.Amount, Is.EqualTo(50f));
        Assert.That(v.Element, Is.EqualTo(Element.Ice));
    }

    [Test]
    public void Neg_KeepsElement()
    {
        var v = Eval("-5:fire");
        Assert.That(v.Amount, Is.EqualTo(-5f));
        Assert.That(v.Element, Is.EqualTo(Element.Fire));
    }

    // ═══════════════════════════════════════════════════════
    // Combined
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Chain_MulAddSub()
    {
        // 100:Physical * 2.5:fire = 250:Fire
        // 250:Fire + 50:Ice = 300:Fire
        // 300:Fire - 30:Fire = 270:Fire
        var lexer  = CreateLexer();
        var runner = new FluxAssembler<ElemValue, ElemDef>(Def);
        var f = runner.Compile(lexer.Lex("[atk] * 2.5:fire + [bonus] - [def]"));
        var result = runner.Instantiate(f)
            .Set("atk",   new ElemValue(100f, Element.Physical))
            .Set("bonus", new ElemValue(50f,  Element.Ice))
            .Set("def",   new ElemValue(30f,  Element.Fire))
            .Run();
        Assert.That(result.Amount, Is.EqualTo(270f));
        Assert.That(result.Element, Is.EqualTo(Element.Fire));
    }

    // ═══════════════════════════════════════════════════════
    // JIT
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Jit_MatchesInterp_Add()
    {
        var interp = Eval("1:fire + 2:fire", jit: false);
        var jit    = Eval("1:fire + 2:fire", jit: true);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Sub()
    {
        var interp = Eval("10:fire - 3:ice", jit: false);
        var jit    = Eval("10:fire - 3:ice", jit: true);
        Assert.That(jit, Is.EqualTo(interp));
    }

    [Test]
    public void Jit_MatchesInterp_Chain()
    {
        var interp = Eval("100:Physical * 2.5:fire + 50:ice - 30:fire", jit: false);
        var jit    = Eval("100:Physical * 2.5:fire + 50:ice - 30:fire", jit: true);
        Assert.That(jit, Is.EqualTo(interp));
    }
}
