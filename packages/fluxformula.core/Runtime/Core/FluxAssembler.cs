using System;
using System.Runtime.CompilerServices;
using FluxFormula.Compiler;

namespace FluxFormula.Core
{
    public readonly unsafe ref struct FluxAssembler<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
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
        public FluxFormula<TData, TDef> Compile(LexResult<TData> lexResult)
        {
            return Compile(lexResult.Tokens, lexResult.VarNames);
        }

        /// <summary>
        /// 将 Token 编译为可缓存的公式数据图纸
        /// </summary>
        public FluxFormula<TData, TDef> Compile(
            ReadOnlySpan<FluxToken<TData>> tokens,
            string[] varNames = null)
        {
            int dataSlots = FormulaFormat.DataSlots<TData>();
            var buffer = new Instruction[tokens.Length * (1 + dataSlots) + 1];

            VariableSlot[] varSlots = null;
            if (varNames != null)
                varSlots = new VariableSlot[varNames.Length];

            var compiler = new FluxCompiler<TData, TDef>(_definition);
            int count = compiler.Compile(tokens, buffer, out int immCount, out int varSlotCount, out byte maxRegister, varNames, varSlots);

            FluxType type = FluxType.Formula;
            if (tokens.Length > 0)
            {
                byte firstOper = tokens[0].Oper;
                OpType kind = _definition.GetKind(firstOper);

                if (kind == OpType.Instruction)
                {
                    byte resolved = _definition.ResolveToken(firstOper, TokenContext.OperandExpected);
                    if (resolved != 0)
                    {
                        firstOper = resolved;
                        kind      = _definition.GetKind(firstOper);
                    }
                }

                if (kind == OpType.Instruction)
                {
                    var pairInfo = _definition.GetPair(firstOper);

                    if (pairInfo.PairRole != Pair.Left)
                    {
                        int arity = _definition.GetArity(firstOper);
                        if (arity != 1 && arity < 3)
                            type = FluxType.Modifier;
                    }
                }
            }

            VariableSlot[] finalSlots = null;
            if (varSlotCount > 0)
            {
                finalSlots = new VariableSlot[varSlotCount];
                Array.Copy(varSlots, finalSlots, varSlotCount);
            }

            return new FluxFormula<TData, TDef>(buffer, count, type, immCount, finalSlots, maxRegister);
        }

        // ═══════════════════════════════════════════════════════
        // Instantiate：原子公式
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 将原子公式激活为流式流水线。
        /// 优先从 <see cref="FormulaCache"/> 获取字节码——命中时直接从 blob fixed 指针重建指令序列，
        /// 避免 ToBytes()/FromBytes() 的分配开销。未命中则回退到 formula.Raw()。
        /// </summary>
        public FluxInstance<TData, TDef> Instantiate(
            FluxFormula<TData, TDef> formula,
            bool jit = false
        )
        {
            if (jit && !FluxPlatform.IsJitDisabled)
            {
                // ── JIT 原子路径 ──
                var hash = formula.GetByteHash();
                var cache = FormulaCache.Instance;

                if (cache.TryGetDelegate(hash, out IntPtr cachedHandle))
                {
                    var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(cachedHandle);
                    var func = (FluxJITCompiler<TData, TDef>.CompiledFunc)handle.Target;
                    var cachedPayload = CreateJitPayload(formula);
                    var cachedInjector = new FluxInjector<TData>(cachedPayload, null, formula.VariableSlots);
                    return new FluxInstance<TData, TDef>(
                        _definition,
                        formula,
                        cachedInjector,
                        func,
                        true
                    );
                }

                try
                {
                    var instSpan = ResolveBytecodeSpan(hash, formula);
                    var func = FluxJITCompiler<TData, TDef>.Compile(
                        instSpan,
                        _definition,
                        out var payload,
                        maxRegister: formula.MaxRegister
                    );
                    var delegateHandle = System.Runtime.InteropServices.GCHandle.Alloc(func);
                    cache.PutDelegate(hash, System.Runtime.InteropServices.GCHandle.ToIntPtr(delegateHandle));
                    cache.PutBytes(hash, formula.ToBytes());

                    var injector = new FluxInjector<TData>(payload, null, formula.VariableSlots);
                    return new FluxInstance<TData, TDef>(
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
                    FluxPlatform.DisableJit(ex.Message);
                }
            }

            // ── 解释器路径 ──
            var inj = CreateInjector(formula);
            return new FluxInstance<TData, TDef>(
                _definition,
                formula,
                inj,
                null,
                false
            );
        }

        // ═══════════════════════════════════════════════════════
        // Instantiate：链式公式
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 将链式公式激活为流式流水线。
        /// 短链（≤ MergeThreshold）保留链式结构逐 link 求值；
        /// 长链自动合并为原子公式后实例化。
        /// </summary>
        public FluxInstance<TData, TDef> Instantiate(
            FluxChain<TData, TDef> chain,
            bool jit = false
        )
        {
            if (chain.Length == 0)
                return Instantiate(FluxFormula<TData, TDef>.Empty, jit);

            // Modifier 首链：首 link 适配为 Formula
            chain = AdaptModifierFirstLink(chain);

            if (jit && !FluxPlatform.IsJitDisabled)
                return InstantiateJitChain(chain);

            // ── 解释器路径 ──
            if (chain.Length > ChainReserved.MergeThreshold)
                return Instantiate(chain.ToAtomic(), jit: false);

            // 短链：保留链式结构，per-link 解释器求值
            var mergedFormula = chain.ToAtomic();
            var injector = CreateInjector(mergedFormula);
            return new FluxInstance<TData, TDef>(
                _definition, mergedFormula, injector, null, false, chain);
        }

        /// <summary>
        /// 将词法 Token 直接编译为可执行的流式流水线
        /// </summary>
        public FluxInstance<TData, TDef> Build(
            ReadOnlySpan<FluxToken<TData>> tokens,
            bool jit = false
        )
        {
            var formula = Compile(tokens);
            return Instantiate(formula, jit);
        }

        // ── 字节码缓存解析 ──

        /// <summary>
        /// 尝试从 <see cref="FormulaCache"/> 获取公式的缓存字节码，返回指令序列。
        /// 命中时零拷贝指向 blob fixed 内存；未命中则回退到 formula.Raw()。
        /// </summary>
        private static ReadOnlySpan<Instruction> ResolveBytecodeSpan(
            DualHash64 hash, FluxFormula<TData, TDef> formula)
        {
            if (FormulaCache.Instance.TryGet(hash, out IntPtr ptr, out int length))
            {
                unsafe
                {
                    var bytes = new ReadOnlySpan<byte>((void*)ptr, length);
                    return FormulaFormat.GetInstructionSpan(bytes);
                }
            }
            return formula.Raw();
        }

        /// <summary>
        /// 扫描 IL 指令缓冲，提取数据槽位并建立普通模式的 Injector
        /// </summary>
        private FluxInjector<TData> CreateInjector(FluxFormula<TData, TDef> formula)
        {
            var buffer = formula.Raw();
            int dataSlots = FormulaFormat.DataSlots<TData>();

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
        /// </summary>
        private Instruction[] CreateJitPayload(FluxFormula<TData, TDef> formula)
        {
            var raw = formula.Raw();
            int dataSlotsPerParam = FormulaFormat.DataSlots<TData>();
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

        // ── Per-link JIT 链式实例化 ──

        /// <summary>
        /// 为链式公式构建 per-link JIT delegate 数组。
        /// 非首个 link 若为 Modifier 则通过 ToFormula(CHAIN_LINK_INTERNAL_0) 适配为 Formula，
        /// 其第一 Immediate 数据槽位在求值时注入前一个 link 的输出。
        /// </summary>
        private FluxInstance<TData, TDef> InstantiateJitChain(
            FluxChain<TData, TDef> chain)
        {
            var links = chain.GetLinks();
            var funcs = new FluxJITCompiler<TData, TDef>.CompiledFunc[links.Length];
            var injectors = new FluxInjector<TData>[links.Length];
            var cache = FormulaCache.Instance;

            for (int i = 0; i < links.Length; i++)
            {
                var linkFormula = LinkToFormula(links[i], i > 0 || links[i].Type == FluxType.Modifier);
                var hash = linkFormula.GetByteHash();

                if (cache.TryGetDelegate(hash, out IntPtr cachedHandle))
                {
                    var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(cachedHandle);
                    funcs[i] = (FluxJITCompiler<TData, TDef>.CompiledFunc)handle.Target;
                    var payload = CreateJitPayload(linkFormula);
                    injectors[i] = new FluxInjector<TData>(payload, null, linkFormula.VariableSlots);
                }
                else
                {
                    try
                    {
                        var instSpan = ResolveBytecodeSpan(hash, linkFormula);
                        var func = FluxJITCompiler<TData, TDef>.Compile(
                            instSpan, _definition, out var payload,
                            maxRegister: linkFormula.MaxRegister);
                        var handle = System.Runtime.InteropServices.GCHandle.Alloc(func);
                        cache.PutDelegate(hash, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
                        cache.PutBytes(hash, linkFormula.ToBytes());
                        funcs[i] = func;
                        injectors[i] = new FluxInjector<TData>(payload, null, linkFormula.VariableSlots);
                    }
                    catch (Exception ex) when (
                        ex is PlatformNotSupportedException
                        || ex is NotSupportedException
                        || ex is InvalidOperationException)
                    {
                        FluxPlatform.DisableJit(ex.Message);
                        return Instantiate(chain.ToAtomic(), jit: false);
                    }
                }
            }

            // per-link injector 用于 Set/SetIndex 场景——基于合并后的原子公式
            var mergedFormula = chain.ToAtomic();
            var mergedInjector = CreateInjector(mergedFormula);

            return new FluxInstance<TData, TDef>(
                _definition, mergedFormula, mergedInjector, funcs, injectors, chain);
        }

        /// <summary>
        /// Modifier 首链适配：若首 link 的 Type 为 Modifier，将其提升为 Formula。
        /// 仅可能来自 VFF 反序列化场景。
        /// </summary>
        private static FluxChain<TData, TDef> AdaptModifierFirstLink(FluxChain<TData, TDef> chain)
        {
            var links = chain.GetLinks();
            if (links.Length == 0 || links[0].Type != FluxType.Modifier)
                return chain;

            var newLinks = new ChainLink[links.Length];
            for (int i = 0; i < links.Length; i++)
                newLinks[i] = links[i];

            var firstPromoted = LinkToFormula(links[0], adaptModifier: true);
            newLinks[0] = new ChainLink
            {
                Key              = links[0].Key,
                Bytecode         = firstPromoted.Raw().ToArray(),
                InstructionCount = firstPromoted.Count,
                Type             = FluxType.Formula,
                ImmediateCount   = firstPromoted.ImmediateCount,
                VarSlots         = firstPromoted.VariableSlots,
                MaxRegister      = firstPromoted.MaxRegister,
            };

            return new FluxChain<TData, TDef>(newLinks);
        }

        /// <summary>
        /// 从 ChainLink 重建 FluxFormula。
        /// 非首个 link 若为 Modifier 则调用 ToFormula(CHAIN_LINK_INTERNAL_0) 适配。
        /// </summary>
        private static FluxFormula<TData, TDef> LinkToFormula(ChainLink link, bool adaptModifier)
        {
            var f = new FluxFormula<TData, TDef>(
                link.Bytecode, link.InstructionCount,
                link.Type, link.ImmediateCount, link.VarSlots,
                link.MaxRegister);

            if (adaptModifier && f.Type == FluxType.Modifier)
                f = f.ToFormula(ChainReserved.InternalPrefix + "0");

            return f;
        }
    }
}
