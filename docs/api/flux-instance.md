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

### Set

```csharp
public FluxInstance<TData, TDef> Set(string name, TData value)
```

按变量名注入值。使用内联二分查找定位变量槽位，所有同名变量同时写入。若 `name` 未在 Lexer 的 `VariablePatterns` 中出现，抛出 `ArgumentException`。

```csharp
var inst = runner.Instantiate(formula);
float r = inst.Set("atk", 150f).Set("def", 50f).Run();
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
public readonly Instruction[] GetBuffer()
```

返回底层的 `Instruction[]` 缓冲。调试和 benchmark 专用，非生产路径。

## 使用示例

```csharp
var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
float r = runner.Instantiate(formula).Run();

// 命名变量注入
float r = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("def", 50f)
    .Run();

```

## v3.0.0 变更

- `FluxInstance<TData, TOper, TDef>` → `FluxInstance<TData, TDef>`（三参数→两参数）
- 移除 `Type == Modifier` 运行时检查：`FluxModifier` 类型上不存在 `Instantiate()`，编译期保证安全

## 参见

- [FluxAssembler](./flux-assembler) — 通过 Instantiate/Build 产出 FluxInstance
- [FluxFormula](./flux-formula) — Instance 包装的字节码容器
