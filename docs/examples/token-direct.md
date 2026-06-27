# 示例：Token 直构

不经过词法分析器，手动构造 `FluxToken<TData>[]` 数组直接编译。适用于代码生成公式、单元测试、以及需要精确控制 Token 序列的工具链场景。

依赖的 `MathDef` 定义见[浮点四则运算](/examples/float-math)，操作码 `Const=0, Add=1, Sub=2, Mul=3, Div=4, Return=255`。

## 中缀表达式 → Token 数组

`v1 * 2 + v2` 等价于以下 Token 序列：

```csharp
using FluxFormula.Core;

// 字面量 → Oper=Const, Data=值；操作符 → Oper=对应的 byte，Data=default
var tokens = new FluxToken<float>[]
{
    new() { Oper = (byte)MathOp.Const, Data = 0f },    // [v1] — 占位，运行时通过 Set("v1", ...) 注入
    new() { Oper = (byte)MathOp.Mul,   Data = default },
    new() { Oper = (byte)MathOp.Const, Data = 2f },     // 立即数 2
    new() { Oper = (byte)MathOp.Add,   Data = default },
    new() { Oper = (byte)MathOp.Const, Data = 0f },     // [v2] — 占位
    new() { Oper = (byte)MathOp.Return,Data = default }, // Return
};
```

## 编译与求值

`FluxAssembler.Compile(ReadOnlySpan<FluxToken<TData>>)` 直接接受 Token 数组，跳过词法分析：

```csharp
var assembler = new FluxAssembler<float, MathDef>(new MathDef());

// Token → Instruction[]（一次性编译期分配）
var formula = assembler.Compile(tokens);

// 激活 → 注入变量 → 求值
float result = assembler.Instantiate(formula, jit: true)
    .Set("v1", 100f)
    .Set("v2", 50f)
    .Run();
// result = (100 * 2) + 50 = 250
```

## 变量占位规则

`Const` Token 的 `Data` 字段在编译时不起作用：编译器将所有 `Const` 视为 Immediate 槽位，按先后顺序分配 `SlotIndex`。运行时通过变量名（`VarNames` 参数）建立名称到槽位的映射。

无变量名时使用 `SetIndex` 按序号注入：

```csharp
var formula = assembler.Compile(tokens); // 不传 varNames
float result = assembler.Instantiate(formula)
    .SetIndex(0, 100f)  // 第一个 Const → Slot 0
    .SetIndex(2, 50f)   // 第三个 Const → Slot 2（第二个是立即数 2，不可 Set）
    .Run();
```

传变量名后即可使用 `Set(name, value)`：

```csharp
var formula = assembler.Compile(tokens, new[] { "v1", "v2" });
float result = assembler.Instantiate(formula)
    .Set("v1", 100f)
    .Set("v2", 50f)
    .Run();
```

## 类型自动推断

`FluxAssembler.Compile()` 自动判断产物是 `FluxFormula` 还是需要手动改为 `FluxModifier`：首个 Token 是 `Const` → Formula；首个 Token 是二元操作符（如 `Add`）→ Modifier。直接构造 Token 时需注意首 Token 的类型选择。

## 注意事项

- Token 数组中的 `Const` 数量和顺序必须与运行时 `Set` / `SetIndex` 的注入位置对应
- `Return` Token 是必需的——编译器不会自动追加
- 与 Lexer 路径的语义完全一致：编译后的 `Instruction[]` 可正常走 JIT/解释器、可序列化、可缓存
