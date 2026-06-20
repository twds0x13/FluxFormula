using System;
using System.Runtime.CompilerServices;
using FluxFormula.Compiler;

namespace FluxFormula.Core
{
    public readonly unsafe ref struct FluxAssembler<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        private readonly TDef _definition;

        /// <summary>
        /// 负责将词法 Token 编译为可执行的公式图纸 (FluxFormula)，
        /// </summary>
        /// <param name="definition"></param>
        public FluxAssembler(TDef definition)
        {
            _definition = definition;
        }

        /// <summary>
        /// 将 LexResult 直接编译为公式（自动提取变量名）
        /// </summary>
        public FluxFormula<TData, TOper> Compile(LexResult<TData, TOper> lexResult)
        {
            return Compile(lexResult.Tokens, lexResult.VarNames);
        }

        /// <summary>
        /// 将 Token 编译为可缓存的公式数据图纸
        /// </summary>
        public FluxFormula<TData, TOper> Compile(
            ReadOnlySpan<FluxToken<TData, TOper>> tokens,
            string[] varNames = null)
        {
            int dataSlots = (sizeof(TData) + 7) / 8;
            var buffer = new Instruction[tokens.Length * (1 + dataSlots) + 1];

            VariableSlot[] varSlots = null;
            if (varNames != null)
                varSlots = new VariableSlot[varNames.Length]; // 上界：每 Token 最多一变量

            var compiler = new FluxCompiler<TData, TOper, TDef>(_definition);
            int count = compiler.Compile(tokens, buffer, out int immCount, out int varSlotCount, varNames, varSlots);

            FluxType type = FluxType.Formula;
            if (tokens.Length > 0)
            {
                TOper firstOper = tokens[0].Oper;
                byte opByte = *(byte*)&firstOper;
                OpType kind = _definition.GetKind(opByte);

                // 上下文消歧：前缀位置的二元运算符可能有一元语义
                if (kind == OpType.Instruction)
                {
                    TOper resolved = _definition.ResolveToken(firstOper, TokenContext.OperandExpected);
                    if (*(byte*)&resolved != 0)
                    {
                        firstOper = resolved;
                        opByte    = *(byte*)&firstOper;
                        kind      = _definition.GetKind(opByte);
                    }
                }

                // 如果第一个 Token 是算术类型，但它是一个"左括号" (PairRole == Pair.Left)，
                // 或者是一元前缀运算符 (arity == 1)，它依然应被视为 Formula 的开始。
                if (kind == OpType.Instruction)
                {
                    var pairInfo = _definition.GetPair(firstOper);

                    if (pairInfo.PairRole != Pair.Left)
                    {
                        int arity = _definition.GetArity(opByte);
                        // arity == 1 → 一元前缀运算符 (如 -5, +3) 可以单独启动公式
                        if (arity != 1)
                            type = FluxType.Modifier;
                    }
                }
            }

            // 裁剪到实际变量槽位数（避免传空白尾元素）
            VariableSlot[] finalSlots = null;
            if (varSlotCount > 0)
            {
                finalSlots = new VariableSlot[varSlotCount];
                Array.Copy(varSlots, finalSlots, varSlotCount);
            }

            return new FluxFormula<TData, TOper>(buffer, count, type, immCount, finalSlots);
        }

        /// <summary>
        /// 将已有的图纸 (FluxFormula) 激活为流式流水线。
        /// 对链式公式自动转换为原子公式后再执行。
        /// </summary>
        public FluxInstance<TData, TOper, TDef> Instantiate(
            FluxFormula<TData, TOper> formula,
            bool jit = false
        )
        {
            if (jit && !FluxPlatform.IsJitDisabled)
            {
                // ── JIT 路径：链式公式需先转为原子公式 ──
                if (formula.IsChained)
                    formula = formula.ToAtomic();

                // ── 尝试委托缓存 ──
                var hash = formula.GetByteHash();
                var cache = ConnectCache.Cache;

                if (cache.TryGetDelegate(hash, out IntPtr cachedHandle))
                {
                    var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(cachedHandle);
                    var func = (FluxJITCompiler<TData, TOper, TDef>.CompiledFunc)handle.Target;
                    // 重建与 Compile 产出一致的紧凑数据 payload
                    var cachedPayload = CreateJitPayload(formula);
                    var cachedInjector = new FluxInjector<TData>(cachedPayload, null, formula.VariableSlots);
                    return new FluxInstance<TData, TOper, TDef>(
                        _definition,
                        formula,
                        cachedInjector,
                        func,
                        true
                    );
                }

                try
                {
                    var func = FluxJITCompiler<TData, TOper, TDef>.Compile(
                        formula.Raw(),
                        _definition,
                        out var payload
                    );
                    // ── 将编译产物存入委托缓存 ──
                    var delegateHandle = System.Runtime.InteropServices.GCHandle.Alloc(func);
                    cache.PutDelegate(hash, System.Runtime.InteropServices.GCHandle.ToIntPtr(delegateHandle));

                    var injector = new FluxInjector<TData>(payload, null, formula.VariableSlots);
                    return new FluxInstance<TData, TOper, TDef>(
                        _definition,
                        formula,
                        injector,
                        func,
                        true
                    );
                }
                catch (Exception ex) when (
                    ex is PlatformNotSupportedException
                    || ex is NotSupportedException
                    || ex is InvalidOperationException)
                {
                    // IL2CPP / AOT 平台不支持 Expression.Compile()
                    FluxPlatform.DisableJit();
                }
            }

            // ── 解释器路径 ──
            if (formula.IsChained)
            {
                // 长链（> MergeThreshold）：合并为原子公式，单次解释器求值
                // 短链：保留链式，Run() 中 per-link 求值（R1 串联）
                if (formula.ChainLength > ChainReserved.MergeThreshold)
                    formula = formula.ToAtomic();

                var mergedForInjector = formula.IsChained ? formula.ToAtomic() : formula;
                var injector = CreateInjector(mergedForInjector);
                return new FluxInstance<TData, TOper, TDef>(
                    _definition,
                    formula,       // 可能仍是链式（短链）或原子（长链）
                    injector,
                    null,
                    false
                );
            }
            else
            {
                var injector2 = CreateInjector(formula);
                return new FluxInstance<TData, TOper, TDef>(
                    _definition,
                    formula,
                    injector2,
                    null,
                    false
                );
            }
        }

        /// <summary>
        /// 将词法 Token 直接编译为可执行的流式流水线
        /// 例如：runner.Build(tokens, jit: true).InjectNext(10).Run();
        /// </summary>
        public FluxInstance<TData, TOper, TDef> Build(
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
        private FluxInjector<TData> CreateInjector(FluxFormula<TData, TOper> formula)
        {
            var buffer = formula.Raw();
            int dataSlots = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);

            // formula.ImmediateCount 已由编译器精确提供，无需额外 O(N) 计数
            int[] offsets = new int[formula.ImmediateCount];
            int idx = 0;
            for (int i = 0; i < formula.Count; )
            {
                if (_definition.GetKind(buffer[i].OpCode) == OpType.Immediate)
                {
                    offsets[idx++] = i + 1;
                    i += 1 + dataSlots;
                }
                else
                    i++;
            }

            return new FluxInjector<TData>(buffer.ToArray(), offsets, formula.VariableSlots);
        }

        /// <summary>
        /// 从公式重建紧凑数据 payload（与 FluxJITCompiler.Compile 产生的格式一致）。
        /// 用于 delegate 缓存命中时重建委托所需的 Instruction[] 数据缓冲区。
        /// </summary>
        private Instruction[] CreateJitPayload(FluxFormula<TData, TOper> formula)
        {
            var raw = formula.Raw();
            int dataSlotsPerParam = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            int totalDataSlots = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (_definition.GetKind(raw[i].OpCode) == OpType.Immediate)
                {
                    totalDataSlots += dataSlotsPerParam;
                    i += dataSlotsPerParam;
                }
            }
            var payload = new Instruction[totalDataSlots];
            int dst = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (_definition.GetKind(raw[i].OpCode) == OpType.Immediate)
                {
                    raw.Slice(i + 1, dataSlotsPerParam).CopyTo(payload.AsSpan(dst));
                    dst += dataSlotsPerParam;
                    i += dataSlotsPerParam;
                }
            }
            return payload;
        }
    }
}
