# 8-Byte Instruction Layout

`Instruction` is FluxFormula's minimal bytecode execution unit. Core design question: **how to encode opcode + destination register + up to 6 operand registers in 8 bytes, while enabling `TData*` pointer reinterpretation?**

## Explicit Memory Layout

```csharp
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct Instruction
{
    [FieldOffset(0)] public byte OpCode;
    [FieldOffset(1)] public byte Dest;
    [FieldOffset(2)] public byte Arg0;
    [FieldOffset(3)] public byte Arg1;
    [FieldOffset(4)] public byte Arg2;
    [FieldOffset(5)] public byte Arg3;
    [FieldOffset(6)] public byte Arg4;
    [FieldOffset(7)] public byte Arg5;
    [FieldOffset(0)] public long Raw;      // 64-bit raw view
}
```

8 bytes fixed, `[StructLayout(LayoutKind.Explicit)]` guarantees consistent layout under IL2CPP/AOT. Maximum arity is 6 — no operator takes more than 6 operands (including destination).

## Why 8 Bytes?

```
1B OpCode + 1B Dest + 6×1B Args = 8 bytes — perfect 64-bit word alignment
```

- **Cache-friendly**: 8 instructions per 64B L1 cache line.
- **Register width match**: 256 virtual registers (`byte.MaxValue + 1`), exactly one byte.
- **Pointer reinterpretation**: `TData*` writes directly into memory adjacent to `Instruction`:

```csharp
*(float*)(pBase + ip + 1) = 3.14f;
// pBase+ip → current Instruction
// +1 → skip 8-byte header
// writes float (4 bytes) in 1 instruction slot
```

## Dual View: `OpCode` + `Raw`

`Raw` (`long`) shares `FieldOffset(0)` with byte fields. This enables:
- **Serialization**: `BinaryFormat.WriteInt64LE(data, offset, inst.Raw)` — one write per instruction.
- **Comparison**: `inst.Raw == other.Raw` — 64-bit integer equality.
- **Copy**: `buffer[i] = new Instruction { Raw = src.Raw }` — single-assignment clone.

## Data Slots: `TData` Inline in Instruction Stream

Immediate values live adjacent to instruction headers, not inside the struct:

```
[Inst0: Op=Immediate, Dest=R2]  ← header
[Data0: float 3.14]              ← data slot (sizeof(TData) instruction slots)
[Inst1: Op=Immediate, Dest=R3]
[Data1: float 2.718]
[Inst2: Op=Add, Dest=R1, Arg0=R2, Arg1=R3]
[Inst3: Op=Return, Dest=R1]
```

`DataSlots<TData>()` computes slot count:

```csharp
internal static int DataSlots<TData>() where TData : unmanaged
    => (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
```

For `float` (4 bytes): 1 slot. For `double` (8 bytes): 1 slot. For large structs (>8 bytes): multiple slots.
