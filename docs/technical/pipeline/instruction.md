# 8 字节指令布局

`Instruction` 是 FluxFormula 字节码的最小执行单元。它的核心设计问题：**如何在 8 字节内编码操作码 + 目标寄存器 + 最多 6 个操作数寄存器，同时让指针重解释写入 `TData` 值成为可能？**

## 显式内存布局

```csharp
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct Instruction
{
    [FieldOffset(0)] public byte OpCode;   // 操作码
    [FieldOffset(1)] public byte Dest;     // 目标寄存器 (0-255)
    [FieldOffset(2)] public byte Arg0;     // 操作数寄存器 0
    [FieldOffset(3)] public byte Arg1;     // 操作数寄存器 1
    [FieldOffset(4)] public byte Arg2;     // 操作数寄存器 2
    [FieldOffset(5)] public byte Arg3;     // 操作数寄存器 3
    [FieldOffset(6)] public byte Arg4;     // 操作数寄存器 4
    [FieldOffset(7)] public byte Arg5;     // 操作数寄存器 5
    [FieldOffset(0)] public long Raw;      // 64 位原始视图
}
```

8 字节定长，`[StructLayout(LayoutKind.Explicit)]` 确保字段布局在 IL2CPP/AOT 下与托管运行时一致。最大 arity 为 6，任何操作符的操作数不超过 6 个（含目标）。

## 为什么选择 8 字节？

```
1B OpCode + 1B Dest + 6×1B Args = 8 bytes ─── 精确对齐到 64 位字
```

- **缓存友好**：8 字节是 x86-64 的 L1 缓存行（64B）的整数因子。一条缓存行恰好容纳 8 条指令。
- **寄存器宽度匹配**：256 个虚拟寄存器（`byte.MaxValue + 1`），1 字节恰好完整寻址。
- **指针重解释**：`TData*` 可以直接写入紧邻 `Instruction` 之后的内存：

```csharp
// 将 float 值写入指令后的 4 字节数据槽
*(float*)(pBase + ip + 1) = 3.14f;
// pBase + ip 指向当前 Instruction
// +1 跳过 8 字节 Instruction 头部
// 写入一个 float (4 字节)，占用 1 个 Instruction 槽位
```

## 双重视图：`OpCode` 与 `Raw`

`Raw`（`long`）与字节字段共享 `FieldOffset(0)`。这允许：

- **序列化**：`BinaryFormat.WriteInt64LE(data, offset, inst.Raw)`，一条写操作完成整个指令的序列化。
- **比较**：`inst.Raw == other.Raw`，指令等价性比较退化为 64 位整数比较。
- **复制**：`buffer[i] = new Instruction { Raw = src.Raw }`，一条赋值完成克隆。

## 数据槽位：`TData` 内联在指令流中

立即数（字面量/注入变量）不存储在 `Instruction` 结构体内。它们紧邻指令头部，存放在后续的"数据槽位"中：

```
Instruction[N] 数组:
[Inst0: Op=Immediate, Dest=R2]  ← 指令头
[Data0: float 3.14]             ← 数据槽 (sizeof(TData) 个 Instruction 槽位)
[Inst1: Op=Immediate, Dest=R3]
[Data1: float 2.718]
[Inst2: Op=Add, Dest=R1, Arg0=R2, Arg1=R3]
[Inst3: Op=Return, Dest=R1]
```

`DataSlots<TData>()` 计算每种 `TData` 需要多少条 Instruction 槽位来存储：

```csharp
internal static int DataSlots<TData>() where TData : unmanaged
    => (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
```

对于 `float`（4 字节），`DataSlots<float>() = 1`（4 < 8，1 条指令槽位够用）。
对于 `double`（8 字节），`DataSlots<double>() = 1`（8 = 8，恰好 1 条）。
对于大型 struct（>8 字节），需要多个槽位。

## Why not union?

C# 不支持 C 的 `union`。`LayoutKind.Explicit` + `FieldOffset(0)` 是等效实现。在 IL2CPP 下，`StructLayout` 属性被保留并正确翻译为 C++ 的显式布局结构体。
