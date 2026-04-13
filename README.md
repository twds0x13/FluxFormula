# FluxFormula

[English](./README.en.md)

[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-green)](https://twds0x13.github.io/FluxFormula/)

Unity 高性能零 GC 线性公式编译管线。自定义运算符集，中缀表达式编译为紧凑字节码，解释器或 JIT 双后端执行

## 特性

- **零 GC 热路径**：ref struct、stackalloc 与 unsafe 指针操作，运行时零堆分配。编译期仅一次 Instruction[] 分配，后续执行走纯栈
- **双后端执行**：解释器全平台兼容（含 IL2CPP/AOT），JIT 基于 LINQ Expression Tree 编译为委托，不支持 JIT 的平台自动降级
- **自定义指令集**：实现 `IFluxJITDefinition<TData, TOper>` 接口定义领域运算符，一次编写同时获得解释器与 JIT 两条路径
- **紧凑字节码**：`Instruction` 为 8 字节定长结构体，显式内存布局。256 虚拟寄存器，最大 arity 6，立即数内联至指令缓冲区
- **手写词法分析**：基于 `ReadOnlySpan<char>` 的零分配扫描器，无正则。支持可配置运算符、括号、变量模式与隐式运算符

## 性能

以下数据来自 BenchmarkDotNet（Intel Core Ultra 9 275HX，.NET 9，ShortRun）：

| 阶段 | 操作 | 耗时 | 分配 |
|------|------|------|------|
| Lexer | 简单表达式 | ~140 ns | 392 B |
| Lexer | 复杂表达式 | ~410 ns | 1080 B |
| Compile | 简单表达式 | ~32 ns | 112 B |
| Compile | 复杂表达式 | ~110 ns | 496 B |
| 解释器 | 简单公式求值 | ~20 ns | 0 B |
| 解释器 | 复杂公式求值 | ~30 ns | 0 B |
| JIT | 简单公式求值 | ~1.7 ns | 0 B |
| JIT | 复杂公式求值 | ~4 ns | 0 B |

编译一次性开销 ~30–110 ns + 数百字节分配。执行期零分配，JIT 比解释器快约 5–11 倍。

## 安装

```bash
# Unity Package Manager → Add package from git URL
https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula
```

或在 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula"
  }
}
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

// 2. 实现 IFluxJITDefinition<float, FloatOp>
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

    public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
        => op == FloatOp.Sub && ctx == TokenContext.OperandExpected
            ? FloatOp.Neg : op;

    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
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

// 3. 配置 Lexer，编译公式，注入变量，执行
var config = new LexerConfig<float, FloatOp>
{
    LiteralOper    = FloatOp.Const,
    LiteralParser  = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators      = { new("+", FloatOp.Add), new("-", FloatOp.Sub),
                       new("*", FloatOp.Mul), new("/", FloatOp.Div) },
    Brackets       = { new("(", ")", FloatOp.LParen, FloatOp.RParen) },
    VariablePatterns = { new("[", "]") },
    ImplicitOperators = { FloatOp.Mul },
};

var def    = new FloatMathDef();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);
var lexResult = new FluxLexer<float, FloatOp>(config).Lex("([atk] * 2 + [bonus]) / 100");

float result = runner.Instantiate(runner.Compile(lexResult))
    .Set("atk", 150f)
    .Set("bonus", 25f)
    .Run();
// result = 3.25
```

分离编译与执行（编译一次，多次复用）：

```csharp
var formula = runner.Compile(lexResult);        // 编译（可缓存）
var inst    = runner.Instantiate(formula);       // 实例化（轻量，可反复创建）
float r     = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

## 文档

详细的 API 参考、进阶配置与使用指南，请访问：<https://twds0x13.github.io/FluxFormula/>

## 许可证

MIT License © 2026 twds0x13
