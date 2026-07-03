# FluxFormula

Unity 高性能线性公式编译管线（执行期零 GC，编译期一次性分配）。自定义运算符集，中缀表达式编译为紧凑字节码，解释器或 JIT 双后端执行

## 特性

- **执行期零 GC**：ref struct、stackalloc 与 unsafe 指针操作，运行时零堆分配
- **双后端执行**：解释器全平台兼容（含 IL2CPP/AOT），JIT 基于 LINQ Expression Tree 编译为委托，AOT 平台自动降级
- **自定义指令集**：实现 `IFluxExprDefinition<TData>` 接口定义领域运算符，同时获得解释器与 JIT 两条执行路径
- **紧凑字节码**：`Instruction` 为 8 字节定长结构体，`LayoutKind.Explicit` 显式布局。256 虚拟寄存器，最大 arity 6，立即数内联
- **手写词法分析**：`ReadOnlySpan<char>` 零分配扫描器，无正则依赖。可配置运算符、括号、变量模式与隐式运算符

## 安装

```bash
# Unity Package Manager → Add package from git URL
https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula
```

最低 Unity 版本：2021.3

## 快速开始

```csharp
using FluxFormula.Core;
using System.Globalization;

// 1. 定义操作符枚举（底层类型必须为 : byte）
public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg,
    LParen, RParen, Return,
}

// 2. 实现 IFluxExprDefinition<float>
public readonly struct FloatMathDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)FloatOp.Return;

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

    public int GetPrecedence(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 3, _ => 0,
    };

    public byte ResolveToken(byte oper, TokenContext ctx)
        => oper == (byte)FloatOp.Sub && ctx == TokenContext.OperandExpected
            ? (byte)FloatOp.Neg : oper;

    public float Compute(byte op, Instruction inst, Span<float> regs)
        => ((FloatOp)op) switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
        => ((FloatOp)op) switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    // 括号与结合性配置省略，详见文档
}

// 3. Lexer 解析 → 编译 → 变量注入 → 执行
var config = new LexerConfig<float>
{
    LiteralOper    = FloatOp.Const,
    LiteralParser  = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators      = { new("+", FloatOp.Add), new("-", FloatOp.Sub),
                       new("*", FloatOp.Mul), new("/", FloatOp.Div) },
    Brackets       = { new("(", ")", FloatOp.LParen, FloatOp.RParen) },
    VariablePatterns = { new("[", "]") },
    ImplicitOperators = { FloatOp.Mul },
};

var def        = new FloatMathDef();
var runner     = new FluxAssembler<float, FloatMathDef>(def);
var lexResult  = new FluxLexer<float>(config).Lex("([atk] * 2 + [bonus]) / 100");

float result = runner.Instantiate(runner.Compile(lexResult))
    .Set("atk", 150f)
    .Set("bonus", 25f)
    .Run();
// result = 3.25
```

## 文档

完整文档：<https://twds0x13.github.io/FluxFormula/>

## 许可证

MIT License © 2026 twds0x13
