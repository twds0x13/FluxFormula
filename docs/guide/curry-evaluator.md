# 分步求值器（柯里化）

假设你有一个技能伤害公式 `[baseAtk] * [skillMult] * (1 + [critDmg])`。baseAtk 和 critDmg 是角色属性，基本不变；skillMult 是技能参数，每个技能不同。你可以在角色面板打开时绑一次角色属性，存为中间状态，之后每个技能只绑 skillMult 就出结果，无需每次 `.Set().Set().Set().Run()`。

`FluxCurryEvaluator` 提供函数式渐进绑定求值：按变量声明顺序分批注入参数，每次 `Bind` 返回新
State，支持从同一中间状态分叉出不同参数组合。适用于参数延迟绑定、模板化公式复用等场景。

## 与热路径的关系

FluxFormula 有三种求值模式:

| | 热路径 | 柯里化 | 单步调试 |
|---|---|---|---|
| 类 | `FluxInstance` (ref struct) | `FluxCurryEvaluator` (struct) | `FluxStepEvaluator` (struct) |
| 目的 | 生产环境全速求值 | 渐进参数绑定 | 逐指令调试 |
| 变量注入 | `.Set(name, value)` | `.Bind(params values)` 按顺序 | 无 |
| 状态模型 | 可变 ref struct | 不可变 State → State | 不可变 State → State |
| 分叉 | 不支持 | 支持 | 可保存状态手动分叉 |

## 创建

```csharp
var assembler = new FluxAssembler<float, FloatMathDef>(definition);
var formula = assembler.Compile(lexer.Lex("[a] * [b] + [c]"));

// 创建柯里化求值器
var curry = assembler.Curry(formula);
// 等价于: FluxCurryEvaluator<float, FloatMathDef>.Create(definition, formula)
```

## API

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsCompleted` | `bool` | 所有变量已绑定并求值完成时为 `true` |
| `Result` | `TData` | 最终结果。未完成时读取会强制用 `default` 填充剩余变量并完成求值 |
| `BoundCount` | `int` | 已绑定变量数 |
| `VariableCount` | `int` | 公式中的变量总数 |

### 方法

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
```

绑定接下来的 N 个变量（按字节码中出现的顺序）。每次调用返回新实例，原实例不变。
传入数量超过剩余变量时多余值被忽略；传入空数组无操作。

## 用法

### 基础: 一次性绑定

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[x] + [y] * [z]")));
curry = curry.Bind(10f, 2f, 3f);  // 一次绑三个
// curry.IsCompleted == true, curry.Result == 16f
```

### 渐进绑定

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[a] * [b]")));
var step1 = curry.Bind(5f);       // a=5, 暂停等待 b
Assert.That(step1.BoundCount, Is.EqualTo(1));
Assert.That(step1.IsCompleted, Is.False);

var step2 = step1.Bind(3f);       // b=3
// step2.Result == 15f
```

### 分叉

```csharp
var step1 = curry.Bind(5f);       // 中间状态

var branchA = step1.Bind(2f);     // 5 * 2 = 10
var branchB = step1.Bind(3f);     // 5 * 3 = 15
// step1 仍然是 BoundCount=1, 未变
```

### 强制完成

```csharp
var curry = assembler.Curry(assembler.Compile(lexer.Lex("[x] + 5")));
// x 未绑定, 但读取 Result 自动用 default(float)=0 填充
Console.WriteLine(curry.Result);  // 5 (0 + 5)
```

## 注意事项

- 非 `ref struct` — 可存于字段、数组、泛型参数
- 每次 `Bind` 分配两个数组（`_boundValues` + `_regs`），大小等于公式中变量数
- `params` 语法支持一次传多个值，避免链式 `Bind().Bind().Bind()`
- 柯里化不走 JIT 路径 — 始终解释执行
- 链式公式 (`FluxChain`) 需先调用 `.ToAtomic()` 转为原子公式再传入
