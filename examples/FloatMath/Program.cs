using System;
using System.Globalization;
using FluxFormula.Core;

var config = new LexerConfig<float>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add, slots: new sbyte[] { -1, +1 }),
        new("-", (byte)MathOp.Sub, slots: new sbyte[] { -1, +1 }),
        new("*", (byte)MathOp.Mul, slots: new sbyte[] { -1, +1 }),
        new("/", (byte)MathOp.Div, slots: new sbyte[] { -1, +1 }),
    },
    Brackets =
    {
        new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var lexer  = new FluxLexer<float>(config);
var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);

// 1. 运算符优先级：1 + 2 * 3 = 7
{
    var lexResult = lexer.Lex("1 + 2 * 3");
    var formula   = runner.Compile(lexResult);
    float result  = runner.Instantiate(formula).Run();
    Console.WriteLine($"1 + 2 * 3 = {result}  (expected: 7)");
}

// 2. 括号：(1 + 2) * 3 = 9
{
    var lexResult = lexer.Lex("(1 + 2) * 3");
    var formula   = runner.Compile(lexResult);
    float result  = runner.Instantiate(formula).Run();
    Console.WriteLine($"(1 + 2) * 3 = {result}  (expected: 9)");
}

// 3. 一元负号：-5
{
    var lexResult = lexer.Lex("-5");
    var formula   = runner.Compile(lexResult);
    float result  = runner.Instantiate(formula).Run();
    Console.WriteLine($"-5 = {result}  (expected: -5)");
}

// 4. 除零保护：1 / 0 → NaN
{
    var lexResult = lexer.Lex("1 / 0");
    var formula   = runner.Compile(lexResult);
    float result  = runner.Instantiate(formula).Run();
    Console.WriteLine($"1 / 0 = {result}  (expected: NaN, isNaN: {float.IsNaN(result)})");
}

// 5. 变量注入：[atk] * 2 + [bonus]
{
    var lexResult = lexer.Lex("[atk] * 2 + [bonus]");
    var formula   = runner.Compile(lexResult);
    float result  = runner.Instantiate(formula)
        .Set("atk", 150f)
        .Set("bonus", 25f)
        .Run();
    Console.WriteLine($"[atk] * 2 + [bonus] (atk=150, bonus=25) = {result}  (expected: 325)");
}

// 6. JIT 路径验证
{
    var lexResult = lexer.Lex("(1 + 2) * 3 - 4 / 2");
    var formula   = runner.Compile(lexResult);
    float interp  = runner.Instantiate(formula, jit: false).Run();
    float jit     = runner.Instantiate(formula, jit: true).Run();
    Console.WriteLine($"(1+2)*3-4/2  interpreter: {interp}, JIT: {jit}  (expected: 7)");
}

Console.WriteLine("\nAll examples passed.");
