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
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        private readonly TDef _definition;
        private readonly FluxFormula<TData, TDef> _formula;
        private readonly CompiledFunc<TData> _jitFunc;
        private readonly bool _isJit;

        private FluxInjector<TData> _injector;

        // ── 链式 JIT ──
        private readonly CompiledFunc<TData>[] _chainFuncs;
        private readonly FluxInjector<TData>[] _chainInjectors;
        /// <summary>[linkIndex][varIndex] = 全局 SlotIndex，用于从 merged injector 读取用户变量值</summary>
        private readonly int[][] _chainUserGlobalSlots;
        /// <summary>[linkIndex][varIndex] = per-link payload 内的局部 slot，用于 SetIndex 写入</summary>
        private readonly int[][] _chainUserLocalSlots;

        // ── 链式表示（解释器链式路径或 JIT 降级）──
        private readonly FluxChain<TData, TDef> _chain;

        // ── 原子构造器 ──

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TDef> formula,
            FluxInjector<TData> injector,
            CompiledFunc<TData> jitFunc,
            bool isJit)
        {
            _definition     = definition;
            _formula        = formula;
            _injector       = injector;
            _jitFunc        = jitFunc;
            _isJit          = isJit;
            _chainFuncs     = null;
            _chainInjectors = null;
            _chainUserGlobalSlots = null;
            _chainUserLocalSlots  = null;
            _chain          = default;
        }

        // ── 链式 JIT 构造器 ──

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TDef> mergedFormula,
            FluxInjector<TData> mergedInjector,
            CompiledFunc<TData>[] chainFuncs,
            FluxInjector<TData>[] chainInjectors,
            int[][] chainUserGlobalSlots,
            int[][] chainUserLocalSlots,
            FluxChain<TData, TDef> chain)
        {
            _definition     = definition;
            _formula        = mergedFormula;
            _injector       = mergedInjector;
            _jitFunc        = null;
            _isJit          = true;
            _chainFuncs     = chainFuncs;
            _chainInjectors = chainInjectors;
            _chainUserGlobalSlots = chainUserGlobalSlots;
            _chainUserLocalSlots  = chainUserLocalSlots;
            _chain          = chain;
        }

        // ── 链式解释器构造器 ──

        internal FluxInstance(
            TDef definition,
            FluxFormula<TData, TDef> mergedFormula,
            FluxInjector<TData> injector,
            CompiledFunc<TData> jitFunc,
            bool isJit,
            FluxChain<TData, TDef> chain)
        {
            _definition     = definition;
            _formula        = mergedFormula;
            _injector       = injector;
            _jitFunc        = jitFunc;
            _isJit          = isJit;
            _chainFuncs     = null;
            _chainInjectors = null;
            _chainUserGlobalSlots = null;
            _chainUserLocalSlots  = null;
            _chain          = chain;
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
            Debug.Assert(_chain.Length > 0 || _formula.Type != FluxType.Modifier,
                "Modifier cannot run standalone. Use ToFormula() to provide a first operand.");

            if (_chainFuncs != null)
            {
                return RunJitChain();
            }
            else if (_isJit)
            {
                return _jitFunc(_injector.GetBuffer());
            }
            else if (_chain.Length > 0)
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
        /// </summary>
        private readonly TData RunJitChain()
        {
            TData prevResult = default;

            for (int i = 0; i < _chainFuncs.Length; i++)
            {
                var injector = _chainInjectors[i];

                // 从 merged injector 传播用户变量值到 per-link injector
                var globalSlots = _chainUserGlobalSlots[i];
                var localSlots  = _chainUserLocalSlots[i];
                for (int v = 0; v < globalSlots.Length; v++)
                {
                    TData value = _injector.GetValue(globalSlots[v]);
                    injector = injector.SetIndex(localSlots[v], value);
                }

                if (i > 0)
                {
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
            var links  = _chain.GetLinks();
            var kernel = new FluxEvaluator<TData, TDef>(_definition);
            TData prevResult = default;
            int globalImmBase = 0;

            for (int i = 0; i < links.Length; i++)
            {
                var buffer = BuildLinkBuffer(links[i], globalImmBase);
                prevResult = (i == 0)
                    ? kernel.Compute(buffer, maxRegister: links[i].MaxRegister)
                    : kernel.Compute(buffer, prevResult, maxRegister: links[i].MaxRegister);
                globalImmBase += links[i].ImmediateCount;
            }

            return prevResult;
        }

        /// <summary>
        /// 为单个 chain link 构建求值用 Instruction[]，从 injector 回读变量值注入。
        /// <paramref name="globalImmBase"/> 是之前所有 link 的 Immediate 总数，
        /// 用于将 VarSlot 的全局 SlotIndex 转换回 link 内的局部位置。
        /// </summary>
        private readonly Instruction[] BuildLinkBuffer(ChainLink link, int globalImmBase)
        {
            var buffer = new Instruction[link.InstructionCount];
            Array.Copy(link.Bytecode, 0, buffer, 0, link.InstructionCount);

            if (link.VarSlots.Length > 0)
            {
                int dataSlotsPerParam = FormulaFormat.DataSlots<TData>();
                int varIdx = 0;
                int localImmIdx = 0;
                for (int ip = 0; ip < link.InstructionCount && varIdx < link.VarSlots.Length; )
                {
                    if (_definition.GetKind(buffer[ip].OpCode) == OpType.Immediate)
                    {
                        if (link.VarSlots[varIdx].SlotIndex == globalImmBase + localImmIdx)
                        {
                            TData value = _injector.GetValue(link.VarSlots[varIdx].SlotIndex);
                            unsafe
                            {
                                fixed (Instruction* pBase = buffer)
                                    *(TData*)(pBase + ip + 1) = value;
                            }
                            varIdx++;
                        }
                        localImmIdx++;
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

        internal readonly Instruction[] GetBuffer() => _injector.GetBuffer();
    }
}
