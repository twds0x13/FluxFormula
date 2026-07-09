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
        // 中缀 (标准二元)
        new("+", (byte)Vector3Op.Add, slots: new sbyte[] { -1, +1 }),
        new("-", (byte)Vector3Op.Sub, slots: new sbyte[] { -1, +1 }),
        new("*", (byte)Vector3Op.Scale, slots: new sbyte[] { -1, +1 }),
        new("x", (byte)Vector3Op.Cross, slots: new sbyte[] { -1, +1 }),
        // 函数式 (前缀 + 括号 + 逗号分隔)
        new("cross", (byte)Vector3Op.Cross,
            slots: new sbyte[] { +2, +4 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
        new("dot", (byte)Vector3Op.Dot,
            slots: new sbyte[] { +2, +4 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ","), new(+5, ")") }),
        new("norm", (byte)Vector3Op.Norm,
            slots: new sbyte[] { +2 },
            aux: new AuxRule[] { new(+1, "("), new(+3, ")") }),
        new(",", (byte)Vector3Op.Comma),
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

float F(float v) => v;  // shorthand for readable test output

// P = P0 + V0 * t
var formula = runner.Compile(lexer.Lex("[P0] + [V0] * [t]"));
var r1 = runner.Instantiate(formula)
    .Set("P0", new Vector3f(10f, 5f, 0f))
    .Set("V0", new Vector3f(5f, 2f, 0f))
    .Set("t",  new Vector3f(3f, 0f, 0f))
    .Run();
Console.WriteLine($"P0 + V0 * t = {r1}");

// Cross: a x b
var cross = runner.Instantiate(runner.Compile(lexer.Lex("[a] x [b]")))
    .Set("a", new Vector3f(1, 0, 0))
    .Set("b", new Vector3f(0, 1, 0))
    .Run();
Console.WriteLine($"(1,0,0) x (0,1,0) = {cross}");  // → (0, 0, 1)

// Norm: normalize a vector
var norm = runner.Instantiate(runner.Compile(lexer.Lex("norm([v])")))
    .Set("v", new Vector3f(3, 4, 0))
    .Run();
Console.WriteLine($"norm(3,4,0) = {norm}");  // → (0.60, 0.80, 0.00)

// Dot: dot product (result in X component)
var dot = runner.Instantiate(runner.Compile(lexer.Lex("dot([a], [b])")))
    .Set("a", new Vector3f(1, 2, 3))
    .Set("b", new Vector3f(4, 5, 6))
    .Run();
Console.WriteLine($"dot((1,2,3), (4,5,6)) = {dot.X}");  // → 32

// JIT consistency
var jit = runner.Instantiate(runner.Compile(lexer.Lex("cross(norm([a]), [b])")), jit: true)
    .Set("a", new Vector3f(3, 4, 0))
    .Set("b", new Vector3f(0, 0, 1))
    .Run();
var interp = runner.Instantiate(runner.Compile(lexer.Lex("cross(norm([a]), [b])")), jit: false)
    .Set("a", new Vector3f(3, 4, 0))
    .Set("b", new Vector3f(0, 0, 1))
    .Run();
Console.WriteLine($"JIT matches interp: {jit.Equals(interp)}");
