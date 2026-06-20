using System;
using FluxFormula.Compiler;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 可执行的公式流式包装器 (Fluent API)
    /// 允许无缝连缀注入参数并执行，零 GC 分配
    /// </summary>
    public ref struct FluxInstance<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        private readonly TDef _provider;
        private readonly FluxFormula<TData, TOper> _formula;
        private readonly FluxJITCompiler<TData, TOper, TDef>.CompiledFunc _jitFunc;
        private readonly bool _isJit;

        private FluxInjector<TData> _injector;

        internal FluxInstance(
            TDef provider,
            FluxFormula<TData, TOper> formula,
            FluxInjector<TData> injector,
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

        /// <summary>按位置注入（非安全：错位不报错，但结果错误）</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> SetIndex(int index, TData value)
        {
            _injector = _injector.SetIndex(index, value);
            return this;
        }

        /// <summary>按变量名安全注入。名称不存在则抛 ArgumentException。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TDef> Set(string name, TData value)
        {
            _injector = _injector.Set(name, value);
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
                // JIT 路径：将注入后的 Payload 传递给编译好的委托
                return _jitFunc(_injector.GetBuffer());
            }
            else
            {
                // 解释器路径：使用 injector 持有的缓冲副本（已通过 Set() 覆写数据）
                var kernel = new FluxEvaluator<TData, TOper, TDef>(_provider);
                return kernel.Compute(_injector.GetBuffer().AsSpan(0, _formula.Count));
            }
        }

        /// <summary>
        /// 获取注入后的指令缓冲（仅供调试/基准测试，正常使用请走 Run()）
        /// </summary>
        public readonly Instruction[] GetBuffer() => _injector.GetBuffer();
    }
}