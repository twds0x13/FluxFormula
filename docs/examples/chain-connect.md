# 示例：链式 Connect

以下示例演示 `Connect()` 的链式行为。ChainLink 为公开结构体：普通用户通过 `FluxFormula` / `FluxModifier` 的方法间接使用，高级用户可通过 `FluxChain.GetLinks()` 读取链结构并配合 `VffFormat.ToBytes()` 持久化。

> **v3.0.0**：`Connect()` 的类型签名从运行时检查提升为编译期保证：参数是 `FluxModifier<TData, TDef>`，传入 `FluxFormula` 编译不过。`ToMultiplier()` 重命名为 `ToModifier()`（旧名保留 `[Obsolete]`）。

## 基础链式 Connect

`Connect()` 的类型签名只接受 `FluxModifier`。必须先调用 `.ToModifier()` 剥离首操作数：

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
    },
    Brackets = { new("(", ")", (byte)AdvMathOp.LParen, (byte)AdvMathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer  = new FluxLexer<float>(config);
var def    = new AdvMathDef();
var runner = new FluxAssembler<float, AdvMathDef>(def);

var fA = runner.Compile(lexer.Lex("10 + 5"));                // Formula（有首操作数 10）
var fB = runner.Compile(lexer.Lex("2 * 3"));                 // Formula（有首操作数 2）

// ❌ 编译错误：Connect 只接受 FluxModifier，FluxFormula 传不进去
// var chain = fA.Connect(fB);  // CS1503: cannot convert FluxFormula to FluxModifier

// ✅ 正确：先将 fB 转为 Modifier（剥离首操作数 2，改为从 R1 读取输入）
var chain = fA.Connect(fB.ToModifier());  // (10+5) * 3 = 45

var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // 45
```

## Modifier 链：前一个 link 的输出传递给下一个

`ToModifier()` 将 Formula 转为 Modifier：移除第一操作数，替换为 R1 总线输入：

```csharp
var fBase = runner.Compile(lexer.Lex("1 + 2"));     // = 3
var fMod  = runner.Compile(lexer.Lex("2 * 3"));     // = 6

// fMod.ToModifier() 将 2*3 变为 R1*3（乘 3 的 modifier）
var chainMod = fBase.Connect(fMod.ToModifier());

var inst2 = runner.Instantiate(chainMod);
Console.WriteLine(inst2.Run()); // 9 = (1+2) * 3
```

多个 modifier 串联：

```csharp
var base = runner.Compile(lexer.Lex("1 + 2")); // FluxFormula
var chain = base.Connect(
    runner.Compile(lexer.Lex("3 * 2")).ToModifier()); // FluxChain

// 再串联 2 个乘 2 的 modifier
for (int i = 0; i < 2; i++)
    chain = chain.Connect(
        runner.Compile(lexer.Lex("3 * 2")).ToModifier());
// 语义: ((3 * 2) * 2) * 2 = 24

var inst3 = runner.Instantiate(chain);
Console.WriteLine(inst3.Run()); // 24
```

## 链 vs 原子：ToAtomic

链式公式可通过 `ToAtomic()` 合并为普通原子公式：

```csharp
var chain = fA.Connect(fB.ToModifier());

// 两条路径结果一致
float perLinkResult = runner.Instantiate(chain).Run();
float atomicResult  = runner.Instantiate(chain.ToAtomic()).Run();

Console.WriteLine(perLinkResult == atomicResult); // True
```

`ToAtomic()` 将所有 link 的字节码完整拼接为单个 `Instruction[]`。链式公式被 JIT 编译或超长链（>8）求值时自动调用。

## 链长超过阈值时的自动合并

链长超过 `FluxConfig.MergeThreshold`（默认 8）时，`Instantiate` 自动调用 `ToAtomic` 合并后求值：

```csharp
FluxChain<float, AdvMathDef> chain = runner.Compile(lexer.Lex("1 + 1"))
    .Connect(runner.Compile(lexer.Lex("2 * 1")).ToModifier());
for (int i = 0; i < 9; i++)
    chain = chain.Connect(
        runner.Compile(lexer.Lex("2 * 1")).ToModifier());

// chain.Length = 11（超过 MergeThreshold 8）
// Instantiate 时自动合并，避免 11 次 per-link 调用
var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // 正常求值
```

用户不需要关心阈值，`Instantiate` 自动选择最优路径。

## ToModifier / ToFormula 往返

Formula ↔ Modifier 转换保持求值等价：

```csharp
var f = runner.Compile(lexer.Lex("7 + 3")); // = 10

// Formula → Modifier → Formula（round-trip）
var mod      = f.ToModifier();
var restored = mod.ToFormula("input");

// 通过新变量名注入原值
var inst = runner.Instantiate(restored).Set("input", 7f);
Console.WriteLine(inst.Run()); // 10（与 f 求值一致）
```

## Modifier 不能独立求值

v3.0.0 中 `FluxModifier<TData, TDef>` 没有 `Instantiate()` 方法，任何尝试独立求值 Modifier 的代码**编译不过**。Modifier 只能通过 `Connect()` 拼接到 Formula 后方，或通过 `ToFormula(varName)` 转为完整 Formula。

## 跨定义类型安全

`FluxFormula<TData, TDef>` 通过 `TDef` 泛型参数绑定到具体定义体。`FluxFormula<float, AdvMathDef>` 和 `FluxFormula<float, GameDef>` 是不同的编译期类型，任何误连的代码编译不过。

## 注意事项

- `ChainReserved.InternalPrefix`（`"CHAIN_LINK_INTERNAL_"`）是链式求值内部使用的变量名前缀。用户不得在 `LexerConfig.VariablePatterns` 中声明此前缀开头的变量
- `FluxChain.Length`、`FluxChain.GetLinks()` 和 `ChainLink` 均为公开 API，高级用户可通过 `GetLinks()` 读取链结构并配合 `VffFormat.ToBytes()` 持久化为 VFF
- `Connect()` 始终返回 `FluxChain<TData, TDef>`：`FluxFormula` 和 `FluxModifier` 不再是链式容器
- `ToMultiplier()` 保留为 `[Obsolete]` 别名，指向 `ToModifier()`
