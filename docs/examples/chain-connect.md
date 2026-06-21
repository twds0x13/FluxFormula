# 示例：ChainLink

以下示例演示 `Connect()` 的链式行为。ChainLink 是 `internal` 结构体，用户通过 `FluxFormula` 的方法间接使用。

## 基础链式 Connect

`Connect()` 要求第二个参数必须是 Modifier。传入 Formula 会直接抛出 `ArgumentException`。必须先用 `.ToMultiplier()` 剥离首操作数：

```csharp
using FluxFormula.Core;

var lexer = CreateMathLexer();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);

var fA = runner.Compile(lexer.Lex("10 + 5"));                // = 15
var fB = runner.Compile(lexer.Lex("2 * 3"));                 // Formula（有首操作数 2）

// ❌ 错误：fB 是 Formula，首操作数会被 fA 的输出静默覆盖
// var chain = fA.Connect(fB);  // throws ArgumentException

// ✅ 正确：先将 fB 转为 Modifier（剥离首操作数 2，改为从 R1 读取输入）
var chain = fA.Connect(fB.ToMultiplier());  // (10+5) * 3 = 45

var inst = runner.Instantiate(chain);
Console.WriteLine(inst.Run()); // 45
```

## Modifier 链：前一个 link 的输出传递给下一个

`ToMultiplier()` 将 Formula 转为 Modifier——移除第一操作数，替换为 R1 总线输入：

```csharp
var fBase = runner.Compile(lexer.Lex("1 + 2"));     // = 3
var fMod  = runner.Compile(lexer.Lex("2 * 3"));     // = 6

// fMod.ToMultiplier() 将 2*3 变为 R1*3（乘 3 的 modifier）
var chainMod = fBase.Connect(fMod.ToMultiplier());

var inst2 = runner.Instantiate(chainMod);
Console.WriteLine(inst2.Run()); // 9 = (1+2) * 3
```

多个 modifier 串联：

```csharp
var current = runner.Compile(lexer.Lex("1 + 2")); // = 3

// 串联 3 个乘 2 的 modifier
for (int i = 0; i < 3; i++)
    current = current.Connect(
        runner.Compile(lexer.Lex("3 * 2")).ToMultiplier());
// 语义: ((3 * 2) * 2) * 2 = 24

var inst3 = runner.Instantiate(current);
Console.WriteLine(inst3.Run()); // 24
```

## 链 vs 原子：ToAtomic

链式公式可通过 `ToAtomic()` 合并为普通原子公式：

```csharp
var chain = fA.Connect(fB.ToMultiplier());

// 两条路径结果一致
float perLinkResult = runner.Instantiate(chain).Run();
float atomicResult  = runner.Instantiate(chain.ToAtomic()).Run();

Console.WriteLine(perLinkResult == atomicResult); // True
```

`ToAtomic()` 将所有 link 的字节码完整拼接为单个 `Instruction[]`。链式公式被 JIT 编译或超长链（>8）求值时自动调用。

## 链长超过阈值时的自动合并

链长超过 `ChainReserved.MergeThreshold`（8）时，`Instantiate` 自动调用 `ToAtomic` 合并后求值：

```csharp
var current = runner.Compile(lexer.Lex("1 + 1"));
for (int i = 0; i < 10; i++)
    current = current.Connect(
        runner.Compile(lexer.Lex("2 * 1")).ToMultiplier());

// chain.ChainLength = 11（超过 8）
// Instantiate 时自动合并，避免 11 次 per-link 调用
var inst = runner.Instantiate(current);
Console.WriteLine(inst.Run()); // 正常求值
```

用户不需要关心阈值——`Instantiate` 自动选择最优路径。

## ToMultiplier / ToFormula 往返

Formula ↔ Modifier 转换保持求值等价：

```csharp
var f = runner.Compile(lexer.Lex("7 + 3")); // = 10

// Formula → Modifier → Formula（round-trip）
var mod     = f.ToMultiplier();
var restored = mod.ToFormula("input");

// 通过新变量名注入原值
var inst = runner.Instantiate(restored).Set("input", 7f);
Console.WriteLine(inst.Run()); // 10（与 f 求值一致）
```

## 注意事项

- `ChainReserved.InternalPrefix`（`"CHAIN_LINK_INTERNAL_"`）是链式求值内部使用的变量名前缀。用户不得在 `LexerConfig.VariablePatterns` 中声明此前缀开头的变量
- Modifier（`FluxType.Modifier`）不能独立 `Run()`，必须作为链的后续 link 使用
- `Connect` 要求第二个参数必须是 Modifier。传入 Formula 会抛出 `ArgumentException`，提示先调用 `.ToMultiplier()`
- `IsChained`、`ChainLength` 和 `ChainLink` 均为 `internal`——用户无需关心公式内部是否为链式表示
