using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 单步调试求值器：逐条指令执行，暴露当前状态供外部检查。
    /// <see cref="Step"/> 执行一条指令并返回新实例，旧 state 不受影响。
    /// </summary>
    public readonly struct FluxStepEvaluator<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        private readonly TDef _definition;
        private readonly Instruction[] _bytecode;
        private readonly TData[] _regs;
        private readonly int _ip;
        private readonly int _instrCount;
        private readonly bool _completed;
        private readonly TData _result;

        /// <summary>是否已执行完毕</summary>
        public bool IsCompleted => _completed;

        /// <summary>最终结果（仅在 IsCompleted 时有意义）</summary>
        public TData Result => _result;

        /// <summary>当前指令指针</summary>
        public int CurrentIP => _ip;

        /// <summary>当前指令的操作码（未开始时为 0）</summary>
        public byte CurrentOpCode => _completed || _ip >= _instrCount
            ? (byte)0 : _bytecode[_ip].OpCode;

        /// <summary>当前指令的完整结构体</summary>
        public Instruction CurrentInstruction => _completed || _ip >= _instrCount
            ? default : _bytecode[_ip];

        /// <summary>寄存器文件只读快照</summary>
        public ReadOnlySpan<TData> Regs => _regs;

        /// <summary>指令总数</summary>
        public int InstructionCount => _instrCount;

        // ── 构造 ──

        public static FluxStepEvaluator<TData, TDef> Create(
            TDef definition, FluxFormula<TData, TDef> formula)
        {
            var raw = formula.Raw();
            var bytecode = raw.ToArray();
            int instrCount = formula.Count;
            int regCount = formula.MaxRegister > Registers.Bus
                ? formula.MaxRegister + 1
                : Registers.FirstAlloc;
            var regs = new TData[regCount];
            regs[Registers.Bus] = default;

            return new FluxStepEvaluator<TData, TDef>(
                definition, bytecode, regs, 0, instrCount, false, default);
        }

        private FluxStepEvaluator(
            TDef definition,
            Instruction[] bytecode,
            TData[] regs,
            int ip,
            int instrCount,
            bool completed,
            TData result)
        {
            _definition = definition;
            _bytecode   = bytecode;
            _regs       = regs;
            _ip         = ip;
            _instrCount = instrCount;
            _completed  = completed;
            _result     = result;
        }

        // ── Step ──

        /// <summary>
        /// 执行恰好一条指令，返回新 state。
        /// 若已完成则返回自身。
        /// </summary>
        public FluxStepEvaluator<TData, TDef> Step()
        {
            if (_completed || _ip >= _instrCount)
                return this;

            // 拷贝 regs
            var newRegs = new TData[_regs.Length];
            Array.Copy(_regs, newRegs, _regs.Length);

            int dataSlots = FormulaFormat.DataSlots<TData>();
            int ip = _ip;

            unsafe
            {
                fixed (Instruction* pBase = _bytecode)
                fixed (TData* r = newRegs)
                {
                    Instruction* inst = pBase + ip;
                    byte opByte = inst->OpCode;
                    OpType kind = _definition.GetKind(opByte);

                    if (kind == OpType.Immediate)
                    {
                        r[inst->Dest] = *(TData*)(pBase + ip + 1);
                        ip += 1 + dataSlots;
                    }
                    else if (kind == OpType.Instruction)
                    {
                        r[inst->Dest] = _definition.Compute(opByte, *inst,
                            new Span<TData>(r, newRegs.Length));

                        if (!IsDefault(r + Registers.Error))
                            return new FluxStepEvaluator<TData, TDef>(
                                _definition, _bytecode, newRegs, ip + 1,
                                _instrCount, true, r[Registers.Error]);
                        ip++;
                    }
                    else if (kind == OpType.Return)
                    {
                        if (ip + 1 < _instrCount)
                        {
                            r[Registers.Bus] = r[inst->Dest];
                            ip++;
                        }
                        else
                        {
                            return new FluxStepEvaluator<TData, TDef>(
                                _definition, _bytecode, newRegs, ip + 1,
                                _instrCount, true, r[inst->Dest]);
                        }
                    }
                }
            }

            bool done = ip >= _instrCount;
            return new FluxStepEvaluator<TData, TDef>(
                _definition, _bytecode, newRegs, ip, _instrCount,
                done, done ? newRegs[Registers.Bus] : default);
        }

        /// <summary>
        /// 全速执行到结束（跳过逐条步进），返回最终状态。
        /// </summary>
        public FluxStepEvaluator<TData, TDef> RunToEnd()
        {
            var state = this;
            while (!state._completed && state._ip < state._instrCount)
                state = state.Step();
            return state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsDefault(TData* ptr)
        {
            TData zero = default;
            return new ReadOnlySpan<byte>(ptr, sizeof(TData)).SequenceEqual(
                new ReadOnlySpan<byte>(&zero, sizeof(TData)));
        }
    }
}
