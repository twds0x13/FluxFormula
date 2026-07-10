# FluxStepEvaluator

当你需要逐条指令检查寄存器状态、理解字节码执行过程时，`FluxStepEvaluator` 提供指令级单步调试。每次 `Step()` 执行一条指令并返回新 state，旧 state 不受影响。

## 签名

```csharp
public readonly struct FluxStepEvaluator<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsCompleted` | `bool` | 是否已执行完毕 |
| `Result` | `TData` | 最终结果（仅在 `IsCompleted` 时有意义） |
| `CurrentIP` | `int` | 当前指令指针（字节码索引） |
| `CurrentOpCode` | `byte` | 当前指令的操作码（未开始或已完成时为 0） |
| `CurrentInstruction` | `Instruction` | 当前指令的完整结构体（未开始或已完成时为 default） |
| `Regs` | `ReadOnlySpan<TData>` | 寄存器文件只读快照 |
| `InstructionCount` | `int` | 指令总数 |

## 方法

### Create（静态工厂）

```csharp
public static FluxStepEvaluator<TData, TDef> Create(
    TDef definition, FluxFormula<TData, TDef> formula)
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `definition` | `TDef` | 运算符定义体 |
| `formula` | `FluxFormula<TData, TDef>` | 已编译的公式 |

返回初始 state（IP = 0，未开始执行）。

### Step

```csharp
public FluxStepEvaluator<TData, TDef> Step()
```

执行恰好一条指令。若已完成则返回自身。返回新实例（寄存器数组完整拷贝）。

### RunToEnd

```csharp
public FluxStepEvaluator<TData, TDef> RunToEnd()
```

执行所有剩余指令直到完成。等价于循环调用 `Step()`，但不需要中间拷贝。

## 使用示例

#### 单步调试循环

```csharp
var def = default(MathDef);
var formula = new FluxAssembler<float, MathDef>(def)
    .Compile(new FluxLexer<float>(config).Lex("(3 + 4) * 2"));

var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);

while (!state.IsCompleted)
{
    Console.WriteLine(
        $"IP={state.CurrentIP}, Op={(MathOp)state.CurrentOpCode}, " +
        $"R1={state.Regs[1]}, R2={state.Regs[2]}");
    state = state.Step();
}
Console.WriteLine($"Result = {state.Result}");  // 14
```

#### 快速执行到底

```csharp
var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);
var final = state.RunToEnd();
float result = final.Result;
```

#### 检查当前指令

```csharp
var state = FluxStepEvaluator<float, MathDef>.Create(def, formula);
var inst = state.CurrentInstruction;
// inst.OpCode, inst.Arg0, inst.Arg1, inst.Dest
```

## 参见

- [FluxCurryEvaluator](./flux-curry-evaluator) — 变量级分步求值
- [FluxInstance](./flux-instance) — 热路径全速求值
- [Instruction](./instruction) — 8 字节指令结构体
