# FluxInstance

ref struct 流式执行器。栈分配，零 GC。

## 签名

```csharp
public ref struct FluxInstance<TData, TOper, TDef>
    where TData : unmanaged
    where TOper : unmanaged, Enum
    where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
```

`ref struct` 只能存在于栈上，不可装箱，不可作为类字段，确保执行路径零堆分配。

## 方法

### Set

```csharp
public FluxInstance<TData, TOper, TDef> Set(string name, TData value)
```

按变量名注入值。使用内联二分查找定位变量槽位，所有同名变量同时写入。若 `name` 未在 Lexer 的 `VariablePatterns` 中出现，抛出 `ArgumentException`。

```csharp
var inst = runner.Instantiate(formula);
float r = inst.Set("atk", 150f).Set("def", 50f).Run();
```

### SetIndex

```csharp
public FluxInstance<TData, TOper, TDef> SetIndex(int index, TData value)
```

按位置索引注入值（第 `index` 个 Immediate 数据槽位）。无变量名校验。

- JIT 路径：写入紧凑的 payload 数组（线性索引）
- 解释器路径：按预扫描的 offsets 写入公式缓冲

### Run

```csharp
public readonly TData Run()
```

启动计算引擎，返回 `TData` 结果。

- 若公式 `Type == Modifier`，抛出 `InvalidOperationException`
- 若 `_isJit == true`，调用 JIT 编译好的委托
- 否则新建 `FluxEvaluator`，`stackalloc` 寄存器执行字节码循环

### GetBuffer

```csharp
public readonly Instruction[] GetBuffer()
```

返回底层的 `Instruction[]` 缓冲。调试和 benchmark 专用，非生产路径。

## 使用示例

```csharp
// 单次求值
float r = runner.Build(tokens).Run();

// 命名变量注入
float r = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("def", 50f)
    .Run();

// 按索引注入
float r = runner.Instantiate(formula)
    .SetIndex(0, 3f)
    .SetIndex(1, 4f)
    .Run();
```

## 参见

- [FluxAssembler](./flux-assembler) — 通过 Instantiate/Build 产出 FluxInstance
- [FluxFormula](./flux-formula) — Instance 包装的字节码容器
