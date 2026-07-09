# 示例：高级浮点运算

在浮点四则运算（[MathDef](/examples/float-math)）基础上扩展函数式运算符和三元条件的定义体（v5.1.0）。

> **v3.0.0**：操作符枚举是定义体的内部实现细节。定义实现 `IFluxExprDefinition<TData>`（1 个泛型参数），所有操作符方法使用 `byte`。

## 操作符枚举

```csharp
enum AdvMathOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    Select, Lerp, Max, Min, Step,
    Question, Colon, LParen, RParen, Comma, Return = 255,
}
```

相比 MathDef 新增：Select（三元）、Lerp（线性插值）、Max/Min（取极值）、Step（阈值判断）、Question/Colon（三元 `? :` 机制）、Comma（逗号分隔参数）。

## 定义体

```csharp
using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

readonly struct AdvMathDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)AdvMathOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => ((AdvMathOp)op) switch
    {
        AdvMathOp.Add => 2, AdvMathOp.Sub => 2, AdvMathOp.Mul => 2,
        AdvMathOp.Div => 2, AdvMathOp.Neg => 1,
        AdvMathOp.Select => 3, AdvMathOp.Lerp => 3,
        AdvMathOp.Max => 2, AdvMathOp.Min => 2, AdvMathOp.Step => 2,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpType GetKind(byte op) => ((AdvMathOp)op) switch
    {
        AdvMathOp.Const  => OpType.Immediate,
        AdvMathOp.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op) => ((AdvMathOp)op) switch
    {
        AdvMathOp.Add => 1, AdvMathOp.Sub => 1,
        AdvMathOp.Mul => 2, AdvMathOp.Div => 2,
        AdvMathOp.Neg => 3,
        AdvMathOp.Select or AdvMathOp.Lerp or AdvMathOp.Max
            or AdvMathOp.Min or AdvMathOp.Step => 4,
        AdvMathOp.Question => -100,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op) => ((AdvMathOp)op) switch
    {
        AdvMathOp.LParen => new OpPair { PairRole = Pair.Left },
        AdvMathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)AdvMathOp.LParen,
        },
        AdvMathOp.Comma => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft  = (byte)AdvMathOp.LParen,
            IsSeparator = true,
        },
        AdvMathOp.Question => new OpPair { PairRole = Pair.None, EmitOnMatch = true, EmitOpCode = (byte)AdvMathOp.Select },
        AdvMathOp.Colon => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft  = (byte)AdvMathOp.Question,
            IsSeparator = true,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op) => ((AdvMathOp)op) switch
    {
        AdvMathOp.Neg => Associativity.Right,
        _             => Associativity.Left,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OperandPosition GetFirstPosition(byte op) => (AdvMathOp)op switch
    {
        AdvMathOp.Add      => OperandPosition.Left,
        AdvMathOp.Sub      => OperandPosition.Left,
        AdvMathOp.Mul      => OperandPosition.Left,
        AdvMathOp.Div      => OperandPosition.Left,
        AdvMathOp.Question => OperandPosition.Left,
        _                  => OperandPosition.Right,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)AdvMathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)AdvMathOp.Neg;
        return oper;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetOperatorName(byte op) => ((AdvMathOp)op).ToString();

    // 解释器
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((AdvMathOp)op) switch
        {
            AdvMathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            AdvMathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            AdvMathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            AdvMathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            AdvMathOp.Neg => -regs[inst.Arg0],
            AdvMathOp.Select => regs[inst.Arg0] != 0f ? regs[inst.Arg1] : regs[inst.Arg2],
            AdvMathOp.Lerp => regs[inst.Arg0] + (regs[inst.Arg1] - regs[inst.Arg0]) * regs[inst.Arg2],
            AdvMathOp.Max => Math.Max(regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Min => Math.Min(regs[inst.Arg0], regs[inst.Arg1]),
            AdvMathOp.Step => regs[inst.Arg1] >= regs[inst.Arg0] ? 1f : 0f,
            _ => 0f,
        };
    }

    // JIT
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        var one  = Expression.Constant(1f);
        var a = regs[inst.Arg0]; var b = regs[inst.Arg1]; var c = inst.Arity > 2 ? regs[inst.Arg2] : null;

        return ((AdvMathOp)op) switch
        {
            AdvMathOp.Add => Expression.Add(a, b),
            AdvMathOp.Sub => Expression.Subtract(a, b),
            AdvMathOp.Mul => Expression.Multiply(a, b),
            AdvMathOp.Div => Expression.Condition(
                Expression.Equal(b, zero), nan, Expression.Divide(a, b)),
            AdvMathOp.Neg => Expression.Negate(a),
            AdvMathOp.Select => Expression.Condition(
                Expression.NotEqual(a, zero), b, c!),
            AdvMathOp.Lerp => Expression.Add(a,
                Expression.Multiply(Expression.Subtract(b, a), c!)),
            AdvMathOp.Max => Expression.Condition(
                Expression.GreaterThan(a, b), a, b),
            AdvMathOp.Min => Expression.Condition(
                Expression.LessThan(a, b), a, b),
            AdvMathOp.Step => Expression.Condition(
                Expression.GreaterThanOrEqual(b, a), one, zero),
            _ => zero,
        };
    }
}
```

## 三元表达式机制

`?` 和 `:` 通过 `OpPair` 协作实现三元表达式 `a ? b : c`：

- **Question** (`?`)：优先级 `-100`（极低），`PairRole = None`，`EmitOnMatch = true`，`EmitOpCode = Select`。遇到时记录但不立即发射操作码
- **Colon** (`:`)：`PairRole = Right`，`TargetLeft = Question`，`IsSeparator = true`。作为 `<question ... colon>` 配对内的分隔符

解析 `condition ? 100 : 200` 时，汇编器将 `?` 和 `:` 配对，合并为 `Select(condition, 100, 200)` 指令。

## 使用

```csharp
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
        new("select", (byte)AdvMathOp.Select, "(", ")"),
        new("lerp",   (byte)AdvMathOp.Lerp,   "(", ")"),
        new("max",    (byte)AdvMathOp.Max,    "(", ")"),
        new("min",    (byte)AdvMathOp.Min,    "(", ")"),
        new("step",   (byte)AdvMathOp.Step,   "(", ")"),
        new("?", (byte)AdvMathOp.Question),
        new(":", (byte)AdvMathOp.Colon),
        new(",", (byte)AdvMathOp.Comma),
    },
    Brackets = { new("(", ")", (byte)AdvMathOp.LParen, (byte)AdvMathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer  = new FluxLexer<float>(config);
var def    = new AdvMathDef();
var runner = new FluxAssembler<float, AdvMathDef>(def);

// select(condition, true_value, false_value)
// select(1, 100, 200) = 100
// select(0, 100, 200) = 200

// lerp(a, b, t)
// lerp(0, 100, 0.5) = 50

// max(a, b) / min(a, b)
// max(3, 7) = 7; min(3, 7) = 3

// step(edge, x): x >= edge → 1, x < edge → 0
// step(2, 3) = 1; step(2, 1) = 0

// 组合：select(step(2, 3), 100, 0) = 100
// lerp(0, 100, step(0.5, 0.8)) = 100
// max(min(150, 100), 0) = 100  (clamp)
// 1 + select(step(10, [x]), 2, 3) * 4  (带变量)
```

## 关键差异

与 [MathDef](/examples/float-math) 相比：

- 函数式前缀语法运算符：`name(arg0, arg1, ...)`，通过 `Operators` 注册时传入 `"("` 和 `")"`
- 逗号 `,` 是分隔符（`IsSeparator = true`），在 `<lparen ... rparen>` 区间内计数参数
- 三元 `? :` 通过 OpPair 配对转为三参数 `Select` 指令
- `GetFirstPosition`：前缀/函数式运算符返回 `Right`（首个操作数在运算符右侧），中缀 `+、-、*、/、?` 返回 `Left`
- 方法标记 `[MethodImpl(AggressiveInlining)]` 以优化热路径

完整源码见 `examples/AdvMath/AdvMathDef.cs`，测试覆盖见 `examples/Examples.Tests/AdvMathTests.cs`。
