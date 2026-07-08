# 字面量扫描器

控制词法分析器如何识别数字、关键字等字面量。v5.0 起 source generator 驱动的 `[LiteralTemplate]` 是推荐方式: 在 TData struct 上声明格式模板，编译器自动生成零分配 span 扫描代码。

## 推荐方式: `[LiteralTemplate]`

在 struct 上加一个 attribute，source generator 在编译期生成扫描器。`LexerConfig.LiteralScanner` 不再是必设字段:

```csharp
[LiteralTemplate("<float X> <float Y>")]
public struct Point2D
{
    public float X;
    public float Y;
}

// 无需设置 LiteralScanner
var config = new LexerConfig<Point2D>
{
    LiteralOper = 0,
    Operators = { new("+", 1), new("-", 2) },
};
var lexer = new FluxLexer<Point2D>(config);
var result = lexer.Lex("3.5 -2.1");
// result.Tokens[0].Data → Point2D { X = 3.5, Y = -2.1 }
```

**运行时优先级**: 词法分析器构造时先查 `LiteralScanners.TryGetScanner<TData>()`（有 `[LiteralTemplate]` 时命中），未命中回退到 `config.LiteralScanner` 手动委托，两者都无则抛出 `ArgumentException`。

## 模板语法

### 紧凑格式

```csharp
[LiteralTemplate("<float Damage>|<optional>draw <int Count>|</optional>idx:<int Index>")]
public struct SpellCard { ... }
```

`<type fieldname>` 声明一个字段，其余裸文字按精确字符匹配。上例匹配 `10.5|draw 2|idx:1` 或 `10.5|idx:0`（`draw` 段可选）。

### XML 格式

与紧凑格式语义等价，适合需要精确控制空白的场景:

```xml
<literal-template>
  <field type="float" name="X"/>
  <text>, </text>
  <field type="float" name="Y"/>
</literal-template>
```

匹配 `3.5, -2.1`。`<text>` 元素包裹精确匹配字符，`<field>` 声明字段，`<optional>` 包裹可选块。

### 多行模板

模板支持 C# raw string literal 多行写法，换行在解析时规范化为空格:

```csharp
[LiteralTemplate("""
    <float X>
    <float Y>
    """)]
public struct PointMultiLine { public float X; public float Y; }
```

## 可选块 `<optional>`

`<optional>...</optional>` 包裹的片段在输入中可以不出现。匹配逻辑: 保存当前位置，尝试匹配块内内容，成功则继续，失败则恢复位置跳过后继续匹配后续模板。

```csharp
[LiteralTemplate("<float Damage>|<optional>draw <int DrawsProvide>|</optional>idx:<int StartIndex>")]
public struct SpellCard
{
    public float Damage;
    public int DrawsProvide;
    public int StartIndex;
}
```

`"10.5|draw 2|idx:1"` 完整匹配全部字段；`"10.5|idx:0"` 中 `draw` 段缺失，`DrawsProvide` 保持 `default`。

## 嵌套结构体

模板中的类型名可以引用另一个有 `[LiteralTemplate]` 的 struct，生成器自动递归生成嵌套扫描:

```csharp
[LiteralTemplate("<float X> <float Y> <float Z>")]
public struct Vec3 { public float X, Y, Z; }

[LiteralTemplate("(<Vec3 Pos>)")]
public struct Entity { public Vec3 Pos; }

[LiteralTemplate("[<Entity Member>]")]
public struct Team { public Entity Member; }
```

`[(10 20 30)]` → Team → Entity → Vec3，三级递归。依赖图自动拓扑排序，循环依赖触发 FLX002 编译错误。

## 外部类型

对于无法直接修改源码的第三方 struct，使用 `[ExternalLiteralTemplate]`:

```csharp
[assembly: ExternalLiteralTemplate(typeof(UnityEngine.Vector3),
    "<float x> <float y> <float z>")]

// 或在 class/struct 上
[ExternalLiteralTemplate(typeof(SomeExternalStruct), "<int A> <int B>")]
public class MyBehaviour { ... }
```

`ExternalLiteralTemplate` 的 Priority B 语义: 若同一类型同时有 `[LiteralTemplate]` 和 `[ExternalLiteralTemplate]`，后者覆盖前者。

## 类型别名

用 `[LiteralTypeAlias]` 给内置类型起领域语义名称，纯语法糖，不改变解析逻辑:

```csharp
[assembly: LiteralTypeAlias("Distance", "float")]
[assembly: LiteralTypeAlias("Health", "int")]

// 模板中可使用别名
[LiteralTemplate("<Distance Range> <Health HP>")]
public struct WeaponStats { public float Range; public int HP; }
```

## 内置类型

source generator 支持 12 种 C# 内置 unmanaged 类型，每个有对应的 `LiteralTemplateRegistry.Scan_Xxx` 方法:

| 别名 | C# 类型 | 识别格式 |
|------|---------|---------|
| `float` | `float` | `-?\d+(\.\d+)?[fF]?` |
| `double` | `double` | `-?\d+(\.\d+)?([eE][+-]?\d+)?[dD]?` |
| `int` | `int` | `-?\d+` |
| `uint` | `uint` | `\d+` |
| `long` | `long` | `-?\d+[lL]?` |
| `ulong` | `ulong` | `\d+[uU]?[lL]?` |
| `short` | `short` | `-?\d+` |
| `ushort` | `ushort` | `\d+` |
| `byte` | `byte` | `\d+` |
| `sbyte` | `sbyte` | `-?\d+` |
| `bool` | `bool` | `true` / `false` |
| `char` | `char` | 单字符 |

所有内置扫描器均为零分配、`AggressiveInlining` 标注的 span 方法。

## 编译器诊断

| ID | Severity | 含义 |
|----|----------|------|
| FLX001 | Error | 模板语法/格式错误 |
| FLX002 | Error | 模板类型之间存在循环依赖 |
| FLX003 | Error | `readonly struct` 不能使用 `[LiteralTemplate]`（字段赋值需要可变 struct） |
| FLX004 | Warning | 模板引用了未注册 `[LiteralTemplate]` 或 `[ExternalLiteralTemplate]` 的类型（字段被跳过） |

## 手动委托: 进阶/回退

当字面量语法过于不规则、模板无法表达时，使用手写 `LiteralScanner<TData>` 委托。有 `[LiteralTemplate]` 时无需设置此字段，但设置了也不会冲突: 生成式扫描器优先。

### 签名

```csharp
public delegate int LiteralScanner<TData>(
    ReadOnlySpan<char> src,  // 完整源码
    int pos,                 // 当前扫描位置
    out TData value          // 匹配成功时输出解析后的值
);
```

- **返回值 `pos`**: 未匹配，词法器继续尝试变量、运算符、括号
- **返回值 `> pos`**: 匹配成功，消费了 `pos` 到返回值之间的字符
- **`out TData value`**: 匹配成功时写入解析后的值；未匹配时写入 `default`

### 默认扫描器

简单数字格式使用 `CreateDefaultNumberScanner`，无需手写扫描器:

```csharp
config.LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
    s => float.Parse(s.TrimEnd('f', 'F')));
```

等价于逐字符匹配 `\d+(\.\d+)?[fF]?`，调用传入的 parser 函数转换为 `TData`。`CreateDefaultNumberScanner` 内部通过 `ToString()` 将 Span 转为 string 再调用 parser，产生编译期一次性分配。

### 十六进制整数

匹配 `0xFF` 格式:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out int value) =>
{
    value = 0;
    if (pos + 2 >= src.Length) return pos;
    if (src[pos] != '0' || (src[pos + 1] != 'x' && src[pos + 1] != 'X'))
        return pos;

    int end = pos + 2;
    while (end < src.Length && IsHexDigit(src[end])) end++;
    if (end == pos + 2) return pos;

    value = ParseHex(src.Slice(pos + 2, end - pos - 2));
    return end;
};

static bool IsHexDigit(char c) =>
    char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
```

关键点: `0x` 前缀检查失败时返回 `pos`，词法器会回退并将 `0` 识别为普通十进制数字。

### 关键字字面量

将 `true` / `false` 匹配为字面量并映射为 `1` / `0`:

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

始终返回 `pos` 的扫描器，词法器回退到运算符等其他匹配:

```csharp
config.LiteralScanner = (ReadOnlySpan<char> src, int pos, out float v) =>
{
    v = 0;
    return pos;
};
```

## 注意点

- 扫描器应逐字符前进，不应使用正则。正则引入的堆分配抵消了 Span 的零分配优势
- `ToString()` / `float.Parse` 等操作产生编译期一次性分配，不进入执行热路径
- 自定义扫描器与 `VariablePatterns` 完全兼容: 词法器先尝试扫描器，未匹配才尝试变量模式
- `TData : unmanaged` 约束排除了 `string` 等引用类型。需要编码额外信息时使用 `enum` 或 `byte` 字段
- 何时使用手动委托: 模板无法表达的不规则语法、或 source generator 不可用（pre-C# 12）
