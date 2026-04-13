# Instruction

8 字节定长指令，显式内存布局。

## 签名

```csharp
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct Instruction
```

## 字段

| 偏移 | 字段 | 类型 | 说明 |
|------|------|------|------|
| 0 | `OpCode` | `byte` | 操作符底层字节 |
| 1 | `Dest` | `byte` | 目标寄存器 (0-255) |
| 2 | `Arg0` | `byte` | 操作数 0 |
| 3 | `Arg1` | `byte` | 操作数 1 |
| 4 | `Arg2` | `byte` | 操作数 2 |
| 5 | `Arg3` | `byte` | 操作数 3 |
| 6 | `Arg4` | `byte` | 操作数 4 |
| 7 | `Arg5` | `byte` | 操作数 5 |
| 0 | `Raw` | `long` | 全部 8 字节的覆盖视图 |

`Raw` 与 `OpCode` 共用 offset 0。读 Raw 得到整个指令的 long 值（调试 dump），指令执行使用独立字段。

## 用途

用户一般不直接操作 `Instruction`。它由 `FluxCompiler` 生成，被 `FluxEvaluator` 与 `FluxJITCompiler` 消费。了解布局有助于调试字节码。
