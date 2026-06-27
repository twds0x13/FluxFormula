using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 链式公式：由多次 <see cref="Connect"/> 串联而成的多段字节码序列。
    /// 需通过 <see cref="FluxAssembler{TData, TDef}.Instantiate(FluxChain{TData, TDef}, bool)"/>
    /// 实例化后求值，或显式调用 <see cref="ToAtomic"/> 合并为 <see cref="FluxFormula{TData, TDef}"/>。
    /// </summary>
    public readonly struct FluxChain<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        private readonly ChainLink[] _links;

        internal FluxChain(ChainLink[] links)
        {
            _links = links ?? Array.Empty<ChainLink>();
        }

        /// <summary>空链（Length=0），Connect 的单位元</summary>
        public static FluxChain<TData, TDef> Empty => new(Array.Empty<ChainLink>());

        // null 守卫：default(FluxChain) 绕过构造器，_links 可能为 null
        private ChainLink[] Links => _links ?? Array.Empty<ChainLink>();

        /// <summary>链中的链接数</summary>
        public int Length => Links.Length;

        /// <summary>获取链式链接的只读视图</summary>
        public ReadOnlySpan<ChainLink> GetLinks() => Links.AsSpan();

        // ── Connect ──

        /// <summary>
        /// 在当前链末尾追加一个 <see cref="FluxModifier{TData, TDef}"/>。
        /// </summary>
        public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
        {
            if (next.Count == 0) return this;
            if (Links.Length == 0)
                return new FluxChain<TData, TDef>(new[] { next.Inner.ToLink() });

            return new FluxChain<TData, TDef>(
                ChainConnect(_links, new[] { next.Inner.ToLink() }));
        }

        // ── 链式 → 原子 ──

        /// <summary>
        /// 将链式公式合并为单个原子 <see cref="FluxFormula{TData, TDef}"/>。
        /// 所有 link 的 Instruction[] 原样拼接，中间 Return 由解释器处理为 R1 总线传递。
        /// </summary>
        public FluxFormula<TData, TDef> ToAtomic()
        {
            if (Links.Length == 0)
                return FluxFormula<TData, TDef>.Empty;

            if (Links.Length == 1)
            {
                var link = Links[0];
                return new FluxFormula<TData, TDef>(
                    link.Bytecode, link.InstructionCount,
                    link.Type, link.ImmediateCount, link.VarSlots,
                    link.MaxRegister);
            }

            // 完整拼接：不丢弃任何指令
            int totalCount = 0;
            for (int i = 0; i < Links.Length; i++)
                totalCount += Links[i].InstructionCount;

            var buffer = new Instruction[totalCount];
            int dst = 0;
            for (int i = 0; i < Links.Length; i++)
            {
                Array.Copy(Links[i].Bytecode, 0, buffer, dst, Links[i].InstructionCount);
                dst += Links[i].InstructionCount;
            }

            int totalSlots = 0;
            foreach (var ls in Links) totalSlots += ls.VarSlots.Length;
            var slots = new VariableSlot[totalSlots];
            int sIdx = 0;
            foreach (var ls in Links)
                foreach (var vs in ls.VarSlots)
                    slots[sIdx++] = vs;

            int totalImm = 0;
            byte chainMaxReg = Registers.Bus;
            foreach (var ls in Links)
            {
                totalImm += ls.ImmediateCount;
                if (ls.MaxRegister > chainMaxReg) chainMaxReg = ls.MaxRegister;
            }

            return new FluxFormula<TData, TDef>(buffer, totalCount, Links[0].Type,
                totalImm, slots, chainMaxReg);
        }

        // ── 哈希 ──

        /// <summary>
        /// 计算链式公式的 DualHash64 标识。空链返回 default。
        /// 顺序 Combine 各 link 的 Key，与 <see cref="FluxFormula{TData, TDef}.GetByteHash"/> 互斥。
        /// </summary>
        public DualHash64 GetByteHash()
        {
            if (Links.Length == 0) return default;
            var h = Links[0].Key;
            for (int i = 1; i < Links.Length; i++)
                h = DualHash64.Combine(h, Links[i].Key);
            return h;
        }

        // ── 内部辅助 ──

        /// <summary>
        /// 合并两段链接数组，右移后半段 VariableSlot 的 SlotIndex。
        /// 暴露为 internal static 供 <see cref="FluxFormula{TData, TDef}"/> 和
        /// <see cref="FluxModifier{TData, TDef}"/> 的 Connect 使用。
        /// </summary>
        internal static ChainLink[] ChainConnect(ChainLink[] a, ChainLink[] b)
        {
            int totalLinks = a.Length + b.Length;
            var chain = new ChainLink[totalLinks];
            Array.Copy(a, 0, chain, 0, a.Length);

            int prevImmediateCount = 0;
            for (int i = 0; i < a.Length; i++)
                prevImmediateCount += a[i].ImmediateCount;

            for (int i = 0; i < b.Length; i++)
            {
                var src = b[i];
                var shiftedSlots = new VariableSlot[src.VarSlots.Length];
                for (int j = 0; j < src.VarSlots.Length; j++)
                    shiftedSlots[j] = new VariableSlot(
                        src.VarSlots[j].Name,
                        src.VarSlots[j].SlotIndex + prevImmediateCount);
                chain[a.Length + i] = new ChainLink
                {
                    Key              = src.Key,
                    Bytecode         = src.Bytecode,
                    InstructionCount = src.InstructionCount,
                    Type             = src.Type,
                    ImmediateCount   = src.ImmediateCount,
                    VarSlots         = shiftedSlots,
                    MaxRegister      = src.MaxRegister,
                };
            }

            return chain;
        }
    }
}
