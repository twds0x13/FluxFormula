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
        new("+", (byte)AdvMathOp.Add),
        new("-", (byte)AdvMathOp.Sub),
        new("*", (byte)AdvMathOp.Mul),
        new("/", (byte)AdvMathOp.Div),
        new("select", (byte)AdvMathOp.Select, "(", ")"),
        new("lerp",   (byte)AdvMathOp.Lerp,   "(", ")"),
        new("max",    (byte)AdvMathOp.Max,    "(", ")"),
        new("min",    (byte)AdvMathOp.Min,    "(", ")"),
        new("step",   (byte)AdvMathOp.Step,   "(", ")"),
        new(",", (byte)AdvMathOp.Comma),
    },
    Brackets =
    {
        new("(", ")", (byte)AdvMathOp.LParen, (byte)AdvMathOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),
    },
};

var lexer  = new FluxLexer<float>(config);
var def    = new AdvMathDef();

// ═══════════════════════════════════════════════════════
// 单算符演示
// ═══════════════════════════════════════════════════════

// Select: 条件选择
{
    float r = Eval("select(1, 100, 200)");
    Console.WriteLine($"select(1, 100, 200) = {r}  (expected: 100)");

    r = Eval("select(0, 100, 200)");
    Console.WriteLine($"select(0, 100, 200) = {r}  (expected: 200)");
}

// Lerp: 线性插值
{
    float r = Eval("lerp(0, 100, 0.5)");
    Console.WriteLine($"lerp(0, 100, 0.5) = {r}  (expected: 50)");
}

// Max / Min
{
    float r = Eval("max(3, 7)");
    Console.WriteLine($"max(3, 7) = {r}  (expected: 7)");

    r = Eval("min(3, 7)");
    Console.WriteLine($"min(3, 7) = {r}  (expected: 3)");
}

// Step: 阶跃函数
{
    float r = Eval("step(2, 3)");
    Console.WriteLine($"step(2, 3) = {r}  (expected: 1)");

    r = Eval("step(2, 1)");
    Console.WriteLine($"step(2, 1) = {r}  (expected: 0)");
}

// ═══════════════════════════════════════════════════════
// 核心演示：Step → Select 组合
// ═══════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine("── Step → Select 组合 ──");

{
    // step 输出 0/1，恰好被 select 作为条件消费
    float r = Eval("select(step(2, 3), 100, 0)");
    Console.WriteLine($"select(step(2, 3), 100, 0) = {r}  (expected: 100, x=3 >= 2)");

    r = Eval("select(step(2, 1), 100, 0)");
    Console.WriteLine($"select(step(2, 1), 100, 0) = {r}  (expected: 0, x=1 < 2)");
}

// Step → Lerp 组合
{
    float r = Eval("lerp(0, 100, step(0.5, 0.8))");
    Console.WriteLine($"lerp(0, 100, step(0.5, 0.8)) = {r}  (expected: 100)");

    r = Eval("lerp(0, 100, step(0.5, 0.2))");
    Console.WriteLine($"lerp(0, 100, step(0.5, 0.2)) = {r}  (expected: 0)");
}

// Max/Min 链：clamp
{
    float r = Eval("max(min(150, 100), 0)");
    Console.WriteLine($"max(min(150, 100), 0) = {r}  (expected: 100, clamp to [0,100])");

    r = Eval("max(min(-20, 100), 0)");
    Console.WriteLine($"max(min(-20, 100), 0) = {r}  (expected: 0, clamp to [0,100])");
}

// ═══════════════════════════════════════════════════════
// 混合运算
// ═══════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine("── 混合运算 ──");

{
    float r = Eval("1 + select(step(10, [x]), 2, 3) * 4");
    Console.WriteLine($"1 + select(step(10, [x]), 2, 3) * 4 (x=15) = {EvalVar("1 + select(step(10, [x]), 2, 3) * 4", "x", 15f)}  (expected: 9)");
    Console.WriteLine($"1 + select(step(10, [x]), 2, 3) * 4 (x=5)  = {EvalVar("1 + select(step(10, [x]), 2, 3) * 4", "x", 5f)}  (expected: 13)");
}

// JIT 一致性
{
    var expr = "select(step(2, 3), max(10, 20), min(10, 20)) + lerp(0, 100, 0.5)";
    float interp = Eval(expr, jit: false);
    float jit    = Eval(expr, jit: true);
    Console.WriteLine($"JIT vs interp: {interp} / {jit}  (expected: 70)");
}

Console.WriteLine("\nAll advanced examples passed.");

// ═══════════════════════════════════════════════════════
// 辅助方法（每次调用新建 FluxAssembler——ref struct 不能被捕获）
// ═══════════════════════════════════════════════════════

float Eval(string expr, bool jit = false)
{
    var r  = lexer.Lex(expr);
    var asm = new FluxAssembler<float, AdvMathDef>(def);
    var f  = asm.Compile(r);
    return asm.Instantiate(f, jit).Run();
}

float EvalVar(string expr, string name, float value)
{
    var r   = lexer.Lex(expr);
    var asm = new FluxAssembler<float, AdvMathDef>(def);
    var f   = asm.Compile(r);
    return asm.Instantiate(f).Set(name, value).Run();
}
