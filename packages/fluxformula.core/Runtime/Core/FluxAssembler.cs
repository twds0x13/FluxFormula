using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FluxFormula.Compiler;

namespace FluxFormula.Core
{
    public readonly unsafe ref struct FluxAssembler<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
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
        internal FluxFormula<TData, TDef> Compile(
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
        /// 优先从 <see cref="FormulaCache"/> 获取字节码：命中时直接从 blob fixed 指针重建指令序列，
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
                if (TryResolveJitDelegate(formula, out var func, out var payload))
                {
                    var injector = new FluxInjector<TData>(payload, null, formula.VariableSlots);
                    return new FluxInstance<TData, TDef>(
                        _definition, formula, injector, func, true);
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
        /// </summary>
        /// <remarks>
        /// <para><b>合并策略（解释器 vs JIT 不对称）：</b></para>
        /// <para><b>解释器路径</b>（<c>jit: false</c>）：短链（≤ <see cref="ChainReserved.MergeThreshold"/>，默认 8）
        /// 保留链式结构，逐 link 通过 R1 总线求值；长链自动调用 <see cref="FluxChain{TData, TDef}.ToAtomic"/>
        /// 合并为单条 bytecode 后求值。原因是解释器 per-link 的 <c>BuildLinkBuffer</c> 每个 link 分配一次
        /// <c>Instruction[]</c>，长链分配开销不可忽略。</para>
        /// <para><b>JIT 路径</b>（<c>jit: true</c>）：始终逐 link 编译，不检查 <c>MergeThreshold</c>。
        /// 每个 link 的 delegate 独立编译并缓存在 <see cref="FormulaCache"/> 中，热路径零分配：
        /// 仅 <c>SetIndex(0, prevResult)</c> 写入 + 一次函数指针调用。合并为原子反而失去 link 级缓存复用
        /// （LEGO 模型：同一 Modifier 跨不同链的 delegate 可共享），且合并后的大公式作为唯一组合需要重新编译。</para>
        /// </remarks>
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
        internal FluxInstance<TData, TDef> Build(
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
        /// 从公式重建紧凑数据 payload（与 FluxExprCompiler.Compile 产生的格式一致）。
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

        // ── 委托编译提供方（编译器选择）──

        /// <summary>
        /// 按优先级尝试所有可用的委托提供方，返回 <see cref="CompiledFunc{TData}"/>。
        /// 不碰 <see cref="FormulaCache"/>——缓存逻辑由 <see cref="TryResolveJitDelegate"/> 负责。
        /// </summary>
        [ExcludeFromCodeCoverage]  // DynamicMethod.CreateDelegate 与覆盖率插桩不兼容
        private static CompiledFunc<TData> CompileDelegate(
            ReadOnlySpan<Instruction> instSpan,
            TDef definition,
            out Instruction[] payload,
            byte maxRegister)
        {
            // 1. IL 发射（仅 Mono / CoreCLR——IL2CPP 不支持 DynamicMethod）
            if (FluxPlatform.IsIlSupported)
            {
                try
                {
                    return FluxILCompiler<TData, TDef>.Compile(
                        instSpan, definition, out payload, maxRegister: maxRegister);
                }
                catch (PlatformNotSupportedException) { /* 降级到 Expression 树 */ }
            }

            // 2. Expression 树（已有，IL2CPP 回退）
            return FluxExprCompiler<TData, TDef>.Compile(
                instSpan, definition, out payload, maxRegister: maxRegister);
        }

        // ── JIT delegate 解析（原子 + 链式复用）──

        /// <summary>
        /// 尝试从 <see cref="FormulaCache"/> 解析 JIT delegate，未命中则通过
        /// <see cref="CompileDelegate"/> 编译并缓存。
        /// </summary>
        /// <returns>true 表示成功获取 delegate 和 payload；false 表示所有编译路径均失败（已调用 <see cref="FluxPlatform.DisableJit"/>），调用方应降级到解释器路径。</returns>
        private bool TryResolveJitDelegate(
            FluxFormula<TData, TDef> formula,
            out CompiledFunc<TData> func,
            out Instruction[] payload)
        {
            var hash = formula.GetByteHash();
            var cache = FormulaCache.Instance;

            if (cache.TryGetDelegate(hash, out IntPtr cachedHandle))
            {
                var handle = System.Runtime.InteropServices.GCHandle.FromIntPtr(cachedHandle);
                func = (CompiledFunc<TData>)handle.Target;
                payload = CreateJitPayload(formula);
                return true;
            }

            try
            {
                var instSpan = ResolveBytecodeSpan(hash, formula);
                func = CompileDelegate(instSpan, _definition, out payload,
                    maxRegister: formula.MaxRegister);
                var delegateHandle = System.Runtime.InteropServices.GCHandle.Alloc(func);
                cache.PutDelegate(hash, System.Runtime.InteropServices.GCHandle.ToIntPtr(delegateHandle));
                cache.PutBytes(hash, formula.ToBytes());
                return true;
            }
            catch (Exception ex) when (
                ex is PlatformNotSupportedException
                || ex is NotSupportedException
                || ex is InvalidOperationException)
            {
                FluxPlatform.DisableJit(ex.Message);
                func = null;
                payload = null;
                return false;
            }
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
            var funcs = new CompiledFunc<TData>[links.Length];
            var injectors = new FluxInjector<TData>[links.Length];

            for (int i = 0; i < links.Length; i++)
            {
                var linkFormula = LinkToFormula(links[i], i > 0 || links[i].Type == FluxType.Modifier);

                if (!TryResolveJitDelegate(linkFormula, out var func, out var payload))
                    return Instantiate(chain.ToAtomic(), jit: false);

                funcs[i] = func;
                injectors[i] = new FluxInjector<TData>(payload, null, linkFormula.VariableSlots);
            }

            // per-link injector 用于 Set/SetIndex 场景：基于合并后的原子公式
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
