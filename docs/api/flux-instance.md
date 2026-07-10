# FluxInstance

ref struct 流式执行器。栈分配，零 GC。

## 签名

```csharp
public ref struct FluxInstance<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

`ref struct` 只能存在于栈上，不可装箱，不可作为类字段，确保执行路径零堆分配。

## 方法

### SetIndex

```csharp
public FluxInstance<TData, TDef> SetIndex(int index, TData value)
```

按槽位索引直接注入值。用于内部工具（VFF 覆写应用等），按名称注入请使用 `Set()`。

### Set

```csharp
public FluxInstance<TData, TDef> Set(string name, TData value)
```

按变量名注入值。使用内联线性扫描定位变量槽位（公式变量通常 3~5 个），所有同名变量同时写入。若 `name` 未在 Lexer 的 `VariablePatterns` 中出现，抛出 `ArgumentException`。

```csharp
var inst = runner.Instantiate(formula);
float r = inst.Set("atk", 150f).Set("def", 50f).Run();
```

### TrySet

```csharp
public FluxInstance<TData, TDef> TrySet(string name, TData value)
```

按变量名安全注入值。变量名存在时与 `Set` 行为一致；变量名不存在时静默跳过，不抛异常。适用于注入代码无法预先知道公式签名的场景（如后处理公式中注入统计量关键字 `[Avg]` `[Max]` `[Min]` `[Mid]`）。

```csharp
// 公式可以是 [Max] - [Avg]，或只是 [Avg]，或任意组合
// 注入代码不变
instance
    .TrySet("Avg", avg)
    .TrySet("Max", max)
    .TrySet("Min", min)
    .TrySet("Mid", median)
    .Run();
```

### Run

```csharp
public readonly TData Run()
```

启动计算引擎，返回 `TData` 结果。

- v3.0.0：`FluxModifier` 没有 `Instantiate()` 方法，无法产生 `FluxInstance`。Modifier 独立求值的错误在编译期就被阻止
- 若 `_isJit == true`，调用 JIT 编译好的委托
- 否则新建 `FluxEvaluator`，`stackalloc` 寄存器执行字节码循环

### GetBuffer

```csharp
internal readonly Instruction[] GetBuffer()
```

返回底层的 `Instruction[]` 缓冲（internal，外部不可调用）。调试和 benchmark 专用。

## 使用示例

```csharp
var config  = new LexerConfig<float>();
var lexer   = new FluxLexer<float>(config);
var def     = new MathDef();
var runner  = new FluxAssembler<float, MathDef>(def);

var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
float r = runner.Instantiate(formula).Run();

// 命名变量注入
float r2 = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("def", 50f)
    .Run();
```

## 参见

- [FluxAssembler](./flux-assembler) — 通过 Instantiate/Build 产出 FluxInstance
- [FluxFormula](./flux-formula) — Instance 包装的字节码容器
