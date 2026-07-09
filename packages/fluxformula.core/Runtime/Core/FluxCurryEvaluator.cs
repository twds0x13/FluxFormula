using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 柯里化求值器：按顺序渐进绑定变量，每次 <see cref="Bind"/> 返回新实例。
    /// 函数式 State→State 模型，旧 state 不受影响，支持中途分叉。
    /// </summary>
    public readonly struct FluxCurryEvaluator<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        // 不可变（所有实例共享）
        private readonly TDef _definition;
        private readonly Instruction[] _bytecode;
        /// <summary>变量在 Immediate 序列中的位置索引（升序），来自 VariableSlots 的 SlotIndex</summary>
        private readonly int[] _varImmIndices;
        private readonly int _immCount;
        private readonly int _instrCount;
        private readonly byte _maxRegister;

        // 可变（Bind 时拷贝）
        private readonly TData[] _regs;
        private readonly TData[] _boundValues;
        private readonly int _bindCursor;
        private readonly int _ip;
        private readonly bool _completed;
        private readonly TData _result;

        /// <summary>是否已完成求值</summary>
        public bool IsCompleted => _completed;

        /// <summary>已完成时的求值结果</summary>
        public TData Result
        {
            get
            {
                if (_completed) return _result;
                // 未完成：剩余未绑定变量填 default，全速求值
                return ForceComplete()._result;
            }
        }

        /// <summary>当前已绑定变量数</summary>
        public int BoundCount => _bindCursor;

        /// <summary>变量总数</summary>
        public int VariableCount => _varImmIndices.Length;

        // ── 构造 ──

        /// <summary>
        /// 从公式创建柯里化求值器。
        /// </summary>
        public static FluxCurryEvaluator<TData, TDef> Create(
            TDef definition, FluxFormula<TData, TDef> formula)
        {
            var raw = formula.Raw();
            var bytecode = raw.ToArray();
            int instrCount = formula.Count;
            int immCount = formula.ImmediateCount;

            // 提取变量的 Immediate 序号（升序）
            var varSlots = formula.VariableSlots;
            var varImmIndices = new int[varSlots.Length];
            for (int i = 0; i < varSlots.Length; i++)
                varImmIndices[i] = varSlots[i].SlotIndex;

            int regCount = formula.MaxRegister > Registers.Bus
                ? formula.MaxRegister + 1
                : Registers.FirstAlloc;
            var regs = new TData[regCount];
            regs[Registers.Bus] = default;

            var state = new FluxCurryEvaluator<TData, TDef>(
                definition, bytecode, varImmIndices, immCount, instrCount,
                formula.MaxRegister, regs, Array.Empty<TData>(), 0, 0, false, default);

            // 执行到第一个挂起点（或完成）
            return Resume(state);
        }

        private FluxCurryEvaluator(
            TDef definition,
            Instruction[] bytecode,
            int[] varImmIndices,
            int immCount,
            int instrCount,
            byte maxRegister,
            TData[] regs,
            TData[] boundValues,
            int bindCursor,
            int ip,
            bool completed,
            TData result)
        {
            _definition    = definition;
            _bytecode      = bytecode;
            _varImmIndices = varImmIndices;
            _immCount      = immCount;
            _instrCount    = instrCount;
            _maxRegister   = maxRegister;
            _regs          = regs;
            _boundValues   = boundValues;
            _bindCursor    = bindCursor;
            _ip            = ip;
            _completed     = completed;
            _result        = result;
        }

        // ── Bind ──

        /// <summary>
        /// 绑定接下来的 N 个变量值，注入后执行到下一个挂起点或完成。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
        {
            if (_completed) return this;
            if (values == null || values.Length == 0) return this;

            int newCursor = _bindCursor + values.Length;
            if (newCursor > _varImmIndices.Length)
                newCursor = _varImmIndices.Length;

            // 拷贝 boundValues 并追加新值
            var newBound = new TData[newCursor > _boundValues.Length ? newCursor : _boundValues.Length];
            Array.Copy(_boundValues, newBound, _bindCursor);
            int copyCount = Math.Min(values.Length, _varImmIndices.Length - _bindCursor);
            Array.Copy(values, 0, newBound, _bindCursor, copyCount);

            // 拷贝 regs
            var newRegs = new TData[_regs.Length];
            Array.Copy(_regs, newRegs, _regs.Length);

            var state = new FluxCurryEvaluator<TData, TDef>(
                _definition, _bytecode, _varImmIndices, _immCount, _instrCount,
                _maxRegister, newRegs, newBound, newCursor, _ip, false, default);

            return Resume(state);
        }

        // ── 执行核心 ──

        /// <summary>
        /// 从当前 IP 继续执行，直到遇到未绑定变量挂起或执行完毕。
        /// </summary>
        private static FluxCurryEvaluator<TData, TDef> Resume(
            FluxCurryEvaluator<TData, TDef> state)
        {
            var def     = state._definition;
            var bytecode = state._bytecode;
            var regs    = state._regs;
            var varImm  = state._varImmIndices;
            var bound   = state._boundValues;
            int cursor  = state._bindCursor;
            int dataSlots = FormulaFormat.DataSlots<TData>();
            int varPtr  = 0; // _varImmIndices 的游标
            int immIdx  = 0; // 当前已扫描的 Immediate 计数
            int instrCount = state._instrCount;

            // 快进 immIdx 到 IP 之前的 Immediate 数
            for (int s = 0; s < state._ip && s < instrCount; )
            {
                if (def.GetKind(bytecode[s].OpCode) == OpType.Immediate)
                {
                    immIdx++;
                    s += 1 + dataSlots;
                }
                else s++;
            }
            // 快进 varPtr
            while (varPtr < varImm.Length && varImm[varPtr] < immIdx)
                varPtr++;

            unsafe
            {
                fixed (Instruction* pBase = bytecode)
                fixed (TData* r = regs)
                {
                    int ip = state._ip;
                    while (ip < instrCount)
                    {
                        Instruction* inst = pBase + ip;
                        byte opByte = inst->OpCode;
                        OpType kind = def.GetKind(opByte);

                        if (kind == OpType.Immediate)
                        {
                            // 检查是否变量 & 是否已绑定
                            bool isVar = varPtr < varImm.Length && varImm[varPtr] == immIdx;
                            if (isVar)
                            {
                                if (varPtr >= cursor)
                                {
                                    // 未绑定：挂起
                                    return new FluxCurryEvaluator<TData, TDef>(
                                        state._definition, state._bytecode, varImm,
                                        state._immCount, instrCount, state._maxRegister,
                                        regs, bound, cursor, ip, false, default);
                                }
                                r[inst->Dest] = bound[varPtr];
                                varPtr++;
                            }
                            else
                            {
                                // 常量：从字节码读
                                r[inst->Dest] = *(TData*)(pBase + ip + 1);
                            }
                            immIdx++;
                            ip += 1 + dataSlots;
                        }
                        else if (kind == OpType.Instruction)
                        {
                            r[inst->Dest] = def.Compute(opByte, *inst,
                                new Span<TData>(r, regs.Length));

                            if (!IsDefault(r + Registers.Error))
                                return new FluxCurryEvaluator<TData, TDef>(
                                    state._definition, state._bytecode, varImm,
                                    state._immCount, instrCount, state._maxRegister,
                                    regs, bound, cursor, ip + 1, true, r[Registers.Error]);
                            ip++;
                        }
                        else if (kind == OpType.Return)
                        {
                            byte returnReg = inst->Dest;
                            if (ip + 1 < instrCount)
                            {
                                r[Registers.Bus] = r[inst->Dest];
                                ip++;
                            }
                            else
                            {
                                TData finalResult = IsDefault(r + Registers.Error)
                                    ? r[returnReg] : r[Registers.Error];
                                return new FluxCurryEvaluator<TData, TDef>(
                                    state._definition, state._bytecode, varImm,
                                    state._immCount, instrCount, state._maxRegister,
                                    regs, bound, cursor, ip + 1, true, finalResult);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Unknown OpType in curry evaluator: {kind} (opCode=0x{opByte:X2}).");
                        }
                    }

                    // 执行完所有指令
                    return new FluxCurryEvaluator<TData, TDef>(
                        state._definition, state._bytecode, varImm,
                        state._immCount, instrCount, state._maxRegister,
                        regs, bound, cursor, ip, true, default);
                }
            }
        }

        /// <summary>
        /// 剩余未绑定变量按 default 填充，全速求值到结束。
        /// </summary>
        private FluxCurryEvaluator<TData, TDef> ForceComplete()
        {
            int remaining = _varImmIndices.Length - _bindCursor;
            if (remaining <= 0)
            {
                // 全部变量已绑定，继续执行到结束
                return Resume(new FluxCurryEvaluator<TData, TDef>(
                    _definition, _bytecode, _varImmIndices, _immCount, _instrCount,
                    _maxRegister, _regs, _boundValues, _bindCursor, _ip, false, default));
            }

            var defaults = new TData[remaining];
            return Bind(defaults);
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
