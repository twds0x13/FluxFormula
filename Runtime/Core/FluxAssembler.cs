using System;
using System.Runtime.CompilerServices;
using FluxFormula.Compiler;

namespace FluxFormula.Core
{
    public readonly unsafe ref struct FluxAssembler<TData, TOper, TProvider>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TProvider : struct, IFluxJITDefinition<TData, TOper>
    {
        private readonly TProvider _provider;
        private readonly ushort _regCount;

        public FluxAssembler(TProvider provider, ushort regCount = 256)
        {
            _provider = provider;
            _regCount = regCount;
        }

        /// <summary>
        /// 将 Token 编译为可缓存的公式数据图纸
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxFormula<TData, TOper> Compile(ReadOnlySpan<FluxToken<TData, TOper>> tokens)
        {
            int dataSlots = (sizeof(TData) + 7) / 8;
            var buffer = new Instruction[tokens.Length * (1 + dataSlots) + 1];

            var compiler = new FluxCompiler<TData, TOper, TProvider>(_provider);
            int count = compiler.Compile(tokens, buffer);

            FluxType type = FluxType.Formula;
            if (tokens.Length > 0)
            {
                TOper firstOper = tokens[0].Oper;
                byte opByte = *(byte*)&firstOper;
                OpType kind = _provider.GetKind(opByte);

                // 如果第一个 Token 是算术类型，但它是一个“左括号” (PairRole == Pair.Left)，
                // 它依然应该被视为 Source 公式的开始。
                if (kind == OpType.Instruction)
                {
                    var pairInfo = _provider.GetPair(firstOper);
                    if (pairInfo.PairRole != Pair.Left)
                    {
                        type = FluxType.Modifier;
                    }
                }
            }

            return new FluxFormula<TData, TOper>(buffer, count, type);
        }

        /// <summary>
        /// 将已有的图纸 (FluxFormula) 激活为流式流水线
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TProvider> Instantiate(
            FluxFormula<TData, TOper> formula,
            bool jit = false
        )
        {
            if (jit)
            {
                var func = FluxJITCompiler<TData, TOper, TProvider>.Compile(
                    formula.GetInstructions(),
                    _provider,
                    out var payload
                );
                var injector = new FluxBinder<TData>(payload);
                return new FluxInstance<TData, TOper, TProvider>(
                    _provider,
                    formula,
                    injector,
                    func,
                    true
                );
            }
            else
            {
                var injector = CreateInjector(formula);
                return new FluxInstance<TData, TOper, TProvider>(
                    _provider,
                    formula,
                    injector,
                    null,
                    false
                );
            }
        }

        /// <summary>
        /// 将词法 Token 直接编译为可执行的流式流水线
        /// 例如：runner.Build(tokens, jit: true).InjectNext(10).Run();
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxInstance<TData, TOper, TProvider> Build(
            ReadOnlySpan<FluxToken<TData, TOper>> tokens,
            bool jit = false
        )
        {
            var formula = Compile(tokens);
            return Instantiate(formula, jit);
        }

        /// <summary>
        /// 扫描 IL 指令缓冲，提取数据槽位并建立普通模式的 Injector
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FluxBinder<TData> CreateInjector(FluxFormula<TData, TOper> formula)
        {
            var buffer = formula.Buffer;
            int dataSlots = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);

            int count = 0;
            for (int i = 0; i < formula.ValidCount; )
            {
                if (_provider.GetKind(buffer[i].OpCode) == OpType.Immediate)
                {
                    count++;
                    i += 1 + dataSlots;
                }
                else
                    i++;
            }

            int[] offsets = new int[count];
            int idx = 0;
            for (int i = 0; i < formula.ValidCount; )
            {
                if (_provider.GetKind(buffer[i].OpCode) == OpType.Immediate)
                {
                    offsets[idx++] = i + 1;
                    i += 1 + dataSlots;
                }
                else
                    i++;
            }

            return new FluxBinder<TData>(buffer, offsets);
        }
    }
}
