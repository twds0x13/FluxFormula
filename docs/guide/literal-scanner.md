# 字面量扫描器

控制词法分析器如何识别数字、关键字等字面量。底层由 [SourceSerializer](https://github.com/twds0x13/SourceSerializer) v1.2.0 驱动：在 TData struct 上声明 `[Template]` 属性，source generator 在编译期生成零分配 span 扫描代码。

## `[Template]` 自动生成

在 struct 上加一个 attribute，source generator 在编译期生成扫描器。`LexerConfig.LiteralScanner` 不再是必设字段:

```csharp
[Template("<float X> <float Y>")]
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

**运行时优先级**: 词法分析器构造时先查 `SerializerScanners.TryGetScanner<TData>()`（有 `[Template]` 时命中），未命中回退到 `config.LiteralScanner` 手动委托，两者都无则抛出 `ArgumentException`。

## 模板语法

### 紧凑格式

```csharp
[Template("<float Damage>|<optional>draw <int Count>|</optional>idx:<int Index>")]
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
[Template("""
    <float X>
    <float Y>
    """)]
public struct PointMultiLine { public float X; public float Y; }
```

## 可选块 `<optional>`

`<optional>...</optional>` 包裹的片段在输入中可以不出现。匹配逻辑: 保存当前位置，尝试匹配块内内容，成功则继续，失败则恢复位置跳过后继续匹配后续模板。

```csharp
[Template("<float Damage>|<optional>draw <int DrawsProvide>|</optional>idx:<int StartIndex>")]
public struct SpellCard
{
    public float Damage;
    public int DrawsProvide;
    public int StartIndex;
}
```

`"10.5|draw 2|idx:1"` 完整匹配全部字段；`"10.5|idx:0"` 中 `draw` 段缺失，`DrawsProvide` 保持 `default`。

## 嵌套结构体

模板中的类型名可以引用另一个有 `[Template]` 的 struct，生成器自动递归生成嵌套扫描:

```csharp
[Template("<float X> <float Y> <float Z>")]
public struct Vec3 { public float X, Y, Z; }

[Template("(<Vec3 Pos>)")]
public struct Entity { public Vec3 Pos; }

[Template("[<Entity Member>]")]
public struct Team { public Entity Member; }
```

`[(10 20 30)]` → Team → Entity → Vec3，三级递归。依赖图自动拓扑排序，循环依赖触发 SSR002 编译错误。

## 外部类型

对于无法直接修改源码的第三方 struct，使用 `[ExternalTemplate]`:

```csharp
[assembly: ExternalTemplate(typeof(UnityEngine.Vector3),
    "<float x> <float y> <float z>")]

// 或在 class/struct 上
[ExternalTemplate(typeof(SomeExternalStruct), "<int A> <int B>")]
public class MyBehaviour { ... }
```

`ExternalTemplate` 的 Priority B 语义: 若同一类型同时有 `[Template]` 和 `[ExternalTemplate]`，后者覆盖前者。

## 类型别名

用 `[TypeAlias]` 给内置类型起领域语义名称，纯语法糖，不改变解析逻辑:

```csharp
[assembly: TypeAlias("Distance", "float")]
[assembly: TypeAlias("Health", "int")]

// 模板中可使用别名
[Template("<Distance Range> <Health HP>")]
public struct WeaponStats { public float Range; public int HP; }
```

## 枚举标签

v5.5+ 支持 `[Tag]` 属性标注枚举成员，使模板可直接识别字符串标签:

```csharp
public enum Element : byte
{
    Physical = 0,
    [Tag("fire")]  Fire,
    [Tag("ice")]   Ice,
    [Tag("magic")] Magic,
}

[Template("<float Amount><optional>:<Element Element></optional>")]
public struct ElemValue
{
    public float Amount;
    public Element Element;
}
```

生成的扫描器自动包含 `switch(new string(src.Slice(...)))` 分支，将 `"fire"` 映射到 `Element.Fire`。模板匹配 `42`、`-5`、`1.5:fire`、`100:ice`。

::: tip
`[Tag]` 使手写委托不再是必需方案。大部分带标签后缀的字面量格式现在都可以用模板表达。
:::

## 序列化方向（Emit）

`[Template]` 同时生成反序列化（scan）和序列化（emit）两条路径。`SerializerEmitters.TryGetEmitter<T>()` 将 struct 实例写回 `StringBuilder`，输出与模板格式一致：

```csharp
[Template("<float X> <float Y>")]
public struct Point2D { public float X; public float Y; }

SerializerEmitters.TryGetEmitter<Point2D>(out var emit);
var sb = new StringBuilder();
emit(sb, new Point2D { X = 3.5f, Y = -2.1f });
// sb.ToString() == "3.5 -2.1"
```

Emit 方向支持裸文字、字段序列化、可选块。`<repetition>` 块的序列化延后到 managed Walk 阶段实现。

## 泛型集合自动解析

字段类型为 `List<T>` 或 `Dictionary<K,V>` 时，source generator 自动从内置的开放泛型模板合成解析器。调用方无需为集合类型手动编写 `[Template]`：

```csharp
[Template("<float Value>")]
public struct NamedValue { public float Value; }

[Template("<repetition>, <List<NamedValue> Items></repetition>")]
public struct Container { public List<NamedValue> Items; }
```

集合字段使用 `.Add()` 赋值，所有解析值保留在列表中。

## 跳过字段：`[TemplateIgnore]`

struct 中包含不应参与序列化的字段（缓存、内部状态），且字段类型没有 `[Template]` 时，用 `[TemplateIgnore]` 标记。被标记的字段不出现在 scanner 和 emitter 代码中：

```csharp
[Template("<float Value>")]
public struct Stats
{
    public float Value;
    [TemplateIgnore] public CacheData InternalCache;
}
```

被标记的字段不应出现在模板字符串中，否则仍会触发 SSR004 错误。

## 内置类型

source generator 支持 12 种 C# 内置 unmanaged 类型和 `string`，每个有对应的 `SerializerRegistry.Scan_Xxx` 和 `Emit_Xxx` 方法:

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
| `string` | `string` | 带引号或不带引号的标记 |

所有内置扫描器和发射器均为零分配、`AggressiveInlining` 标注的 span 方法。`string` 类型不受 `TData : unmanaged` 约束影响（Template 层面，class 类型也受支持），但 `FluxLexer<TData>` 仍要求 `TData : unmanaged`。

## 编译器诊断

| ID | Severity | 含义 |
|----|----------|------|
| SSR001 | Error | 模板语法/格式错误 |
| SSR002 | Error | 模板类型之间存在循环依赖 |
| SSR003 | Error | `readonly struct` 不能使用 `[Template]`（字段赋值需要可变 struct） |
| SSR004 | Error | 模板引用了未注册 `[Template]` 或 `[ExternalTemplate]` 的类型，且字段未标记 `[TemplateIgnore]` |

## 手动委托: 进阶/回退

当字面量语法过于不规则、模板无法表达时，使用手写 `LiteralScanner<TData>` 委托。有 `[Template]` 时无需设置此字段，但设置了也不会冲突: 生成式扫描器优先。

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
