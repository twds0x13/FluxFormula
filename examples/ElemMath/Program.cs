using System;
using System.Globalization;
using FluxFormula.Core;

var config = new LexerConfig<ElemValue>
{
    LiteralOper   = (byte)ElemOp.Const,
    LiteralScanner = (ReadOnlySpan<char> src, int pos, out ElemValue value) =>
    {
        value = default;
        if (pos >= src.Length) return pos;

        // 数字 + 可选标签: 1.5:fire, -3:ice
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
    Brackets =
    {
        new("(", ")", (byte)ElemOp.LParen, (byte)ElemOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var def    = new ElemDef();
var runner = new FluxAssembler<ElemValue, ElemDef>(def);
var lexer  = new FluxLexer<ElemValue>(config);

// Test: basic operations
Console.WriteLine($"Sub: {runner.Instantiate(runner.Compile(lexer.Lex("[a] - [b]"))).Set("a", new ElemValue(200f, Element.Fire)).Set("b", new ElemValue(30f, Element.Fire)).Run()}");
Console.WriteLine($"Add: {runner.Instantiate(runner.Compile(lexer.Lex("[a] + [b]"))).Set("a", new ElemValue(150f, Element.Fire)).Set("b", new ElemValue(50f, Element.Ice)).Run()}");

// Test: full formula (element types set via Set, not via :tag on variables)
var lexResult = lexer.Lex("[atk] * 2.5:fire + [bonus] - [def]");
var formula   = runner.Compile(lexResult);
ElemValue result = runner.Instantiate(formula)
    .Set("atk",   new ElemValue(100f, Element.Physical))
    .Set("bonus", new ElemValue(50f,  Element.Ice))
    .Set("def",   new ElemValue(30f,  Element.Fire))
    .Run();
Console.WriteLine($"Full: {result}");
// 100:Physical * 2.5:Fire = 250:Fire (Mul: b.Element)
// 250:Fire + 50:Ice      = 300:Fire (Add: a.Element stays)
// 300:Fire - 30:Fire     = 270:Fire (Sub: same element deduction)
