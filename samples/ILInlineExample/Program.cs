using System;
using FluxFormula.Core;

var def = default(FloatMathILDef);
var a   = new FluxAssembler<float, FloatMathILDef>(def);

var lexer = new FluxLexer<float>(new LexerConfig<float>
{
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
    LiteralOper    = (byte)FloatOp.Const,
    Operators =
    {
        new("+", (byte)FloatOp.Add),
        new("-", (byte)FloatOp.Sub),
        new("*", (byte)FloatOp.Mul),
        new("/", (byte)FloatOp.Div),
    },
    Brackets = { new("(", ")", 0, 0) },
});

bool allPassed = true;
void Check(string label, float actual, float expected)
{
    bool ok = Math.Abs(actual - expected) < 0.0001f;
    Console.WriteLine($"  {label,-24} = {actual,6:F2}  {(ok ? "PASS" : "FAIL (expected " + expected + ")")}");
    if (!ok) allPassed = false;
}

// ═══════════════════════════════════════════════════════════
// Test 1: Add + Mul → EmitOp 内联 内联
// ═══════════════════════════════════════════════════════════
Console.WriteLine("公式 1: 1 + 2 * 3  (期望: 7)");
Console.WriteLine(new string('-', 50));
var f1 = a.Compile(lexer.Lex("1 + 2 * 3").Tokens);
Check("解释器",           a.Instantiate(f1, jit: false).Run(), 7f);
Check("IL 路径 (EmitOp 内联)", a.Instantiate(f1, jit: true).Run(),  7f);
Console.WriteLine(new string('-', 50));
Console.WriteLine();

// ═══════════════════════════════════════════════════════════
// Test 2: Sub + Div → Compute 指针 回退（EmitOp 返回 false）
// ═══════════════════════════════════════════════════════════
Console.WriteLine("公式 2: 10 - 4 / 2  (期望: 8)");
Console.WriteLine(new string('-', 50));
var f2 = a.Compile(lexer.Lex("10 - 4 / 2").Tokens);
Check("解释器",              a.Instantiate(f2, jit: false).Run(), 8f);
Check("IL 路径 (Compute 指针 回退)", a.Instantiate(f2, jit: true).Run(),  8f);
Console.WriteLine(new string('-', 50));
Console.WriteLine();

// ═══════════════════════════════════════════════════════════
// Test 3: 负 + 混合运算
// ═══════════════════════════════════════════════════════════
Console.WriteLine("公式 3: -5 + 3 * 4  (期望: 7)");
Console.WriteLine(new string('-', 50));
var f3 = a.Compile(lexer.Lex("-5 + 3 * 4").Tokens);
Check("解释器", a.Instantiate(f3, jit: false).Run(), 7f);
Check("IL 路径", a.Instantiate(f3, jit: true).Run(),  7f);
Console.WriteLine(new string('-', 50));
Console.WriteLine();

if (allPassed)
    Console.WriteLine("All tests passed. EmitOp 内联 IL inline implementation is correct.");
else
    Console.WriteLine("Some tests FAILED.");
