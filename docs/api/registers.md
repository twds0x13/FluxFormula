# Registers

寄存器模型语义常量。项目中所有寄存器号引用的唯一来源。

## 签名

```csharp
public static class Registers
```

## 常量

| 常量 | 类型 | 值 | 说明 |
|------|------|------|------|
| `Error` | `byte` | `0` | R0：错误哨兵寄存器。`Compute()` 在此寄存器写入非 default 值时触发短路返回 |
| `Bus` | `byte` | `1` | R1：输出总线寄存器。公式最终结果写入此寄存器，链式链接的值也通过 R1 传递 |

## 设计要点

| 特性 | 说明 |
|------|------|
| R0 哨兵 | VM 每条指令执行后检查 R0，一旦非 default 立即终止求值并返回错误值 |
| R1 总线 | 所有公式和 Modifier 的计算结果都落在 R1 上。`Connect()` 将前一公式的 R1 输出注入为后一 Modifier 的输入 |
| R2-R254 通用 | 253 个通用寄存器用于编译期分配中间结果。具体分配由 `FluxCompiler` 的内部寄存器分配器管理 |

## 使用示例

```csharp
// 在使用 FluxInstance.Run 时检查组内是否存在错误
float result = instance.Run();
// 如果 Compute() 返回非 default 值，result 继承该值，且 VM 提前退出

// 快速常量引用（大多数代码无需直接使用）
byte errReg = Registers.Error;   // 0
byte busReg = Registers.Bus;     // 1
```

## 参见

- [Instruction](./instruction) — 指令布局，其中 `Dest` 字段引用寄存器索引
- [FluxEvaluator](/technical/pipeline/evaluator) — 解释器执行循环中的 R0 检查
- [FluxChain](./flux-chain) — 链式公式通过 R1 总线传递值
