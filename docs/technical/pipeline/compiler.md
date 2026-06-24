# 调车场编译器

`FluxCompiler<TData, TDef>` 实现了经典的调车场算法（Shunting-Yard Algorithm），将中缀 Token 序列编译为后缀字节码（`Instruction[]`）。它的核心设计问题：**如何在一个线性扫描中同时处理操作符优先级、括号配对、和函数调用的多参数语法？**

## 为什么调车场？

调车场算法是 Dijkstra 在 1961 年提出的经典算法。对于 FluxFormula 的场景，它有三个优势：

1. **单遍扫描**：输入 Token 序列只读一次，O(n) 时间复杂度。
2. **本地决策**：每个 Token 的优先级和结合性由 Definition 提供，算法不关心操作符的具体语义。
3. **自然产出后缀表示**：输出顺序天然就是栈式解释器的执行顺序，无需额外的 AST→字节码转换。

## 双栈模型

```
输入队列:  [C(1), Add, C(2), Mul, C(3)]    ← Token 序列（中缀）
                │
    ┌───────────┴───────────┐
    │     调车场算法         │
    │  operatorStack (Op)   │  操作符暂存栈
    │  outputQueue   (RPN)  │  输出队列（后缀字节码）
    └───────────────────────┘
                │
输出:      [C(1), C(2), C(3), Mul, Add]    ← 后缀字节码
```

- **操作符栈**：暂存尚未确定操作数范围的操作符。遇到更高优先级的操作符时，栈顶操作符需要等待；遇到更低优先级时，栈顶操作符可以安全弹出。
- **输出队列**：直接写入 `Instruction[]` 缓冲区。FluxFormula 不使用 `List<T>` 或 `Queue<T>`——操作符栈用 `Instruction*` 指针 + `stackalloc` 在栈上管理，输出队列是预分配的 `Instruction[]`。

## 核心循环

```
for each Token t in input:
    if t is Immediate (字面量/变量):
        → EmitLoad(t)         // 直接输出到指令缓冲
    if t is LeftParen:
        → Push(t)
    if t is RightParen:
        → Pop until LeftParen, emit all
    if t is Operator:
        → while (topOfStack.priority ≥ t.priority):
              Pop and emit
          Push(t)
```

关键细节在操作符优先级比较中：

```csharp
while (opStack.Count > 0)
{
    var top = opStack.Peek();
    int topPrec = _definition.GetPrecedence(topOper);
    int curPrec = _definition.GetPrecedence(currentOper);

    if (topPrec > curPrec || (topPrec == curPrec && leftAssociative))
        Emit(opStack.Pop());
    else
        break;
}
```

结合性处理：左结合（`+`, `-`, `*`, `/`）在同等优先级时弹出栈顶；右结合（`^`）在同等优先级时保留栈顶。

## 操作符配对系统（Pair System）

这是 FluxFormula 编译器对标准调车场算法最关键的扩展。操作符可以声明配对关系：

```
? 的 OpPair: PairRole=None, EmitOnMatch=true, EmitOpCode=Select
, 的 OpPair: PairRole=None, EmitOnMatch=true, EmitOpCode=Select, IsSeparator=true
```

当编译器看到 `:` 时（它被配置为 `?` 的配对目标），它会：
1. 在操作符栈中找到对应的 `?`
2. 弹出 `?` 和 `:` 之间的所有操作符
3. 将 `?` 替换为 `Select` 操作码并发出

这允许**三元表达式 `a ? b : c`** 在调车场中自然地编译为 `Select(a, b, c)`。逗号分隔符同理——`select(a, b, c)` 中的逗号触发 `Select` 的 emit。

## 寄存器分配

编译器在 emit 每条指令时分配虚拟寄存器：

```csharp
byte destReg = AllocRegister();
inst.Dest = destReg;
```

寄存器分配器跟踪已使用和已释放的寄存器。当指令的所有操作数被消费后，它们的寄存器被回收。寄存器范围是 0-255（`byte`），实际使用量存储在公式头部的 `MaxRegister` 字段中——运行时按需 `stackalloc`，不分配全量 255。

## 输出格式

编译产物直接写入 `Instruction[]` 缓冲区，格式为：

```
[Immediate(R2, value)] [Immediate(R3, value)] [Add(R1, R2, R3)] [Return(R1)]
```

不是 AST，不是三地址码——直接就是可执行的字节码。Instruction 的 8 字节布局确保了 `Span<Instruction>` 可以 `fixed` 指针后以 `TData*` 重解释写入立即数。
