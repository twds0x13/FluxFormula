using System;
using System.Diagnostics;
using FluxFormula.Compiler;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 可执行的公式流式包装器 (Fluent API)。
    /// 支持原子公式（单次 JIT/解释器求值）、链式解释器求值、链式 JIT 求值。
    /// </summary>
    public ref struct FluxInstance<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        private readonly TDef _definition;
        private readonly FluxFormula<TData, TDef> _formula;
        private readonly FluxJITCompiler<TData, TDef>.CompiledFunc _jitFunc;
        private readonly bool _isJit;

        private FluxInjector<TData> _injector;

        // ── 链式 JIT ──
        private readonly FluxJITCompiler<TData, TDef>.CompiledFunc[] _chainFuncs;
        private readonly FluxInjector<TData>[] _chainInjectors;

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TDef> formula,
            FluxInjector<TData> injector,
            FluxJITCompiler<TData, TDef>.CompiledFunc jitFunc,
            bool isJit)
        {
            _definition     = definition;
            _formula        = formula;
            _injector       = injector;
            _jitFunc        = jitFunc;
            _isJit          = isJit;
            _chainFuncs     = null;
            _chainInjectors = null;
        }

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TDef> formula,
            FluxInjector<TData> mergedInjector,
            FluxJITCompiler<TData, TDef>.CompiledFunc[] chainFuncs,
            FluxInjector<TData>[] chainInjectors)
        {
            _definition     = definition;
            _formula        = formula;
            _injector       = mergedInjector;
            _jitFunc        = null;
            _isJit          = true;
            _chainFuncs     = chainFuncs;
            _chainInjectors = chainInjectors;
        }

        // ================= 流式数据注入 =================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TDef> SetIndex(int index, TData value)
        {
            _injector = _injector.SetIndex(index, value);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TDef> Set(string name, TData value)
        {
            _injector = _injector.Set(name, value);
            return this;
        }

        // ================= 求值 =================

        public readonly TData Run()
        {
            Debug.Assert(_formula.Type != FluxType.Modifier,
                "Modifier cannot run standalone. Use ToFormula() to provide a first operand.");

            if (_chainFuncs != null)
            {
                return RunJitChain();
            }
            else if (_isJit)
            {
                return _jitFunc(_injector.GetBuffer());
            }
            else if (_formula.IsChained)
            {
                return RunChainInterpreter();
            }
            else
            {
                var kernel = new FluxEvaluator<TData, TDef>(_definition);
                return kernel.Compute(_injector.GetBuffer().AsSpan(0, _formula.Count),
                    maxRegister: _formula.MaxRegister);
            }
        }

        /// <summary>
        /// Per-link JIT 链式求值：逐 link 调用 JIT delegate，通过内部变量注入串联结果。
        /// Modifier 链路已在 Instantiate 中通过 ToFormula(CHAIN_LINK_INTERNAL_0) 适配。
        /// </summary>
        private readonly TData RunJitChain()
        {
            TData prevResult = default;

            for (int i = 0; i < _chainFuncs.Length; i++)
            {
                var injector = _chainInjectors[i];
                if (i > 0)
                {
                    // 将前一个 link 的输出注入到当前 link 的第一个数据槽位
                    injector = injector.SetIndex(0, prevResult);
                }
                prevResult = _chainFuncs[i](injector.GetBuffer());
            }

            return prevResult;
        }

        /// <summary>
        /// 链式解释器求值：逐 link 通过 R1 总线串联。
        /// </summary>
        private readonly TData RunChainInterpreter()
        {
            var links  = _formula.GetChainLinks();
            var kernel = new FluxEvaluator<TData, TDef>(_definition);
            TData prevResult = default;

            for (int i = 0; i < links.Length; i++)
            {
                var buffer = BuildLinkBuffer(links[i]);
                prevResult = (i == 0)
                    ? kernel.Compute(buffer, maxRegister: links[i].MaxRegister)
                    : kernel.Compute(buffer, prevResult, maxRegister: links[i].MaxRegister);
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
                int dataSlotsPerParam = FormulaFormat.DataSlots<TData>();
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
