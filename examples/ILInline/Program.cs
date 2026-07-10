using System;
using System.Globalization;
using FluxFormula.Core;

var config = new LexerConfig<float>
{
    LiteralOper   = (byte)FloatOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)FloatOp.Add),
        new("-", (byte)FloatOp.Sub),
        new("*", (byte)FloatOp.Mul),
        new("/", (byte)FloatOp.Div),
    },
    Brackets = { new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var def    = default(FloatMathILDef);
var runner = new FluxAssembler<float, FloatMathILDef>(def);
var lexer  = new FluxLexer<float>(config);

// Basic: 1 + 2 * 3 = 7 (Add and Mul use inline IL; no Compute call overhead)
var formula = runner.Compile(lexer.Lex("1 + 2 * 3").Tokens);
float result = runner.Instantiate(formula, jit: true).Run();
Console.WriteLine($"1 + 2 * 3 = {result}");

// Negation: -5 + 3 = -2 (Neg and Add fall back to Compute pointer call since Neg is not inlined)
var negFormula = runner.Compile(lexer.Lex("(-5) + 3").Tokens);
float negResult = runner.Instantiate(negFormula, jit: true).Run();
Console.WriteLine($"(-5) + 3 = {negResult}");

// Variable injection: [x] * [y] + 10
var varFormula = runner.Compile(lexer.Lex("[x] * [y] + 10").Tokens);
float varResult = runner.Instantiate(varFormula, jit: true).Set("x", 3f).Set("y", 4f).Run();
Console.WriteLine($"[x=3] * [y=4] + 10 = {varResult}");
