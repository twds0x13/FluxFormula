# Feature: Lexer 层 + 流式注入优化

> 状态：A 已完成（v1.1.0-v1.3.0），B 部分完成 | 发起：2026-06-18

## 功能拆分

| # | 功能 | 说明 | 状态 |
|---|------|------|:--:|
| A | **Lexer（词法分析器）** | 字符串 → `FluxToken[]`，可自定义符号映射规则 | ✅ 已交付 |
| B | **流式注入优化** | 统一解释器/JIT 注入路径，减少不必要拷贝 | ⚠️ 部分完成 |

---

# A. Lexer

## 定位

```
"(1 + 2) * 3.f"
     │
     ▼
  Lexer.Parse("(1 + 2) * 3.f")
     │  1. 切分词素
     │  2. 查符号表 → TOper
     │  3. 解析字面量 → TData
     ▼
  FluxToken<float, FloatOp>[]
     │
     ▼
  FluxAssembler.Compile()  ← 已有，不动
     │
     ▼
  Instruction[]
```

## 输入 / 输出示例

```
输入: "(1 * 2) + (3 / 4)"
输出: [
  { Oper: LParen },
  { Oper: Const, Data: 1 },
  { Oper: Mul },
  { Oper: Const, Data: 2 },
  { Oper: RParen },
  { Oper: Add },
  { Oper: LParen },
  { Oper: Const, Data: 3 },
  { Oper: Div },
  { Oper: Const, Data: 4 },
  { Oper: RParen },
]
```

## 可自定义的维度

| 维度 | 示例 | 难度 |
|------|------|:--:|
| **运算符符号** | `+`→Add, `*`→Mul, `&&`→And, `**`→Pow | 低 |
| **括号类型** | `()`、`[]`、`{}` 映射到不同 Pair | 低 |
| **数字格式** | `42`、`3.14f`、`0xFF`、`1e-4` | 中 |
| **标识符/变量** | `PI`→3.14159、`x`→寄存器索引 | 中 |
| **注释** | `//` 行注释、`/* */` 块注释 | 中 |
| **函数调用** | `sin(x)` → OpPair EmitOnMatch | 高（需语法支持） |
| **自定义语法格式** | XML-like `<add><c>1</c><c>2</c></add>` | 高（需结构解析） |

## 技术选型

### 方案 1：手写递归下降分词器（推荐起步）

- **零依赖**，完全可控
- 参考 .NET `StringTokenizer` 或 Unity 的 `Lexer` 模式
- 适合：数学表达式、类 C# 格式
- 不足：复杂嵌套格式（XML）需额外处理

### 方案 2：正则分词 + 配置表

```csharp
var lexer = new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
{
    TokenPatterns = new[]
    {
        new(@"\d+\.?\d*f?",         FloatOp.Const,  TokenType.Immediate),
        new(@"\+",                  FloatOp.Add,    TokenType.Operator),
        new(@"\-",                  FloatOp.Sub,    TokenType.Operator),
        new(@"\*",                  FloatOp.Mul,    TokenType.Operator),
        new(@"/",                   FloatOp.Div,    TokenType.Operator),
        new(@"\(",                  FloatOp.LParen, TokenType.Operator),
        new(@"\)",                  FloatOp.RParen, TokenType.Operator),
    },
    LiteralParser = s => float.Parse(s.TrimEnd('f')),
    IgnorePattern = @"\s+",
});
```

### 方案 3：Sprache / Pidgin（C# Parser Combinator）

- NuGet 包，Unity 兼容（纯 C#，无 native 依赖）
- **Sprache**：面向对象风格，适合快速原型
- **Pidgin**：值类型风格，更高的性能天花板
- 适合：需要嵌套结构（函数调用、条件表达式）

### 方案对比

| | 手写分词器 | 正则+配置表 | Sprache/Pidgin |
|------|:--:|:--:|:--:|
| 依赖 | 零 | 零 | NuGet 包 |
| 数学表达式 | ✅ | ✅ | ✅ |
| 自定义运算符 | 代码改动 | 配置表 | 组合子 |
| XML/嵌套格式 | ❌ 困难 | ❌ 困难 | ✅ |
| 函数调用 (sin(x)) | 中等 | 中等 | ✅ |
| 性能 | 最优 | 良好 | 良好 |
| 学习曲线 | 低 | 低 | 中 |

### 实际实现

**最终选型：手写 Span 扫描器（方案 1）**。零依赖，`ReadOnlySpan<char>` 逐字符扫描。零 Regex，零 Parser Combinator 依赖。

```
┌────────────────────────────────────────┐
│  FluxLexer（推荐方式）                  │
│  用户填 LexerConfig 配置表              │
│  内部手写 ReadOnlySpan<char> 扫描器     │
│  零依赖，零 Regex                      │
└────────────────────────────────────────┘
```

用户配一份 `LexerConfig` 即可使用。Lexer 内部对运算符按长度降序排序，长符号优先匹配（`**` 在 `*` 之前尝试）。

### 架构接口草案

```csharp
/// <summary>
/// 词法分析器：将字符串转换为 FluxToken 流。
/// 内部使用 Pidgin 实现，用户通过 LexerConfig 配置。
/// </summary>
public class FluxLexer<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
{
    /// <summary>从配置表构建（推荐方式，零 Pidgin 知识）</summary>
    public FluxLexer(LexerConfig<TData, TOper> config);

    /// <summary>从用户手写的 Pidgin 解析器桥接（高级模式）</summary>
    public static FluxLexer<TData, TOper> FromPidgin(
        Parser<char, FluxToken<TData, TOper>> customParser,
        Func<string, TData> literalParser);

    /// <summary>解析字符串为 Token 数组</summary>
    public FluxToken<TData, TOper>[] Lex(string source);
}

/// <summary>
/// 纯配置驱动的词法规则表。
/// 用户不需要理解 Pidgin，填表即可使用。
/// </summary>
public class LexerConfig<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
{
    /// <summary>空白/注释模式（被跳过）</summary>
    public string WhitespacePattern = @"\s+";

    /// <summary>字面量 → TData 转换函数</summary>
    public Func<string, TData> LiteralParser;

    /// <summary>运算符规则列表</summary>
    public List<OperatorRule<TOper>> Operators = new();

    /// <summary>括号规则列表</summary>
    public List<BracketRule<TOper>> Brackets = new();

    /// <summary>数字/标识符 正则模式</summary>
    public string LiteralPattern = @"\d+(\.\d+)?f?";
}

/// <summary>运算符映射规则</summary>
public record OperatorRule<TOper>(
    string Symbol,    // 如 "+", "*", "&&", "**"
    TOper Oper        // 关联的枚举值
);

/// <summary>括号映射规则</summary>
public record BracketRule<TOper>(
    string Open,      // 如 "(", "[", "{"
    string Close,     // 如 ")", "]", "}"
    TOper LeftOper,   // 左括号枚举
    TOper RightOper   // 右括号枚举
);
```

## 使用示例

### Layer 1：填表即用（推荐）

```csharp
var lexer = new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
{
    LiteralPattern = @"\d+(\.\d+)?f?",
    LiteralParser  = s => float.Parse(s.TrimEnd('f')),

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
});

var tokens = lexer.Lex("(1.5f + 2f) * 3f");
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);
float r = runner.Build(tokens, jit: true).Run(); // 10.5
```

### Layer 2：手写 Pidgin 解析器（高级）

```csharp
// 用户自己写的 Pidgin 解析器
var myParser = OneOf(
    DecimalNum.Select(v => Token(MyOp.Const, v)),
    Char('+').ThenReturn(Token(MyOp.Add, 0f)),
    // ...
);

// 通过 FromPidgin 桥接，享受与 FluxLexer 相同的下游链路
var lexer = FluxLexer<float, FloatOp>.FromPidgin(myParser, s => float.Parse(s));
var tokens = lexer.Lex("1 + 2");
```

---

# B. 流式注入优化

（原内容保留，待 A 完成后继续讨论）

## 当前痛点

1. 解释器路径：`buffer.ToArray()` 复制整份公式缓冲
2. 每次 `Set()` 返回新 `FluxInjector` 副本（`readonly struct`）
3. 两次扫描 `CreateInjector()`
4. 解释器和 JIT 注入路径不统一

## 待定方案

（等你对 A 部分反馈后继续讨论）

---

## 实施检查表

- [ ] A. Lexer 方案确认（方案 2 or 方案 3）
- [ ] A. 接口设计定稿
- [ ] A. 实现 + 单元测试
- [ ] B. 注入路径统一
- [ ] 文档更新 (guide/lexer.md)
