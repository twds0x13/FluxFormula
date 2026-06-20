using System;
using FluxFormula.Compiler;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 可执行的公式流式包装器 (Fluent API)。
    /// 支持原子公式（单次 JIT/解释器求值）和链式公式（per-link 解释器求值）。
    /// </summary>
    public ref struct FluxInstance<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        private readonly TDef _definition;
        private readonly FluxFormula<TData, TOper> _formula;
        private readonly FluxJITCompiler<TData, TOper, TDef>.CompiledFunc _jitFunc;
        private readonly bool _isJit;

        private FluxInjector<TData> _injector;

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TOper> formula,
            FluxInjector<TData> injector,
            FluxJITCompiler<TData, TOper, TDef>.CompiledFunc jitFunc,
            bool isJit)
        {
            _definition = definition;
            _formula    = formula;
            _injector   = injector;
            _jitFunc    = jitFunc;
            _isJit      = isJit;
        }

        // ================= 流式数据注入 =================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> SetIndex(int index, TData value)
        {
            _injector = _injector.SetIndex(index, value);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> Set(string name, TData value)
        {
            _injector = _injector.Set(name, value);
            return this;
        }

        // ================= 求值 =================

        public readonly TData Run()
        {
            if (_formula.Type != FluxType.Formula)
                throw new InvalidOperationException("Modifier cannot run standalone.");

            if (_isJit)
            {
                return _jitFunc(_injector.GetBuffer());
            }
            else if (_formula.IsChained)
            {
                return RunChainInterpreter();
            }
            else
            {
                var kernel = new FluxEvaluator<TData, TOper, TDef>(_definition);
                return kernel.Compute(_injector.GetBuffer().AsSpan(0, _formula.Count));
            }
        }

        /// <summary>
        /// 链式解释器求值：逐 link 通过 R1 总线串联。
        /// </summary>
        private readonly TData RunChainInterpreter()
        {
            var links  = _formula.GetChainLinks();
            var kernel = new FluxEvaluator<TData, TOper, TDef>(_definition);
            TData prevResult = default;

            for (int i = 0; i < links.Length; i++)
            {
                var buffer = BuildLinkBuffer(links[i]);
                prevResult = (i == 0)
                    ? kernel.Compute(buffer)
                    : kernel.Compute(buffer, prevResult);
            }

            return prevResult;
        }

        /// <summary>
        /// 为单个 chain link 构建求值用 Instruction[]，从 injector 回读变量值注入。
        /// </summary>
        private readonly Instruction[] BuildLinkBuffer(ChainLink link)
        {
            var buffer = new Instruction[link.InstructionCount];
            Array.Copy(link.Bytecode, 0, buffer, 0, link.InstructionCount);

            if (link.VarSlots.Length > 0)
            {
                int dataSlotsPerParam; unsafe { dataSlotsPerParam = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction); }
                int varIdx = 0;
                for (int ip = 0; ip < link.InstructionCount && varIdx < link.VarSlots.Length; )
                {
                    if (_definition.GetKind(buffer[ip].OpCode) == OpType.Immediate)
                    {
                        // 从 injector 直接按 SlotIndex 读取已注入的值
                        TData value = _injector.GetValue(link.VarSlots[varIdx].SlotIndex);
                        unsafe
                        {
                            fixed (Instruction* pBase = buffer)
                                *(TData*)(pBase + ip + 1) = value;
                        }
                        varIdx++;
                        ip += 1 + dataSlotsPerParam;
                    }
                    else
                    {
                        ip++;
                    }
                }
            }

            return buffer;
        }

        public readonly Instruction[] GetBuffer() => _injector.GetBuffer();
    }
}
