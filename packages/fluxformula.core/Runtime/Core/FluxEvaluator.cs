using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 平台能力检测与 JIT 降级状态管理
    /// </summary>
    internal static class FluxPlatform
    {
        /// <summary>最大寄存器数量（byte 索引，R0 错误/R1 总线保留，剩余 253 个可用）</summary>
        internal const int MaxRegisters = 255;

        private static volatile bool _jitDisabled;

        /// <summary>
        /// 当前平台是否不支持 JIT (IL2CPP/AOT 导致 Expression.Compile 失败)
        /// </summary>
        public static bool IsJitDisabled => _jitDisabled;

        /// <summary>
        /// 标记 JIT 不可用，之后同进程内不再尝试
        /// </summary>
        public static void DisableJit() => _jitDisabled = true;
    }

    internal readonly unsafe ref struct FluxEvaluator<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxDefinition<TData, TOper>
    {
        private readonly TDef _definition;

        public FluxEvaluator(TDef definition)
        {
            _definition = definition;
        }

        /// <summary>标准求值，R1 从 default(TData) 开始</summary>
        public TData Compute(ReadOnlySpan<Instruction> raw)
        {
            return ComputeCore(raw, default);
        }

        /// <summary>
        /// 链式求值入口：R1 从 initialR1 开始而非 default(TData)。
        /// 用于链式公式的 per-link 解释器求值——前一个 link 的输出通过 R1 总线传入下一个 link。
        /// </summary>
        public TData Compute(ReadOnlySpan<Instruction> raw, TData initialR1)
        {
            return ComputeCore(raw, initialR1);
        }

        private TData ComputeCore(ReadOnlySpan<Instruction> raw, TData initialR1)
        {
            byte* rawPtr = stackalloc byte[sizeof(TData) * FluxPlatform.MaxRegisters + 63];
            long addr = (long)rawPtr;
            TData* regsPtr = (TData*)((addr + 63) & ~63);
            Span<TData> registers = new(regsPtr, FluxPlatform.MaxRegisters);

            regsPtr[0] = default;
            regsPtr[1] = initialR1;
            byte returnReg = 1; // 默认总线寄存器

            fixed (Instruction* pBase = raw)
            {
                for (int ip = 0; ip < raw.Length; ip++)
                {
                    Instruction* inst = pBase + ip;
                    byte operByte = inst->OpCode;
                    OpType kind = _definition.GetKind(operByte);

                    if (kind == OpType.Immediate)
                    {
                        TData* pData = (TData*)(pBase + ip + 1);
                        regsPtr[inst->Dest] = *pData;
                        ip += (sizeof(TData) + 7) / 8;
                    }
                    else if (kind == OpType.Instruction)
                    {
                        regsPtr[inst->Dest] = _definition.Compute(operByte, *inst, registers);

                        if (!IsDefault(&regsPtr[0]))
                            return regsPtr[0];
                    }
                    else if (kind == OpType.Return)
                    {
                        returnReg = inst->Dest;
                        // 链式合并的字节码中，Return 之后可能还有后续 link 的指令。
                        // 此时不退出——将输出复制到 R1 总线供下一个 link 消费。
                        if (ip + 1 < raw.Length)
                        {
                            regsPtr[1] = regsPtr[inst->Dest];
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            return IsDefault(&regsPtr[0]) ? regsPtr[returnReg] : regsPtr[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDefault(TData* ptr)
        {
            TData zero = default;
            return new ReadOnlySpan<byte>(ptr, sizeof(TData)).SequenceEqual(
                new ReadOnlySpan<byte>(&zero, sizeof(TData))
            );
        }
    }
}
