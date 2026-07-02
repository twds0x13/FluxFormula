# 自定义字面量扫描器

`LiteralScanner` 是 `LexerConfig<TData>` 提供的底层扩展点：一个零分配的 Span 扫描器委托，可完全替代内置数字扫描器，实现任意字面量语法。

## 签名

```csharp
public delegate int LiteralScanner<TData>(
    ReadOnlySpan<char> src,  // 完整源码
    int pos,                 // 当前扫描位置
    out TData value          // 匹配成功时输出解析后的值
);
```

- **返回值 `pos`**：未匹配，词法器继续尝试变量、运算符、括号
- **返回值 `> pos`**：匹配成功，消费了 `pos` 到返回值之间的字符
- **`out TData value`**：匹配成功时写入解析后的值；未匹配时写入 `default`

## 与 LiteralParser 的关系

| | LiteralParser | LiteralScanner |
|---|---|---|
| 类型 | `Func<string, TData>` | 委托 `(ReadOnlySpan<char>, int, out TData) -> int` |
| 输入 | 已匹配的子串（`string`） | 完整源码 + 起始位置 |
| 控制扫描边界 | 否 | 是 |
| 零分配 | 否（传 `string`） | 是（Span 操作） |
| 何时被调用 | 仅被内置默认扫描器调用 | 设置后完全替代默认扫描器 |

关键规则：设置 `LiteralScanner` 后 `LiteralParser` 不会被调用，但构造函数仍要求 `LiteralParser` 非 null（作为降级回退的占位）。可赋值为 `_ => default`。

## LiteralPattern 字段

`config.LiteralPattern` 在运行时不被读取。它仅为 Unity 编辑器的文档参考字段，对词法分析行为无任何影响。实际行为完全由 `LiteralScanner`（或未设置时的默认扫描器）控制。

## 默认扫描器

未设置 `LiteralScanner` 时，`FluxLexer` 构造函数调用 `CreateDefaultNumberScanner(LiteralParser)` 生成内置扫描器，行为等价于逐字符匹配 `\d+(\.\d+)?[fF]?`：

1. 检查当前位置是否为数字
2. 扫描整数部分
3. 可选：`.小数部分`
4. 可选：`f` 或 `F` 后缀
5. 调用 `LiteralParser(matchedSubstring)` 转换为 `TData`

## 示例

### 十六进制整数

匹配 `0xFF` 格式的十六进制字面量：

```csharp
var hexScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 2 >= src.Length) return pos;
    if (src[pos] != '0' || (src[pos + 1] != 'x' && src[pos + 1] != 'X'))
        return pos;

    int end = pos + 2;
    while (end < src.Length && IsHexDigit(src[end])) end++;
    if (end == pos + 2) return pos; // 0x 后无数字

    value = ParseHex(src.Slice(pos + 2, end - pos - 2));
    return end;
};

static bool IsHexDigit(char c) =>
    char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
```

关键点：`0x` 前缀检查失败时返回 `pos`，词法器会回退并将 `0` 识别为普通十进制数字。

### 关键字字面量

将 `true` / `false` 匹配为字面量并映射为 `1` / `0`：

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 4 <= src.Length && src.Slice(pos, 4).SequenceEqual("true"))
    {
        value = 1;
        return pos + 4;
    }
    if (pos + 5 <= src.Length && src.Slice(pos, 5).SequenceEqual("false"))
    {
        value = 0;
        return pos + 5;
    }
    return pos;
};
```

`Span.SequenceEqual` 是零分配的字符串前缀匹配方式。

### 不做任何事

始终返回 `pos` 的扫描器等价于未设置扫描器时的数字匹配行为，同时还展示了词法器的回退机制：

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out float v) =>
{
    v = 0;
    return pos; // 永不匹配字面量，词法器继续尝试运算符等
};
```

## 注意点

- 扫描器应逐字符前进，不应使用正则。正则引入的堆分配抵消了 Span 的零分配优势
- `ToString()` / `float.Parse` 等操作产生编译期一次性分配，不进入执行热路径
- 自定义扫描器与 `VariablePatterns` 完全兼容：词法器先尝试扫描器，未匹配才尝试变量模式
- `TData : unmanaged` 约束排除了 `string` 等引用类型。需要编码额外信息时使用 `enum` 或 `byte` 字段
