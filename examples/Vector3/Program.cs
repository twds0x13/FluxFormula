using System;
using System.Globalization;
using FluxFormula.Core;

var config = new LexerConfig<Vector3f>
{
    LiteralOper = (byte)Vector3Op.Const,
    LiteralScanner = LexerConfig<Vector3f>.CreateDefaultNumberScanner(
        s => new Vector3f(float.Parse(s, CultureInfo.InvariantCulture), 0, 0)),
    Operators =
    {
        new("+", (byte)Vector3Op.Add),
        new("-", (byte)Vector3Op.Sub),
        new("*", (byte)Vector3Op.Scale),
    },
    Brackets =
    {
        new("(", ")", (byte)Vector3Op.LParen, (byte)Vector3Op.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var lexer  = new FluxLexer<Vector3f>(config);
var def    = new Vector3Def();
var runner = new FluxAssembler<Vector3f, Vector3Def>(def);

// P = P0 + V0 * t
var formula = runner.Compile(lexer.Lex("[P0] + [V0] * [t]"));
var result = runner.Instantiate(formula)
    .Set("P0", new Vector3f(10f, 5f, 0f))
    .Set("V0", new Vector3f(5f, 2f, 0f))
    .Set("t", new Vector3f(3f, 0f, 0f))
    .Run();

Console.WriteLine(result); // → (25.00, 11.00, 0.00)

// JIT verification
var jitResult = runner.Instantiate(formula, jit: true)
    .Set("P0", new Vector3f(10f, 5f, 0f))
    .Set("V0", new Vector3f(5f, 2f, 0f))
    .Set("t", new Vector3f(3f, 0f, 0f))
    .Run();

Console.WriteLine($"JIT matches: {result.Equals(jitResult)}");
