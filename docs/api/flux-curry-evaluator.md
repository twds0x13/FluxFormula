# FluxCurryEvaluator

当你需要渐进式注入变量，而非一次性提供全部参数：`FluxCurryEvaluator` 在变量注入点之间挂起求值，每次 `Bind` 返回新 state，旧 state 不受影响。

## 签名

```csharp
public readonly struct FluxCurryEvaluator<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsCompleted` | `bool` | 所有变量已绑定且求值完成 |
| `Result` | `TData` | 求值结果。掩码未满时抛出 `InvalidOperationException`，需调用 `ForceComplete` 或绑定全部变量 |
| `BoundCount` | `int` | 已绑定变量数 |
| `VariableCount` | `int` | 公式变量总数 |

## 方法

### Create（静态工厂）

```csharp
public static FluxCurryEvaluator<TData, TDef> Create(
    TDef definition, FluxFormula<TData, TDef> formula)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `definition` | `TDef` | 运算符定义体 |
| `formula` | `FluxFormula<TData, TDef>` | 已编译的公式 |

创建后自动执行到第一个挂起点（第一个未绑定变量），返回初始 state。

### Bind（按名）

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(string name, TData value)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 变量名（乱序绑定） |
| `value` | `TData` | 绑定值 |

按名查找未绑定变量，注入后执行到下一个挂起点或完成。变量已绑定或不存在时抛 `ArgumentException`。返回新实例。

### TryBind（按名）

```csharp
public FluxCurryEvaluator<TData, TDef> TryBind(string name, TData value)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 变量名（乱序绑定） |
| `value` | `TData` | 绑定值 |

按名安全绑定单个变量。变量名不存在或已绑定时静默跳过，不抛异常。适用于注入代码无法预先知道公式签名的场景。返回新实例。

### Bind（顺次）

```csharp
public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `values` | `params TData[]` | 按顺序绑定到接下来的 N 个未绑定变量 |

依次绑定、逐值执行到挂起点。返回新实例。

### TryBind（顺次）

```csharp
public FluxCurryEvaluator<TData, TDef> TryBind(params TData[] values)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `values` | `params TData[]` | 按顺序安全绑定到接下来的 N 个未绑定变量 |

依次安全绑定未绑定变量。全部已绑定时静默停止。返回新实例。

### ForceComplete

```csharp
public FluxCurryEvaluator<TData, TDef> ForceComplete()
```

以 `default(TData)` 填充所有剩余未绑定变量，执行到完成。`IsCompleted` 变为 `true` 后可直接读取 `Result`。

## 使用示例

#### 分步绑定

```csharp
var def = default(MathDef);
var assembler = new FluxAssembler<float, MathDef>(def);
var lexer = new FluxLexer<float>(config);
var formula = assembler.Compile(lexer.Lex("[atk] * [mult] + [bonus]"));

var curry = FluxCurryEvaluator<float, MathDef>.Create(def, formula);
curry = curry.Bind("atk", 100f);   // 注入 atk，执行到 mult 挂起
curry = curry.Bind("mult", 2f);    // 注入 mult，执行到 bonus 挂起
curry = curry.Bind("bonus", 50f);  // 注入 bonus，执行完毕
float result = curry.Result;       // 250
```

#### 中途分叉

```csharp
var baseState = FluxCurryEvaluator<float, MathDef>.Create(def, formula)
    .Bind("atk", 100f).Bind("mult", 2f);

var branchA = baseState.Bind("bonus", 50f);   // 250
var branchB = baseState.Bind("bonus", 0f);    // 200
// baseState 不受影响
```

#### 强制完成

```csharp
var partial = FluxCurryEvaluator<float, MathDef>.Create(def, formula)
    .Bind("atk", 100f);
// 剩余 mult、bonus 以 0f 填充
float result = partial.ForceComplete().Result;  // 0
```

## 参见

- [FluxStepEvaluator](./flux-step-evaluator) — 逐指令单步调试
- [FluxInstance](./flux-instance) — 一次性全部注入的热路径求值器
- [FluxAssembler](./flux-assembler) — 编译入口
