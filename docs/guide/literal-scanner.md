# 自定义字面量扫描器

`LiteralScanner` 是 `LexerConfig<TData>` 的必设字段：一个零分配的 Span 扫描器委托，控制词法分析器如何识别数字、关键字等字面量。

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

## 默认扫描器

简单数字格式（整数、浮点数）使用 `CreateDefaultNumberScanner`，无需手写扫描器：

```csharp
config.LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
    s => float.Parse(s.TrimEnd('f', 'F')));
```

它等价于逐字符匹配 `\d+(\.\d+)?[fF]?`：

1. 检查当前位置是否为数字
2. 扫描整数部分
3. 可选：`.小数部分`
4. 可选：`f` 或 `F` 后缀
5. 调用传入的 parser 函数转换为 `TData`

`CreateDefaultNumberScanner` 内部通过 `ToString()` 将匹配到的 Span 转为 string 再调用 parser，产生编译期一次性分配。对分配有要求的场景（如零字符串分配），使用自定义扫描器直接在 Span 上解析。

## 示例

### 十六进制整数

匹配 `0xFF` 格式的十六进制字面量：

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
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

始终返回 `pos` 的扫描器，词法器回退到尝试运算符等其他匹配：

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
- `LiteralScanner` 必须设置，否则构造函数抛出 `ArgumentException`
