using System;
using FluxFormula.Compiler;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 可执行的公式流式包装器 (Fluent API)
    /// 允许无缝连缀注入参数并执行，零 GC 分配
    /// </summary>
    public struct FluxInstance<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        private readonly TDef _provider;
        private readonly FluxFormula<TData, TOper> _formula;
        private readonly FluxJITCompiler<TData, TOper, TDef>.CompiledFunc _jitFunc;
        private readonly bool _isJit;

        private FluxBinder<TData> _injector;

        internal FluxInstance(
            TDef provider,
            FluxFormula<TData, TOper> formula,
            FluxBinder<TData> injector,
            FluxJITCompiler<TData, TOper, TDef>.CompiledFunc jitFunc,
            bool isJit)
        {
            _provider = provider;
            _formula = formula;
            _injector = injector;
            _jitFunc = jitFunc;
            _isJit = isJit;
        }

        // ================= 流式数据注入 =================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> Inject(int index, TData value)
        {
            _injector = _injector.Inject(index, value);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> InjectNext(TData value)
        {
            _injector = _injector.InjectNext(value);
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> Seek(int index)
        {
            _injector = _injector.Seek(index);
            return this;
        }

        /// <summary>
        /// 启动计算引擎 (自动适配底层是 JIT 还是 解释器)
        /// </summary>
        public readonly TData Run()
        {
            if (_formula.Type != FluxType.Formula)
                throw new InvalidOperationException("Modifier cannot run standalone.");

            if (_isJit)
            {
                // 直接将隐藏的纯净 Payload 塞进委托
                return _jitFunc(_injector.Buffer);
            }
            else
            {
                // 使用修改好的 IL 数组让 Kernel 计算
                var kernel = new FluxEvaluator<TData, TOper, TDef>(_provider);
                return kernel.Compute(_formula.Raw());
            }
        }
    }
}