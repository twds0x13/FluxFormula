using System;
using FluxFormula.Core;

// ═══════════════════════════════════════════════════════
// ElemValue 通过 [LiteralTemplate] + [LiteralTag] 自动生成
// 字面量扫描器，无需手写 LiteralScanner 委托。
// 模板: "<float Amount><optional>:<Element tag></optional>"
// 支持的语法: 42, -5, 1.5:fire, 100:ice
// ═══════════════════════════════════════════════════════

var config = new LexerConfig<ElemValue>
{
    LiteralOper   = (byte)ElemOp.Const,
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

var lexResult = lexer.Lex("[atk] * 2.5:fire + [bonus] - [def]");
var formula   = runner.Compile(lexResult);

ElemValue result = runner.Instantiate(formula)
    .Set("atk",   new ElemValue(100f, Element.Physical))
    .Set("bonus", new ElemValue(50f,  Element.Ice))
    .Set("def",   new ElemValue(30f,  Element.Fire))
    .Run();

Console.WriteLine($"Result: {result}");
// Expected: 270.00:Fire

var jitResult = runner.Instantiate(formula, jit: true)
    .Set("atk",   new ElemValue(100f, Element.Physical))
    .Set("bonus", new ElemValue(50f,  Element.Ice))
    .Set("def",   new ElemValue(30f,  Element.Fire))
    .Run();
Console.WriteLine($"JIT matches: {result.Equals(jitResult)}");
