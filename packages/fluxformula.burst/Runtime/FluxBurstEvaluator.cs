using System;
using FluxFormula.Core;
using Unity.Burst;

namespace FluxFormula.Burst
{
    /// <summary>
    /// Burst 编译的公式解释器。将 .ff 字节码作为原始字节指针执行。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="FluxEvaluator{TData, TDef}"/> 相同的三路分发循环（Immediate / Instruction / Return），
    /// 但输入从 <see cref="ReadOnlySpan{Instruction}"/> 改为 <c>byte*</c>，寄存器从 <c>stackalloc</c> 改为调用方传入的 <c>TData*</c>。
    /// 不分配托管内存，可与 Unity Jobs / Burst 完全兼容。
    /// </remarks>
    [BurstCompile]
    public struct FluxBurstEvaluator<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxDefinition<TData>
    {
        /// <summary>
        /// 在给定字节码和寄存器上执行解释器循环。
        /// </summary>
        /// <param name="bytecode">完整 .ff 字节码（含 14 字节头 + Instruction 体 + 变量槽尾）</param>
        /// <param name="registers">指向 256 个 TData 寄存器槽的指针（调用方通过 NativeArray 分配）</param>
        /// <param name="maxRegister">编译期分析的最高寄存器号（从 header 读取，0=回退到全量）</param>
        /// <returns>R1 总线中的结果，或 R0 中的错误哨兵</returns>
        [BurstCompile]
        public static unsafe TData Execute(
            byte* bytecode, TData* registers, byte maxRegister)
        {
            // ── 解析 header ──
            int count = ReadInt32LE(bytecode);
            // byte type = bytecode[4];     // FluxType（未使用——调用方保证字节码有效性）
            // int immCount 在 header[5..8]; // 未使用——仅 SetIndex 偏移计算需要
            // int varSlots 在 header[9..12];// 未使用——变量名在胶水层解析
            byte headerMaxReg = bytecode[13];

            byte actualMax = headerMaxReg > Registers.Bus ? headerMaxReg : Registers.Bus;
            if (maxRegister > actualMax) actualMax = maxRegister;
            int regCount = actualMax + 1;

            // ── 初始化寄存器 ──
            for (int i = 0; i < regCount; i++)
                registers[i] = default;

            // ── 指令体 ──
            Instruction* pBase = (Instruction*)(bytecode + FormulaFormat.HeaderSize);
            byte returnReg = Registers.Bus;
            int dataSlots = DataSlotCount();
            var regSpan = new Span<TData>((void*)registers, regCount);

            for (int ip = 0; ip < count; ip++)
            {
                Instruction* inst = pBase + ip;
                byte operByte = inst->OpCode;
                OpType kind = default(TDef).GetKind(operByte);

                if (kind == OpType.Immediate)
                {
                    // 紧跟在当前指令后的 TData 值
                    TData* pData = (TData*)(pBase + ip + 1);
                    registers[inst->Dest] = *pData;
                    ip += dataSlots;
                }
                else if (kind == OpType.Instruction)
                {
                    registers[inst->Dest] = default(TDef).Compute(operByte, *inst, regSpan);

                    // NaN → R0 错误传播（除零等操作）。
                    // typeof 检查在 Burst 编译时被常量折叠，非 float 特化零开销。
                    if (typeof(TData) == typeof(float))
                    {
                        var result = registers[inst->Dest];
                        if (float.IsNaN(*(float*)&result))
                            registers[Registers.Error] = result;
                    }

                    // R0 短路检查
                    if (!IsDefault(&registers[Registers.Error]))
                        return registers[Registers.Error];
                }
                else if (kind == OpType.Return)
                {
                    returnReg = inst->Dest;
                    // 合并字节码（FluxChain.ToAtomic 产物）：Return 后还有指令 → 不退出
                    if (ip + 1 < count)
                    {
                        registers[Registers.Bus] = registers[inst->Dest];
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return registers[returnReg];
        }

        /// <summary>按字节比较 TData 是否为 default（全零）</summary>
        private static unsafe bool IsDefault(TData* ptr)
        {
            byte* p = (byte*)ptr;
            for (int i = 0; i < sizeof(TData); i++)
                if (p[i] != 0)
                    return false;
            return true;
        }

        /// <summary>小端序读取 int32</summary>
        private static unsafe int ReadInt32LE(byte* p)
        {
            return p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24);
        }

        /// <summary>ceil(sizeof(TData) / sizeof(Instruction))——Immediate 占用的槽位数</summary>
        private static unsafe int DataSlotCount()
        {
            return (sizeof(TData) + 7) / 8;
        }
    }
}
