# 快速入门

从零构建一个完整的浮点四则运算公式。

## 第 1 步：定义操作符枚举

```csharp
public enum FloatOp : byte
{
    Const,     // 立即数（操作数）
    Add,       // +
    Sub,       // -
    Mul,       // *
    Div,       // /
    Neg,       // 一元取负
    LParen,    // (
    RParen,    // )
    Return,    // 终止指令
}
```

枚举底层类型必须为 `: byte`。框架通过 `*(byte*)&oper` 取底层字节作为 opcode，底层类型不为 byte 会在类型初始化阶段抛出异常。

## 第 2 步：实现定义

```csharp
public readonly struct FloatMathDef : IFluxJITDefinition<float, FloatOp>
{
    public FloatOp GetReturnOp() => FloatOp.Return;

    public int GetArity(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    public int GetPrecedence(FloatOp op) => op switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 3, _ => 0,
    };

    // Token 消歧：'-' 在期望操作数时是一元取负
    public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
    {
        if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
            return FloatOp.Neg;
        return op;
    }

    // 解释器路径
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return ((FloatOp)op) switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT 路径
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((FloatOp)op) switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
    // 括号和结合性配置省略，详见"自定义运算符"章节
}
```

## 第 3 步：配置 Lexer 并解析

```csharp
var config = new LexerConfig<float, FloatOp>
{
    LiteralOper = FloatOp.Const,
    LiteralParser = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators =
    {
        new("+", FloatOp.Add),
        new("-", FloatOp.Sub),
        new("*", FloatOp.Mul),
        new("/", FloatOp.Div),
    },
    Brackets =
    {
        new("(", ")", FloatOp.LParen, FloatOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),  // 变量用 [ ] 包裹： [atk]、[def]
    },
    ImplicitOperators = { FloatOp.Mul },  // 2[atk] → 2*[atk]
};

var lexer = new FluxLexer<float, FloatOp>(config);
var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
// lexResult.Tokens   → FluxToken[]
// lexResult.VarNames → 变量名数组（atk, bonus）
```

## 第 4 步：编译并执行

```csharp
var def    = new FloatMathDef();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);

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

当不需要字符串解析时，可直接构造 Token 数组：

```csharp
var tokens = new FluxToken<float, FloatOp>[]
{
    new() { Oper = FloatOp.Const, Data = 1f },
    new() { Oper = FloatOp.Add },
    new() { Oper = FloatOp.Const, Data = 2f },
    new() { Oper = FloatOp.Mul },
    new() { Oper = FloatOp.Const, Data = 3f },
};
// 表达式: 1 + 2 * 3

float result = runner.Build(tokens, jit: true).Run();
// result = 7.0
```

## 下一步

- [核心概念](/guide/core-concepts) — Token → Formula → Instance 完整流水线
- [自定义运算符](/guide/writing-a-definition) — `IFluxJITDefinition` 各方法详解
- [完整示例](/examples/float-math) — 可直接拷贝的 FloatMathDef 完整实现
