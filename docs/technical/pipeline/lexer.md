# 词法分析器

`FluxLexer<TData>` 是手写的零分配扫描器。它的核心设计问题：**如何在不使用正则表达式的前提下，将中缀表达式字符串高效转换为 Token 序列？**

## 为什么不用正则？

正则表达式在 .NET 中的 `Match` 操作会产生 `Match` 对象和 `Group` 集合的堆分配。对于 FluxFormula 的目标场景（编译期一次性分配，执行期零 GC），正则的分配开销可以接受（编译本身就有 Token 数组分配）。但手写扫描器提供了三个正则无法提供的优势：

1. **精确的错误定位**：手写循环知道当前字符位置，可以产出带行列号的错误信息。
2. **零中间字符串**：`ReadOnlySpan<char>` 切片直接传给 `LiteralScanner`，仅在调用 `CreateDefaultNumberScanner` 中的 parser 时才 `ToString()`。
3. **上下文感知**：操作符消歧（如 `-` 在 `OperandExpected` 位置是一元负号）在扫描阶段即可处理，不需要编译器的额外遍历。

## 核心数据结构

```csharp
public readonly struct LexResult<TData> where TData : unmanaged
{
    public readonly FluxToken<TData>[] Tokens;  // Token 序列
    public readonly string[] VarNames;          // 变量名列表（按出现顺序）
}
```

`FluxToken<TData>` 本身是 16 字节（`byte Oper` + padding + `TData Data`）：

```csharp
public readonly struct FluxToken<TData> where TData : unmanaged
{
    public readonly byte Oper;   // 操作符字节码（由 Definition 定义）
    public readonly TData Data;  // 字面量值（仅 Immediate 类型有效）
}
```

关键设计：Token 不存储字符串。操作符通过 `byte` 标识（由 Definition 的 `ResolveToken` 转换），字面量值在解析时即完成 `TData` 转换。扫描完成后，原始字符串不再需要。

## 扫描循环

`Lex()` 方法的整体结构：

```
while (pos < input.Length):
    if (char.IsWhiteSpace)  → skip
    if (char.IsDigit/dot)   → TryScanLiteral → FluxToken(oper=LiteralOper, data=parsedValue)
    if (operator starts)    → TryScanOperator → FluxToken(oper=operatorByte)
    if (bracket starts)     → try match bracket pair
    if (variable starts)    → TryScanVariable → FluxToken(oper=LiteralOper, data=default)
```

核心循环不分配任何堆内存。唯一的分配在退出循环后：`FluxToken[]` 数组和 `string[] VarNames`。

## 操作符扫描：最长匹配

`TryScanOperator` 不是简单的前缀匹配。它使用**最长匹配策略**。当输入 `select(a, b, c)` 时，扫描器先尝试匹配 `select` 而非 `s` 或 `se`：

1. 遍历所有已注册操作符的 `TokenText`，找出所有前缀匹配
2. 选择最长匹配（`select` > `s`）
3. 如果最长匹配是函数操作符（带括号），自动进入函数参数模式

这避免了用户定义 `select` 和 `s` 两个操作符时的歧义。

## 括号匹配

括号配置通过 `BracketRule` 定义：

```csharp
public readonly struct BracketRule
{
    public readonly string Left;    // "("
    public readonly string Right;   // ")"
    public readonly byte LeftOper;  // LParen 操作码
    public readonly byte RightOper; // RParen 操作码
}
```

扫描器不关心括号的嵌套层级。它只负责将 `(` 和 `)` 分别转为 `LParen` 和 `RParen` 操作码 Token。嵌套正确性由编译器（调车场算法）在处理括号栈时验证。

## 变量模式

变量通过 `VariablePatternRule` 配置（如 `["[", "]"]` 或 `["{var:", "}"]`）：

```csharp
public readonly struct VariablePatternRule
{
    public readonly string Prefix;  // "["
    public readonly string Suffix;  // "]"
}
```

扫描时，当遇到 prefix 起始字符时，扫描至 suffix 结束，提取中间的变量名。变量被映射为 `LiteralOper` 类型的 Token（`Data = default`），同时变量名加入 `VarNames` 列表。变量到 Immediate 槽位的映射在编译阶段完成。

## 字面量扫描

字面量通过 `LiteralScanner` 委托扫描：

```csharp
public LiteralScanner<TData> LiteralScanner;
```

`LiteralScanner` 接收 `ReadOnlySpan<char>` 和当前位置，返回消费后的位置和解析值。简单数字格式使用 `CreateDefaultNumberScanner`。`ToString()` 分配是编译期唯一与字面量相关的堆分配（~392B for simple, ~1080B for complex）。详见 [字面量扫描器](../../guide/literal-scanner.md)。

## 隐式乘法

`ImplicitOperators` 集合允许配置哪些操作符可以在无显式符号时自动插入：

```
配置: ImplicitOperators = { Mul }
输入: "2(3)"  → 扫描器在 "2" 和 "(" 之间自动插入 Mul Token
输入: "(a)(b)" → 扫描器在 ")(" 之间自动插入 Mul Token
```

插入条件：当字面量/右括号后紧跟左括号/变量前缀时，且隐式操作符已注册。

## Source Generator 集成 (v5.0+)

v5.0 引入 source generator 驱动的字面量模板系统，将扫描器生成从运行时移到编译期。

### 编译期管线

`LiteralScannerGenerator` (IIncrementalGenerator) 在编译期间运行，三条增量管线收集三类输入:

| 管线 | Attribute | 收集对象 |
|------|-----------|---------|
| A | `[Template]` | 直接标记的 struct |
| B | `[ExternalTemplate]` | 第三方类型的模板注册（覆盖管线 A） |
| C | `[LiteralTypeAlias]` | 类型别名（如 `Distance` → `float`） |

管线 A 和 B 合并后（B 覆盖 A），每个模板经过两阶段处理:

1. **CompactToXml**: 紧凑格式 `"<float X> <float Y>"` 转为标准 XML `<literal-template><field type="float" name="X"/>...</literal-template>`
2. **XmlTemplateParser**: XML 解析为 AST（`LiteralTextNode` / `FieldDirectiveNode` / `OptionalBlockNode`）

随后 `BuildDependencyGraph` 分析模板间的类型引用关系、检测循环依赖、生成拓扑排序，最终由 `CodeEmitter` 将 AST 编译为 C# span scanner 源码，注入 `SerializerScanners.g.cs`。

### 运行时集成

生成的 `LiteralScanners` 部分类的静态构造函数将每个生成式扫描器写入 `ScannerRegistry<TData>.Scanner`:

```csharp
static LiteralScanners()
{
    ScannerRegistry<Point2D>.Scanner = (s, p, out v) => Scan_Point2D(s, p, out v);
    ScannerRegistry<Entity>.Scanner = (s, p, out v) => Scan_Entity(s, p, out v);
}
```

`FluxLexer<TData>` 构造函数按优先级选择扫描器:

1. `SerializerScanners.TryGetScanner<TData>()` — 有 `[Template]` 时命中
2. `config.LiteralScanner` — 手动委托回退
3. 两者都无则抛出 `ArgumentException`

### 生成代码模式

每个生成式 `Scan_Xxx` 方法的结构:

- span 边界检查 → 逐字段扫描
- 内置类型字段调用 `SerializerRegistry.Scan_Float` 等 12 种内置扫描器
- 自定义类型字段递归调用已生成的 `Scan_OtherStruct`
- 可选块: save position → 尝试匹配 → 成功则继续，失败则 restore
- 逐字符精确匹配裸文字
- 所有方法标注 `[MethodImpl(AggressiveInlining)]`

内置扫描器的识别格式和更多细节见[字面量扫描器](../../guide/literal-scanner.md)。
