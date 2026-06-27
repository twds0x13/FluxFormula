using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 平台能力检测与 JIT 降级状态管理
    /// </summary>
    internal static class FluxPlatform
    {
        /// <summary>最大寄存器数量。与 <see cref="Registers.Max"/> 保持一致。</summary>
        internal const int MaxRegisters = 255;

        private static volatile bool _jitDisabled;

        /// <summary>
        /// 当前平台是否不支持 JIT (IL2CPP/AOT 导致 Expression.Compile 失败)
        /// </summary>
        public static bool IsJitDisabled => _jitDisabled;

        /// <summary>
        /// 当前平台是否支持 IL 发射（<see cref="System.Reflection.Emit.DynamicMethod"/>）。
        /// IL2CPP / NativeAOT 返回 false——此时 IL 编译器不可用，自动降级到 Expression 树路径。
        /// </summary>
        public static bool IsIlSupported =>
            System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

        /// <summary>
        /// JIT 降级时触发。参数为异常消息。Unity 端可订阅以输出 <c>Debug.LogWarning</c>。
        /// Core 包无 UnityEngine 引用，故此事件保持纯 System.Action。
        /// </summary>
        public static event Action<string> OnJitDisabled;

        /// <summary>
        /// 标记 JIT 不可用，之后同进程内不再尝试。
        /// 调用前会触发 <see cref="OnJitDisabled"/> 事件携带原因。
        /// </summary>
        public static void DisableJit(string reason = null)
        {
            if (_jitDisabled) return;
            _jitDisabled = true;
            if (reason != null)
                OnJitDisabled?.Invoke(reason);
        }

        /// <summary>
        /// 重置 JIT 禁用标志（仅测试用）。
        /// 生产环境中不应调用：一旦探测到平台不支持 JIT，应保持禁用。
        /// </summary>
        public static void ResetJit()
        {
            _jitDisabled = false;
        }
    }

    internal readonly unsafe ref struct FluxEvaluator<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxDefinition<TData>
    {
        private readonly TDef _definition;

        public FluxEvaluator(TDef definition)
        {
            _definition = definition;
        }

        /// <summary>标准求值，R1 从 default(TData) 开始</summary>
        public TData Compute(ReadOnlySpan<Instruction> raw, byte maxRegister = 0)
        {
            return ComputeCore(raw, default, maxRegister);
        }

        /// <summary>
        /// 链式求值入口：R1 从 initialR1 开始而非 default(TData)。
        /// 用于链式公式的 per-link 解释器求值：前一个 link 的输出通过 R1 总线传入下一个 link。
        /// </summary>
        public TData Compute(ReadOnlySpan<Instruction> raw, TData initialR1, byte maxRegister = 0)
        {
            return ComputeCore(raw, initialR1, maxRegister);
        }

        private TData ComputeCore(ReadOnlySpan<Instruction> raw, TData initialR1, byte maxRegister)
        {
            // 扫描 bytecode 获取实际最大寄存器号（防御 ToFormula 等操作后 MaxRegister 未更新）
            byte actualMax = maxRegister > Registers.Bus ? maxRegister : Registers.Bus;
            for (int i = 0; i < raw.Length; i++)
            {
                var inst = raw[i];
                if (inst.Dest > actualMax) actualMax = inst.Dest;
                if (inst.Arg0 > actualMax) actualMax = inst.Arg0;
                if (inst.Arg1 > actualMax) actualMax = inst.Arg1;
                if (inst.Arg2 > actualMax) actualMax = inst.Arg2;
                if (inst.Arg3 > actualMax) actualMax = inst.Arg3;
                if (inst.Arg4 > actualMax) actualMax = inst.Arg4;
                if (inst.Arg5 > actualMax) actualMax = inst.Arg5;
            }
            int regCount = actualMax + 1;
            // 栈上分配寄存器文件，手工对齐到 64 字节（cache line）边界。
            // +63 将指针推入下一 cache line 对齐窗口，& ~63 向下取整到 64 字节边界。
            // 避免寄存器数组跨越 cache line 导致 Burst / CPU 访问两次 L1。
            byte* rawPtr = stackalloc byte[sizeof(TData) * regCount + 63];
            long addr = (long)rawPtr;
            TData* regsPtr = (TData*)((addr + 63) & ~63);
            Span<TData> registers = new(regsPtr, regCount);

            regsPtr[Registers.Error] = default;
            regsPtr[Registers.Bus]  = initialR1;
            byte returnReg = Registers.Bus;

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
                        ip += FormulaFormat.DataSlots<TData>();
                    }
                    else if (kind == OpType.Instruction)
                    {
                        regsPtr[inst->Dest] = _definition.Compute(operByte, *inst, registers);

                        if (!IsDefault(&regsPtr[Registers.Error]))
                            return regsPtr[Registers.Error];
                    }
                    else if (kind == OpType.Return)
                    {
                        returnReg = inst->Dest;
                        // 链式合并的字节码中，Return 之后可能还有后续 link 的指令。
                        // 此时不退出：将输出复制到 R1 总线供下一个 link 消费。
                        if (ip + 1 < raw.Length)
                        {
                            regsPtr[Registers.Bus] = regsPtr[inst->Dest];
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Unknown OpType in evaluator: {kind} (opCode=0x{operByte:X2}). " +
                            "If you added a new OpType, update the evaluator dispatch.");
                    }
                }
            }

            return IsDefault(&regsPtr[Registers.Error]) ? regsPtr[returnReg] : regsPtr[Registers.Error];
        }

        /// <summary>
        /// 判断 <typeparamref name="TData"/> 值是否等于 default。
        /// 不走 <c>== default</c>（unmanaged 约束不保证相等运算符），
        /// 改用 <c>ReadOnlySpan&lt;byte&gt;.SequenceEqual</c> 做逐字节比较。
        /// </summary>
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
