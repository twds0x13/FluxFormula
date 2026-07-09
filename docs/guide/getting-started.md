# 快速入门

从零构建一个完整的浮点四则运算公式（v5.1.0）。

## 第 1 步：定义操作符枚举

操作符枚举现在是定义体的 `private` 实现细节，框架只看到 `byte`：

```csharp
enum MathOp : byte
{
    Const,     // 立即数（操作数）
    Add,       // +
    Sub,       // -
    Mul,       // *
    Div,       // /
    Neg,       // 一元取负
    LParen,    // (
    RParen,    // )
    Return = 255,  // 终止指令
}
```

## 第 2 步：实现定义

实现 `IFluxExprDefinition<float>`（单泛型参数），所有操作符相关方法接收/返回 `byte`：

```csharp
readonly struct MathDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)MathOp.Return;

    public int GetArity(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 2, MathOp.Sub => 2, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((MathOp)op) switch
    {
        MathOp.Const  => OpType.Immediate,
        MathOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 1, MathOp.Sub => 1, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 3, _ => 0,
    };

    public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
    {
        MathOp.Neg => Associativity.Right,
        _          => Associativity.Left,
    };

    public OperandPosition GetFirstPosition(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => OperandPosition.Left,
        MathOp.Sub => OperandPosition.Left,
        MathOp.Mul => OperandPosition.Left,
        MathOp.Div => OperandPosition.Left,
        _          => OperandPosition.Right,
    };

    public OpPair GetPair(byte op) => ((MathOp)op) switch
    {
        MathOp.LParen => new OpPair { PairRole = Pair.Left },
        MathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)MathOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    // Token 消歧：'-' 在期望操作数时是一元取负
    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    // 解释器路径
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((MathOp)op) switch
        {
            MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            MathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            MathOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            MathOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT 路径
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((MathOp)op) switch
        {
            MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
}
```

::: tip LiteralTemplate (v5.0+)
对于自定义 TData struct，可使用 `[LiteralTemplate]` 在编译期自动生成字面量扫描器，无需手写 `LiteralScanner` 委托。内置 `float` 类型直接通过 `CreateDefaultNumberScanner` 工作。详见[字面量扫描器指南](/guide/literal-scanner)。
:::

## 第 3 步：配置 Lexer 并解析

```csharp
var config = new LexerConfig<float>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets =
    {
        new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),  // 变量用 [ ] 包裹： [atk]、[def]
    },
    ImplicitOperators = { (byte)MathOp.Mul },  // 2[atk] → 2*[atk]
};

var lexer = new FluxLexer<float>(config);
var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
// lexResult.Tokens   → FluxToken[]
// lexResult.VarNames → 变量名数组（atk, bonus）
```

## 第 4 步：编译并执行

```csharp
var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);

var formula = runner.Compile(lexResult);
float result = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("bonus", 25f)
    .Run();
// result = 3.25
```

## 分离编译与执行

公式可缓存复用。编译一次，多次实例化运行：

```csharp
// 编译（可缓存）
var formula = runner.Compile(lexResult);

// 实例化（轻量，可反复创建）
var inst = runner.Instantiate(formula, jit: false);
float r = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

## 直接构造 Token（无 Lexer）

```csharp
var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
float result  = runner.Instantiate(formula, jit: true).Run();
// result = 7.0
```

## v3.0.0 至 v5.1.0 关键变更

| v2.x | v3.0.0 |
|------|--------|
| `public enum FloatOp : byte` | `enum MathOp : byte`（private） |
| `IFluxExprDefinition<float, FloatOp>` | `IFluxExprDefinition<float>` |
| `GetPrecedence(FloatOp op)` | `GetPrecedence(byte op)` |
| `ResolveToken(FloatOp op, ...)` | `ResolveToken(byte oper, ...)` |
| `OpPair<FloatOp>` | `OpPair`（非泛型） |
| `FluxToken<float, FloatOp>` | `FluxToken<float>` |
| `FluxAssembler<float, FloatOp, FloatMathDef>` | `FluxAssembler<float, MathDef>` |

| v3.0.0 | v5.1.0 |
|--------|--------|
| `LiteralScanner` 委托必设 | `[LiteralTemplate]` 生成式扫描器（委托变为可选） |
| 手动扫描逻辑 | 编译期 source generator 生成 `Scan_Xxx` 方法 |

## 下一步

- [核心概念](/guide/core-concepts) — Token → Formula → Instance 完整流水线
- [自定义运算符](/guide/writing-a-definition) — `IFluxExprDefinition` 各方法详解
- [完整示例](/examples/float-math) — 可直接拷贝的 MathDef 完整实现
