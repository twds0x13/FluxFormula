using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 分步求值器：按名或按顺序渐进绑定变量，每次 <see cref="Bind"/> 返回新实例。
    /// 函数式 State→State 模型，旧 state 不受影响，支持中途分叉。
    /// </summary>
    public readonly struct FluxCurryEvaluator<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        // 不可变（所有实例共享）
        private readonly TDef _definition;
        private readonly Instruction[] _bytecode;
        private readonly int[] _varImmIndices;
        private readonly string[] _varNames;
        private readonly int _immCount;
        private readonly int _instrCount;
        private readonly byte _maxRegister;

        // 可变（Bind 时拷贝）
        private readonly TData[] _regs;
        private readonly TData[] _boundValues;
        private readonly bool[] _boundMask;
        private readonly int _boundCount;
        private readonly int _ip;
        private readonly bool _completed;
        private readonly TData _result;

        public bool IsCompleted => _completed;

        /// <summary>
        /// 求值结果。掩码未满时抛出 <see cref="InvalidOperationException"/>，
        /// 需调用 <see cref="ForceComplete"/> 显式以 default 填充剩余变量。
        /// </summary>
        public TData Result
        {
            get
            {
                if (_completed) return _result;
                throw new InvalidOperationException(
                    $"Not all variables bound ({_boundCount}/{_varImmIndices.Length}). " +
                    "Call ForceComplete() to fill remaining variables with default, " +
                    "or bind all variables before accessing Result.");
            }
        }

        public int BoundCount => _boundCount;
        public int VariableCount => _varImmIndices.Length;

        // ── 构造 ──

        public static FluxCurryEvaluator<TData, TDef> Create(
            TDef definition, FluxFormula<TData, TDef> formula)
        {
            var raw = formula.Raw();
            var bytecode = raw.ToArray();
            int instrCount = formula.Count;
            int immCount = formula.ImmediateCount;

            var varSlots = formula.VariableSlots;
            var varImmIndices = new int[varSlots.Length];
            var varNames = new string[varSlots.Length];
            for (int i = 0; i < varSlots.Length; i++)
            {
                varImmIndices[i] = varSlots[i].SlotIndex;
                varNames[i] = varSlots[i].Name;
            }

            int regCount = formula.MaxRegister > Registers.Bus
                ? formula.MaxRegister + 1
                : Registers.FirstAlloc;
            var regs = new TData[regCount];
            regs[Registers.Bus] = default;

            int varCount = varSlots.Length;
            var state = new FluxCurryEvaluator<TData, TDef>(
                definition, bytecode, varImmIndices, varNames, immCount, instrCount,
                formula.MaxRegister, regs,
                new TData[varCount], new bool[varCount], 0,
                0, false, default);

            return Resume(state);
        }

        private FluxCurryEvaluator(
            TDef definition,
            Instruction[] bytecode,
            int[] varImmIndices,
            string[] varNames,
            int immCount,
            int instrCount,
            byte maxRegister,
            TData[] regs,
            TData[] boundValues,
            bool[] boundMask,
            int boundCount,
            int ip,
            bool completed,
            TData result)
        {
            _definition    = definition;
            _bytecode      = bytecode;
            _varImmIndices = varImmIndices;
            _varNames      = varNames;
            _immCount      = immCount;
            _instrCount    = instrCount;
            _maxRegister   = maxRegister;
            _regs          = regs;
            _boundValues   = boundValues;
            _boundMask     = boundMask;
            _boundCount    = boundCount;
            _ip            = ip;
            _completed     = completed;
            _result        = result;
        }

        // ── Bind（按名）──

        /// <summary>
        /// 按名绑定单个变量（乱序），注入后执行到下一个挂起点或完成。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> Bind(string name, TData value)
        {
            if (_completed) return this;

            int idx = -1;
            for (int i = 0; i < _varNames.Length; i++)
            {
                if (_varNames[i] == name)
                {
                    if (_boundMask[i])
                        throw new ArgumentException(
                            $"Variable '{name}' is already bound.", nameof(name));
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
                throw new ArgumentException(
                    $"Variable '{name}' not found in formula.", nameof(name));

            return BindAt(idx, value);
        }

        /// <summary>
        /// 按名安全绑定单个变量。变量名不存在或已绑定时静默跳过。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> TryBind(string name, TData value)
        {
            if (_completed) return this;

            for (int i = 0; i < _varNames.Length; i++)
            {
                if (_varNames[i] == name)
                {
                    if (_boundMask[i])
                        return this; // 已绑定，跳过
                    return BindAt(i, value);
                }
            }
            return this; // 变量名不存在，跳过
        }

        // ── Bind（顺次）──

        /// <summary>
        /// 依次绑定接下来的 N 个未绑定变量，注入后执行到下一个挂起点或完成。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> Bind(params TData[] values)
        {
            if (_completed) return this;
            if (values == null || values.Length == 0) return this;

            var state = this;
            foreach (var v in values)
            {
                // 找下一个未绑定位置
                int next = -1;
                var mask = state._boundMask;
                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i]) { next = i; break; }
                }
                if (next < 0) break;
                state = state.BindAt(next, v);
                if (state._completed) break;
            }
            return state;
        }

        /// <summary>
        /// 依次安全绑定接下来的 N 个未绑定变量。已满时静默停止。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> TryBind(params TData[] values)
        {
            if (_completed) return this;
            if (values == null || values.Length == 0) return this;

            var state = this;
            foreach (var v in values)
            {
                int next = -1;
                var mask = state._boundMask;
                for (int i = 0; i < mask.Length; i++)
                {
                    if (!mask[i]) { next = i; break; }
                }
                if (next < 0) break;
                state = state.BindAt(next, v);
                if (state._completed) break;
            }
            return state;
        }

        // ── 内部：绑定指定位置 ──

        private FluxCurryEvaluator<TData, TDef> BindAt(int index, TData value)
        {
            var newBound = new TData[_boundValues.Length];
            Array.Copy(_boundValues, newBound, _boundValues.Length);
            newBound[index] = value;

            var newMask = new bool[_boundMask.Length];
            Array.Copy(_boundMask, newMask, _boundMask.Length);
            newMask[index] = true;

            var newRegs = new TData[_regs.Length];
            Array.Copy(_regs, newRegs, _regs.Length);

            int newCount = _boundCount + 1;
            var state = new FluxCurryEvaluator<TData, TDef>(
                _definition, _bytecode, _varImmIndices, _varNames,
                _immCount, _instrCount, _maxRegister,
                newRegs, newBound, newMask, newCount, _ip, false, default);

            return Resume(state);
        }

        // ── 执行核心 ──

        private static FluxCurryEvaluator<TData, TDef> Resume(
            FluxCurryEvaluator<TData, TDef> state)
        {
            var def      = state._definition;
            var bytecode = state._bytecode;
            var regs     = state._regs;
            var varImm   = state._varImmIndices;
            var bound    = state._boundValues;
            var mask     = state._boundMask;
            int dataSlots = FormulaFormat.DataSlots<TData>();
            int varPtr   = 0;
            int immIdx   = 0;
            int instrCount = state._instrCount;

            // 快进 immIdx / varPtr 到 IP
            for (int s = 0; s < state._ip && s < instrCount; )
            {
                if (def.GetKind(bytecode[s].OpCode) == OpType.Immediate)
                {
                    immIdx++;
                    s += 1 + dataSlots;
                }
                else s++;
            }
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
                            bool isVar = varPtr < varImm.Length && varImm[varPtr] == immIdx;
                            if (isVar)
                            {
                                if (!mask[varPtr])
                                {
                                    // 未绑定：挂起
                                    return new FluxCurryEvaluator<TData, TDef>(
                                        state._definition, state._bytecode, varImm, state._varNames,
                                        state._immCount, instrCount, state._maxRegister,
                                        regs, bound, mask, state._boundCount, ip, false, default);
                                }
                                r[inst->Dest] = bound[varPtr];
                                varPtr++;
                            }
                            else
                            {
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
                                    state._definition, state._bytecode, varImm, state._varNames,
                                    state._immCount, instrCount, state._maxRegister,
                                    regs, bound, mask, state._boundCount, ip + 1, true, r[Registers.Error]);
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
                                    state._definition, state._bytecode, varImm, state._varNames,
                                    state._immCount, instrCount, state._maxRegister,
                                    regs, bound, mask, state._boundCount, ip + 1, true, finalResult);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Unknown OpType in curry evaluator: {kind} (opCode=0x{opByte:X2}).");
                        }
                    }

                    return new FluxCurryEvaluator<TData, TDef>(
                        state._definition, state._bytecode, varImm, state._varNames,
                        state._immCount, instrCount, state._maxRegister,
                        regs, bound, mask, state._boundCount, ip, true, default);
                }
            }
        }

        /// <summary>
        /// 剩余未绑定变量按 default 填充，全速求值到结束。
        /// </summary>
        public FluxCurryEvaluator<TData, TDef> ForceComplete()
        {
            if (_completed) return this;

            // 填满剩余未绑定槽位
            var state = this;
            for (int i = 0; i < _boundMask.Length; i++)
            {
                if (!_boundMask[i])
                    state = state.BindAt(i, default);
            }
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
