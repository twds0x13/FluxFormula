# FluxLexer / LexerConfig

当你需要将字符串表达式转换为 Token 流时，`FluxLexer` 是整条管线的入口。它不依赖字典查找或堆分配集合：构造时预索引所有规则，`Lex()` 热路径中仅做手写 Span 遍历。

## 签名

```csharp
public class FluxLexer<TData>
    where TData : unmanaged

public class LexerConfig<TData>
    where TData : unmanaged

public readonly struct LexResult<TData>
    where TData : unmanaged
```

**关联规则类型：**

```csharp
public struct VariablePatternRule          // 变量模式：前缀 + 后缀
public readonly struct BracketRule         // 括号对映射：开/关符号 + 操作码
public readonly struct OperatorRule        // 运算符语法视图：符号 + 操作码 + Slots/Aux
```

## LexerConfig 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `LiteralOper` | `byte` | 字面量对应的操作码（如 `(byte)MathOp.Const`） |
| `LiteralScanner` | `LiteralScanner<TData>` | 字面量扫描器委托。简单数字格式使用 `CreateDefaultNumberScanner` |
| `Operators` | `List<OperatorRule>` | 运算符映射列表（按长度自动降序匹配，无需手动排序） |
| `Brackets` | `List<BracketRule>` | 括号映射列表 |
| `ImplicitOperators` | `List<byte>` | 可隐式插入的运算符列表（如 `(byte)MathOp.Mul`）。单条目时 `2(3)` 和 `(a)(b)` 自动插入；多条目歧义时抛异常 |
| `VariablePatterns` | `List<VariablePatternRule>` | 变量模式列表，如 `new("[", "]")` 匹配 `[x]` |
| `WhitespacePattern` | `string` | 空白/注释正则（匹配的内容被跳过）。默认 `@"\s+"` |

### 辅助类型

**OperatorRule:**
| 字段 | 类型 | 说明 |
|------|------|------|
| `Symbol` | `string` | 中轴符号（如 `"+"`, `"cross"`, `"?"`） |
| `Oper` | `byte` | 后端 opcode |
| `Slots` | `sbyte[]` | 操作数位置偏移数组（中轴 = 0）。null 表示使用 IFluxDefinition 默认 |
| `Aux` | `AuxRule[]` | 辅助符号约束（括号/分隔符）。null 表示无额外约束 |
| `BracketOpen` / `BracketClose` | `string` | 函数调用括号符号（如 `"("`, `")"`）。null 表示不使用括号语法 |

**BracketRule:**
| 字段 | 类型 | 说明 |
|------|------|------|
| `Open` / `Close` | `string` | 开/关括号符号 |
| `LeftOper` / `RightOper` | `byte` | 开/关括号对应的操作码 |

**VariablePatternRule:**
| 字段 | 类型 | 说明 |
|------|------|------|
| `Prefix` / `Suffix` | `string` | 变量包裹符号（如 `"[", "]"` 或 `"{var:", "}"`） |

## 方法

### FluxLexer 构造

```csharp
public FluxLexer(LexerConfig<TData> config)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `config` | `LexerConfig<TData>` | 词法规则配置。不可为 null |

构造时预索引所有运算符（按长度降序）、括号和变量模式，后续 `Lex()` 调用零分配。

### Lex

```csharp
public LexResult<TData> Lex(string source)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `source` | `string` | 源码字符串。空字符串返回空结果 |

返回 `LexResult<TData>`，包含 `Tokens`（`FluxToken<TData>[]`）和 `VarNames`（`string[]`，非变量位置为 null）。

### CreateDefaultNumberScanner（静态）

```csharp
public static LiteralScanner<TData> CreateDefaultNumberScanner(
    Func<string, TData> parser)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `parser` | `Func<string, TData>` | 字符串到 TData 的转换函数 |

返回匹配 `\d+(\.\d+)?[fF]?` 格式的扫描器委托。等价于手动设置 `LexerConfig.LiteralScanner`。

## 使用示例

#### 基本四则运算配置

```csharp
var config = new LexerConfig<float>
{
    LiteralOper    = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer = new FluxLexer<float>(config);
var result = lexer.Lex("1 + 2 * 3");
// result.Tokens → [Const(1), Add, Const(2), Mul, Const(3)]
```

#### 隐式乘法

```csharp
config.ImplicitOperators.Add((byte)MathOp.Mul);
// "2(3)" 自动解析为 "2 * (3)"
// "(a)(b)" 自动插入乘号
```

#### 自定义变量语法

```csharp
config.VariablePatterns.Add(new VariablePatternRule("{var:", "}"));
// 匹配 "{var:damage}" → 变量名 "damage"
```

## 参见

- [FluxToken](./flux-token) — 词法 Token 结构体
- [FluxAssembler](./flux-assembler) — 接收 LexResult 的编译入口
- [IFluxDefinition](./idefinition) — 运算符行为定义
