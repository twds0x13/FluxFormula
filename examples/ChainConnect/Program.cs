using System;
using System.Globalization;
using FluxFormula.Core;

var config = new LexerConfig<float>
{
    LiteralOper = (byte)AdvMathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)AdvMathOp.Add), new("-", (byte)AdvMathOp.Sub),
        new("*", (byte)AdvMathOp.Mul), new("/", (byte)AdvMathOp.Div),
        new("step", (byte)AdvMathOp.Step, "(", ")"),
        new("?", (byte)AdvMathOp.Question), new(":", (byte)AdvMathOp.Colon),
        new(",", (byte)AdvMathOp.Comma),
    },
    Brackets = { new("(", ")", (byte)AdvMathOp.LParen, (byte)AdvMathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer  = new FluxLexer<float>(config);
var def    = new AdvMathDef();
var runner = new FluxAssembler<float, AdvMathDef>(def);

// 1. 基础 Connect
var fA = runner.Compile(lexer.Lex("10 + 5"));
var fB = runner.Compile(lexer.Lex("2 * 2")).ToModifier();
Console.WriteLine($"(10+5) * (2*2) = {runner.Instantiate(fA.Connect(fB)).Run()}  (expected: 30)");

// 2. 三元 Connect：step → ? :
var stepHi = runner.Compile(lexer.Lex("step(0.5, 0.8)"));
var stepLo = runner.Compile(lexer.Lex("step(0.5, 0.2)"));
var tern   = runner.Compile(lexer.Lex("? 100 : 0")).ToModifier();
Console.WriteLine($"step(0.5,0.8) ? 100 : 0 = {runner.Instantiate(stepHi.Connect(tern)).Run()}  (expected: 100)");
Console.WriteLine($"step(0.5,0.2) ? 100 : 0 = {runner.Instantiate(stepLo.Connect(tern)).Run()}  (expected: 0)");

// 3. Modifier 链
var base_ = runner.Compile(lexer.Lex("1 + 2"));
var c = base_.Connect(runner.Compile(lexer.Lex("3 * 2")).ToModifier())
             .Connect(runner.Compile(lexer.Lex("2 + 1")).ToModifier());
Console.WriteLine($"(1+2) -> x6 -> +3 = {runner.Instantiate(c).Run()}  (expected: 7)");

// 4. 往返
var f = runner.Compile(lexer.Lex("7 + 3"));
var m = f.ToModifier();
Console.WriteLine($"roundtrip: {runner.Instantiate(m.ToFormula("in")).Set("in", 7f).Run()}  (expected: 10)");

// 5. ToAtomic vs per-link
var ca = fA.Connect(fB);
float pl = runner.Instantiate(ca).Run();
float at = runner.Instantiate(ca.ToAtomic()).Run();
Console.WriteLine($"per-link: {pl}, atomic: {at}  (expected: both 30)");

// 6. JIT
float j = runner.Instantiate(stepHi.Connect(tern), jit: true).Run();
Console.WriteLine($"step? JIT: {j}  (expected: 100)");

Console.WriteLine("\nAll ChainConnect examples passed.");
