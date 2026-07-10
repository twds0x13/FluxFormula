# 单步调试器

公式结果不对？想知道字节码到底在哪一步走偏了？单步调试器让你逐条指令检视寄存器状态，像断点调试 C# 代码一样观察公式执行过程。

`FluxStepEvaluator` 提供逐指令执行能力，每次 `Step()` 推进一条指令并返回寄存器快照。
适用于调试编译器输出、可视化公式执行过程、教学演示等场景。

## 创建

```csharp
var assembler = new FluxAssembler<float, FloatMathDef>(definition);
var formula = assembler.Compile(new[] { C(3f), Op(FloatOp.Mul), C(4f) });

var step = assembler.StepDebug(formula);
// 等价于: FluxStepEvaluator<float, FloatMathDef>.Create(definition, formula)
```

`C()` 和 `Op()` 是测试辅助方法，分别生成 Const 立即数和 Instruction 操作符 token。

完整的属性列表、方法签名和参数说明见 [FluxStepEvaluator API 参考](/api/flux-step-evaluator)。

## 用法

### 手动单步

```csharp
var step = assembler.StepDebug(formula);
Console.WriteLine(step.CurrentIP);     // 0
Console.WriteLine(step.CurrentOpCode); // Const

step = step.Step();  // 加载立即数到寄存器
step = step.Step();  // 执行乘法
step = step.Step();  // Return: 完成

Assert.That(step.IsCompleted, Is.True);
Assert.That(step.Result, Is.EqualTo(12f));
```

### 统计指令数

```csharp
var step = assembler.StepDebug(formula);
int steps = 0;
while (!step.IsCompleted) { step = step.Step(); steps++; }
// steps == 3 (Const + Mul + Return)
```

### 检查寄存器

```csharp
var step = assembler.StepDebug(formula);
step = step.Step();  // 执行第一条指令
var snapshot = step.Regs;
Console.WriteLine(snapshot.Length);  // 寄存器数量
```

## 注意事项

- `ReadOnlySpan<TData>` 寄存器快照仅当次有效 — 每次 `Step()` 返回新的只读视图
- 非 `ref struct` — 可保存到数组或字段
- 单步调试不走 JIT — 始终解释执行
- 与 `FluxCurryEvaluator` 类似，每次 `Step()` 分配新数组
