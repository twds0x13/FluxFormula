# Feature: Lexer 层 + 流式注入优化

> 状态：A 已完成（v1.1.0-v1.3.0），B 部分完成 | 发起：2026-06-18 | 代码示例已更新至 v3.0.0 API

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
     │  2. 查符号表 → byte 操作码
     │  3. 解析字面量 → TData
     ▼
  FluxToken<float>[]
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
  { Oper: (byte)MathOp.LParen },
  { Oper: (byte)MathOp.Const, Data: 1 },
  { Oper: (byte)MathOp.Mul },
  { Oper: (byte)MathOp.Const, Data: 2 },
  { Oper: (byte)MathOp.RParen },
  { Oper: (byte)MathOp.Add },
  { Oper: (byte)MathOp.LParen },
  { Oper: (byte)MathOp.Const, Data: 3 },
  { Oper: (byte)MathOp.Div },
  { Oper: (byte)MathOp.Const, Data: 4 },
  { Oper: (byte)MathOp.RParen },
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
var lexer = new FluxLexer<float, MathDef>(new LexerConfig<float, MathDef>
{
    TokenPatterns = new[]
    {
        new(@"\d+\.?\d*f?",         (byte)MathOp.Const,  TokenType.Immediate),
        new(@"\+",                  (byte)MathOp.Add,    TokenType.Operator),
        new(@"\-",                  (byte)MathOp.Sub,    TokenType.Operator),
        new(@"\*",                  (byte)MathOp.Mul,    TokenType.Operator),
        new(@"/",                   (byte)MathOp.Div,    TokenType.Operator),
        new(@"\(",                  (byte)MathOp.LParen, TokenType.Operator),
        new(@"\)",                  (byte)MathOp.RParen, TokenType.Operator),
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
/// </summary>
public class FluxLexer<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
{
    /// <summary>从配置表构建（推荐方式）</summary>
    public FluxLexer(LexerConfig<TData, TDef> config);

    /// <summary>解析字符串为 Token 数组</summary>
    public LexResult<TData> Lex(string source);
}

/// <summary>
/// 纯配置驱动的词法规则表。
/// </summary>
public class LexerConfig<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxJITDefinition<TData>
{
    public byte LiteralOper;
    public Func<string, TData> LiteralParser;
    public List<OperatorRule> Operators = new();
    public List<BracketRule> Brackets = new();
    public List<VariablePatternRule> VariablePatterns = new();
    public List<byte> ImplicitOperators = new();
}

public record OperatorRule(string Symbol, byte Oper);
public record BracketRule(string Open, string Close, byte LeftOper, byte RightOper);
```

## 使用示例

### Layer 1：填表即用（推荐）

```csharp
var lexer = new FluxLexer<float, MathDef>(new LexerConfig<float, MathDef>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralParser = s => float.Parse(s.TrimEnd('f')),

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
});

var tokens = lexer.Lex("(1.5f + 2f) * 3f");
var runner = new FluxAssembler<float, MathDef>(def);
float r = runner.Build(tokens.Tokens, jit: true).Run(); // 10.5
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

- [x] A. Lexer 方案确认（方案 1：手写 Span）
- [x] A. 接口设计定稿
- [x] A. 实现 + 单元测试
- [ ] B. 注入路径统一
- [ ] 文档更新 (guide/lexer.md)
