# 示例：VFF 持久化与参数覆写

以下示例演示 VFF（Virtual FluxFormula）的完整生命周期：编译公式、构造链式引用、序列化为 `.vff` 字节数组、从字节反序列化并求值。参数覆写允许在 VFF 中固定部分变量为常量，其余变量由调用方在求值时注入。

依赖的 `MathDef` 定义见[浮点四则运算](/examples/float-math)。

## 编译与缓存

编译两条公式并注入 `FormulaCache`，使 VFF 反序列化时能按哈希查找被引用的字节码：

```csharp
using System;
using System.Globalization;
using System.IO;
using FluxFormula.Core;

var config = new LexerConfig<float>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add), new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul), new("/", (byte)MathOp.Div),
    },
    Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer     = new FluxLexer<float>(config);
var def       = default(MathDef);
var assembler = new FluxAssembler<float, MathDef>(def);

// 编译两条独立公式
var damage   = assembler.Compile(lexer.Lex("[atk] * [mult]"));    // Formula
var reducer  = assembler.Compile(lexer.Lex("[def] * 0.5"));       // Formula

// 计算哈希并注入缓存
byte[] dBytes = damage.ToBytes(), rBytes = reducer.ToBytes();
var dHash = DualHash64.Compute(dBytes);
var rHash = DualHash64.Compute(rBytes);
FormulaCache.Instance.PutBytes(dHash, dBytes);
FormulaCache.Instance.PutBytes(rHash, rBytes);
```

## 构造链式引用

`Connect()` 将两条公式串联为 `FluxChain`，前一条的输出通过 R1 总线传递给后一条：

```csharp
var chain = damage.Connect(reducer.ToModifier());
// 语义: (atk * mult) - def * 0.5
```

`chain.GetLinks()` 返回 `ChainLink[]`，可直接传给 `VffFormat.ToBytes()`。

## 序列化为 VFF

无参数覆写时传空数组：

```csharp
var links = chain.GetLinks().ToArray();
byte[] vffData = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());

// 写入临时文件（模拟持久化 + 重新加载）
string path = Path.GetTempFileName();
File.WriteAllBytes(path, vffData);
```

## 从字节反序列化并求值

加载 VFF 字节数组，反序列化为链式公式后直接求值：

```csharp
byte[] loaded = File.ReadAllBytes(path);
var result = VffFormat.FromBytes<float, MathDef>(loaded);

var instance = assembler.Instantiate(result, jit: true);
instance.Set("atk", 100f).Set("mult", 2f).Set("def", 50f);
float value = instance.Run(); // (100 * 2) - 50 * 0.5 = 175

File.Delete(path);
```

`FromBytes` 内部通过 `FormulaCache` 按哈希查找被引用公式的字节码，调用前须确保依赖公式已注入缓存。

## 参数覆写

VFF 允许在序列化时将链中的变量固定为特定值。覆写类型由 `VffOverrideKind` 指定：

| 类型 | 含义 |
|------|------|
| `Inject` | 求值时由调用方通过 Injector 注入（默认行为） |
| `Constant` | VFF 中已硬编码为固定值，求值时不可覆盖 |

覆写的 `GlobalSlot` 是变量在合并管道中的 Immediate 全局序号，可通过 `ChainLink.VarSlots` 查找：

```csharp
// 查找 "mult" 的全局序号
int multSlot = -1;
foreach (var link in chain.GetLinks())
    foreach (var vs in link.VarSlots)
        if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }

// 将 "mult" 固定为 2.0，"atk" 和 "def" 保留为运行时注入
var overrides = new[]
{
    new VffOverride<float>(multSlot, VffOverrideKind.Constant, 2f),
};

byte[] vffWithOverride = VffFormat.ToBytes<float>(
    chain.GetLinks().ToArray(), overrides);
```

反序列化后，"mult" 已被硬编码，调用方仅需注入剩余变量：

```csharp
string path2 = Path.GetTempFileName();
File.WriteAllBytes(path2, vffWithOverride);

byte[] loaded2 = File.ReadAllBytes(path2);
var result2 = VffFormat.FromBytes<float, MathDef>(loaded2);

var inst2 = assembler.Instantiate(result2, jit: true)
    .Set("atk", 100f).Set("def", 30f);
float value2 = inst2.Run(); // (100 * 2) - 30 * 0.5 = 185
// "mult" 无法通过 Set() 覆盖，Instantiate 已自动应用 VFF Constant 覆写

File.Delete(path2);
```

## 通过缓存哈希解析

如果 VFF 字节已通过 `PutBytes` 注入缓存，可使用 `Resolve()` 按哈希查找，避免自行管理字节数组：

```csharp
var vffHash = DualHash64.Compute(vffData);
FormulaCache.Instance.PutBytes(vffHash, vffData);

// 后续仅需哈希即可解析
var result3 = VffFormat.Resolve<float, MathDef>(vffHash);
```

## 注意事项

- `FormulaCache` 需在 `FromBytes` / `Resolve` 调用前包含所有被引用公式的字节码，否则抛出 `InvalidOperationException`
- 循环引用（VFF 引用自身或间接引用自身）在解析时被检测并拒绝
- `FluxType` 为 internal enum，调用方通过 `FluxChain.GetLinks()` 获取 ChainLink 即可，无需手动构造
- VFF 版本号当前为 1，未来版本升级时 `FromBytes` 会校验 `Version` 字段
