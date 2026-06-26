using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    // ═══════════════════════════════════════════════════════
    // 链式求值内部变量名——用户不应使用此前缀
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 链式 JIT 求值使用的内部变量名前缀。
    /// 用户不得声明以此前缀开头的变量名，否则链式求值会发生变量冲突。
    /// </summary>
    public static class ChainReserved
    {
        /// <summary>内部变量前缀</summary>
        public const string InternalPrefix = "CHAIN_LINK_INTERNAL_";

        /// <summary>
        /// 链式公式在<b>解释器路径</b>下触发合并的阈值（从 <see cref="FluxConfig"/> 读取）。
        /// JIT 路径始终逐 link 求值，不受此阈值影响——详见
        /// <see cref="FluxAssembler{TData, TDef}.Instantiate(FluxChain{TData, TDef}, bool)"/>。
        /// </summary>
        public static int MergeThreshold => FluxConfig.Current.MergeThreshold;
    }

    /// <summary>变量（未知数）槽位：名称 → Immediate 序号</summary>
    public readonly struct VariableSlot
    {
        public readonly string Name;
        /// <summary>该变量是第几个 Immediate（0-based），解释器/JIT 两路径通用</summary>
        public readonly int SlotIndex;

        public VariableSlot(string name, int slotIndex)
        {
            Name      = name;
            SlotIndex = slotIndex;
        }

        public override readonly string ToString() => $"{Name}→slot[{SlotIndex}]";
    }

    /// <summary>
    /// 链式公式的一个环节。存储该公式片段的字节码引用和元数据，
    /// 通过 DualHash64.Key 从缓存中检索 JIT delegate。
    /// </summary>
    public struct ChainLink
    {
        /// <summary>字节码哈希——在缓存中查找 delegate 的键</summary>
        public DualHash64 Key;

        /// <summary>字节码引用（指向原始公式的 Instruction[]，不复制）</summary>
        public Instruction[] Bytecode;

        /// <summary>Instruction 数量</summary>
        public int InstructionCount;

        /// <summary>Formula 或 Modifier</summary>
        internal FluxType Type;

        /// <summary>该片段的 Immediate 数（用于 SetIndex 偏移计算）</summary>
        public int ImmediateCount;

        /// <summary>该片段的变量槽</summary>
        public VariableSlot[] VarSlots;

        /// <summary>该片段的最大寄存器索引（0=未分析）</summary>
        public byte MaxRegister;
    }

    public readonly struct FluxFormula<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        // ── 原子公式表示（Instruction[]）──
        private readonly Instruction[] _buffer;
        public readonly int Count;
        internal readonly FluxType Type;
        public readonly int ImmediateCount;
        public readonly VariableSlot[] VariableSlots;

        /// <summary>
        /// 该公式使用的最大寄存器索引（0=未分析，回退到全量分配）。
        /// </summary>
        public readonly byte MaxRegister;

        internal FluxFormula(Instruction[] buffer, int count, FluxType type,
            int immediateCount = 0, VariableSlot[] varSlots = null, byte maxRegister = 0)
        {
            _buffer        = buffer;
            Count          = count;
            Type           = type;
            ImmediateCount = immediateCount;
            VariableSlots  = varSlots ?? Array.Empty<VariableSlot>();
            MaxRegister    = maxRegister;
        }

        /// <summary>空公式（Count=0），主要用于 Connect 边界场景</summary>
        public static FluxFormula<TData, TDef> Empty =>
            new(Array.Empty<Instruction>(), 0, FluxType.Formula);

        /// <summary>空 Modifier（Count=0），供 <see cref="FluxModifier{TData, TDef}.Empty"/> 使用</summary>
        internal static FluxFormula<TData, TDef> EmptyModifier =>
            new(Array.Empty<Instruction>(), 0, FluxType.Modifier);

        /// <summary>从原子公式创建单个 <see cref="ChainLink"/>，供 Connect 路径使用。</summary>
        internal ChainLink ToLink()
        {
            return new ChainLink
            {
                Key              = GetByteHash(),
                Bytecode         = _buffer,
                InstructionCount = Count,
                Type             = Type,
                ImmediateCount   = ImmediateCount,
                VarSlots         = VariableSlots,
                MaxRegister      = MaxRegister,
            };
        }

        // ── Formula ↔ Modifier 互转 ──

        /// <summary>
        /// 转换为 Modifier：移除第一个数据操作数，后续指令中对 dest 寄存器的引用全部重命名为 R1。
        /// 已为 Modifier（内部 Type==Modifier）则直接包装。
        /// </summary>
        public FluxModifier<TData, TDef> ToModifier()
        {
            if (Type == FluxType.Modifier) return new FluxModifier<TData, TDef>(this);

            if (Count < 2)
                throw new InvalidOperationException("Cannot convert formula with fewer than 2 instructions to Modifier.");

            // 第一个指令必须是 Immediate（加载第一操作数）
            byte destReg = _buffer[0].Dest;
            int dataSlots = FormulaFormat.DataSlots<TData>();

            // 新指令数 = 原指令数 - 1(Immediate) - dataSlots(Immediate 数据)
            int newCount = Count - 1 - dataSlots;
            var newBuffer = new Instruction[newCount];

            // 复制剩余指令（跳过第一个 Immediate 及其数据槽位）
            Array.Copy(_buffer, 1 + dataSlots, newBuffer, 0, newCount);

            // 寄存器重命名：destReg → Bus (R1)
            if (destReg != Registers.Bus)
            {
                for (int i = 0; i < newCount; i++)
                {
                    ref var inst = ref newBuffer[i];
                    if (inst.Dest == destReg) inst.Dest = Registers.Bus;
                    if (inst.Arg0 == destReg) inst.Arg0 = Registers.Bus;
                    if (inst.Arg1 == destReg) inst.Arg1 = Registers.Bus;
                    if (inst.Arg2 == destReg) inst.Arg2 = Registers.Bus;
                    if (inst.Arg3 == destReg) inst.Arg3 = Registers.Bus;
                    if (inst.Arg4 == destReg) inst.Arg4 = Registers.Bus;
                    if (inst.Arg5 == destReg) inst.Arg5 = Registers.Bus;
                }
            }

            // 调整变量槽：移除第一个变量（如果有）
            int newImmCount = ImmediateCount - 1;
            VariableSlot[] newSlots;
            if (VariableSlots.Length > 0 && VariableSlots[0].SlotIndex == 0)
            {
                // 第一个变量槽属于被移除的操作数——移除它，其余 SlotIndex 减 1
                newSlots = new VariableSlot[VariableSlots.Length - 1];
                for (int i = 1; i < VariableSlots.Length; i++)
                    newSlots[i - 1] = new VariableSlot(
                        VariableSlots[i].Name,
                        VariableSlots[i].SlotIndex - 1);
            }
            else
            {
                newSlots = VariableSlots;
            }

            return new FluxModifier<TData, TDef>(
                new FluxFormula<TData, TDef>(newBuffer, newCount, FluxType.Modifier,
                    newImmCount, newSlots, MaxRegister));
        }

        /// <summary>
        /// [Obsolete] 旧名称，请使用 <see cref="ToModifier"/>。
        /// </summary>
        [Obsolete("Use ToModifier() instead.")]
        public FluxModifier<TData, TDef> ToMultiplier() => ToModifier();

        /// <summary>
        /// Modifier→Formula：插入命名变量替代 R1 输入。
        /// 已为 Formula 则原样返回。
        /// </summary>
        public FluxFormula<TData, TDef> ToFormula(string varName)
        {
            if (Type == FluxType.Formula) return this;

            int dataSlots = FormulaFormat.DataSlots<TData>();

            // 新指令数 = 原指令数 + 1(Immediate) + dataSlots(Immediate 数据)
            int newCount = Count + 1 + dataSlots;
            var newBuffer = new Instruction[newCount];

            // 插入 new Immediate 指令（dest = R2 或第一个暂时寄存器）
            // 选择寄存器：找个未被原指令占用的
            byte newDest = FindFreeRegister(_buffer.AsSpan(0, Count), dataSlots);
            newBuffer[0] = new Instruction { Dest = newDest };

            // Immediate 数据槽位已保留（newBuffer[1..1+dataSlots]），设为 0

            // 复制原指令（偏移 1+dataSlots）
            Array.Copy(_buffer, 0, newBuffer, 1 + dataSlots, Count);

            // 重命名 Bus(R1) → newDest 在后续指令中
            for (int i = 1 + dataSlots; i < newCount; i++)
            {
                ref var inst = ref newBuffer[i];
                if (inst.Dest == Registers.Bus) inst.Dest = newDest;
                if (inst.Arg0 == Registers.Bus) inst.Arg0 = newDest;
                if (inst.Arg1 == Registers.Bus) inst.Arg1 = newDest;
                if (inst.Arg2 == Registers.Bus) inst.Arg2 = newDest;
                if (inst.Arg3 == Registers.Bus) inst.Arg3 = newDest;
                if (inst.Arg4 == Registers.Bus) inst.Arg4 = newDest;
                if (inst.Arg5 == Registers.Bus) inst.Arg5 = newDest;
            }

            // 变量槽：新变量 SlotIndex=0，旧变量 SlotIndex+1
            var newSlots = new VariableSlot[VariableSlots.Length + 1];
            newSlots[0] = new VariableSlot(varName, 0);
            for (int i = 0; i < VariableSlots.Length; i++)
                newSlots[i + 1] = new VariableSlot(
                    VariableSlots[i].Name,
                    VariableSlots[i].SlotIndex + 1);

            return new FluxFormula<TData, TDef>(newBuffer, newCount, FluxType.Formula,
                ImmediateCount + 1, newSlots, MaxRegister);
        }

        /// <summary>查找一条 bytecode 中未被占用的最低寄存器号（Error/Bus 保留）</summary>
        private static byte FindFreeRegister(ReadOnlySpan<Instruction> program, int dataSlots)
        {
            Span<bool> used = stackalloc bool[Registers.Max + 1];
            for (int i = 0; i < program.Length; i++)
            {
                var inst = program[i];
                used[inst.Dest] = true;
                used[inst.Arg0] = true;
                used[inst.Arg1] = true;
                used[inst.Arg2] = true;
                used[inst.Arg3] = true;
                used[inst.Arg4] = true;
                used[inst.Arg5] = true;
            }
            for (byte r = Registers.FirstAlloc; r < Registers.Max; r++)
                if (!used[r]) return r;
            return (byte)(Registers.Max - 1);
        }

        /// <summary>
        /// 将当前公式与一个 Modifier 串联，返回 <see cref="FluxChain{TData, TDef}"/>。
        /// 前者的 R1 输出自动流入后者的首操作数位置。
        /// 类型系统保证 <paramref name="next"/> 是 Modifier（缺首操作数），
        /// 避免 Formula 的首操作数被静默覆盖。
        /// </summary>
        public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
        {
            if (Count == 0)
                return next.Count == 0
                    ? FluxChain<TData, TDef>.Empty
                    : new FluxChain<TData, TDef>(new[] { next.Inner.ToLink() });
            if (next.Count == 0)
                return new FluxChain<TData, TDef>(new[] { ToLink() });
            if (Count == 1)
                return new FluxChain<TData, TDef>(new[] { next.ToFormula(ChainReserved.InternalPrefix + "single").ToLink() });

            return new FluxChain<TData, TDef>(
                FluxChain<TData, TDef>.ChainConnect(new[] { ToLink() }, new[] { next.Inner.ToLink() }));
        }

        /// <summary>
        /// 返回公式的底层指令跨度。始终为 O(1)，零分配。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Instruction> Raw()
        {
            return _buffer.AsSpan(0, Count);
        }

        // ================================================================
        // 哈希——公式字节码的内容寻址键
        // ================================================================

        /// <summary>
        /// 计算公式的 DualHash64 标识。等效于 ToBytes() 的哈希。
        /// </summary>
        /// <remarks>
        /// 对 build-time 公式，此哈希存储在 Source Generator 生成的偏移表中。
        /// 对运行期 Connect 产物，用作 CompileCache/DelegateCache 的 key。
        /// 链式公式的哈希应通过 <see cref="FluxChain{TData, TDef}.GetByteHash"/> 获取。
        /// </remarks>
        public readonly DualHash64 GetByteHash()
        {
            var bytes = ToBytes();
            return DualHash64.Compute(bytes);
        }

        // ================================================================
        // 零序列化持久化：字节码即文件格式
        // ================================================================

        /// <summary>
        /// 将公式序列化为字节数组。可直接写入磁盘，无需 JSON/XML。
        /// 格式定义见 <see cref="FormulaFormat"/>。
        /// </summary>
        public readonly byte[] ToBytes()
        {

            int varSlotCount = VariableSlots.Length;
            int instByteLen = Count * FormulaFormat.InstructionSize;

            // 预编码变量名
            var enc = System.Text.Encoding.UTF8;
            var nameBytesList = new byte[varSlotCount][];
            int nameBytesTotal = 0;
            for (int i = 0; i < varSlotCount; i++)
            {
                nameBytesList[i] = enc.GetBytes(VariableSlots[i].Name);
                nameBytesTotal += nameBytesList[i].Length;
            }

            // 每个变量槽：NameLen(4) + NameBytes + SlotIndex(4) = 8 + nameLen
            int slotSectionSize = varSlotCount * 8 + nameBytesTotal;
            int totalSize = FormulaFormat.HeaderSize + instByteLen + slotSectionSize + FormulaFormat.FingerprintSize;

            byte[] data = new byte[totalSize];
            int offset = 0;

            // ── 头部 ──
            FormulaFormat.WriteHeader(data, ref offset,
                new FormulaHeader(Count, (byte)Type, ImmediateCount, varSlotCount, MaxRegister));

            // ── 指令 ──
            for (int i = 0; i < Count; i++)
            {
                BinaryFormat.WriteInt64LE(data, ref offset, _buffer[i].Raw);
            }

            // ── 变量槽 ──
            for (int i = 0; i < varSlotCount; i++)
            {
                byte[] nb = nameBytesList[i];
                BinaryFormat.WriteInt32LE(data, ref offset, nb.Length);
                Buffer.BlockCopy(nb, 0, data, offset, nb.Length); offset += nb.Length;
                BinaryFormat.WriteInt32LE(data, ref offset, VariableSlots[i].SlotIndex);
            }

            // ── 类型指纹（V2 尾部 8 字节）──
            FormulaFormat.WriteTypeFingerprint<TData, TDef>(data, ref offset);

            return data;
        }

        /// <summary>
        /// 从字节数组反序列化公式。与 <see cref="ToBytes"/> 配对使用。
        /// TData/TDef 类型参数由调用方提供（字节格式不携带泛型类型信息）。
        /// </summary>
        public static FluxFormula<TData, TDef> FromBytes(byte[] data)
        {
            return FromBytes(data.AsSpan());
        }

        /// <summary>
        /// 从只读字节跨度反序列化公式。
        /// 与 <see cref="FromBytes(byte[])"/> 相同逻辑，但接受 <see cref="ReadOnlySpan{T}"/>
        /// ——避免从 native 指针重建时需要临时 byte[] 分配。
        /// </summary>
        public static FluxFormula<TData, TDef> FromBytes(ReadOnlySpan<byte> data)
        {
            int offset = 0;

            // ── 头部 ──
            var header = FormulaFormat.ReadHeader(data);
            offset += FormulaFormat.HeaderSize;

            if (header.Count < 0 || header.ImmediateCount < 0 || header.VarSlotCount < 0)
                throw new ArgumentException("Corrupted formula data: negative count fields.");

            // ── 指令 ──
            var instructions = new Instruction[header.Count];
            for (int i = 0; i < header.Count; i++)
            {
                instructions[i] = new Instruction { Raw = BinaryFormat.ReadInt64LE(data, ref offset) };
            }

            // ── 变量槽 ──
            var varSlots = header.VarSlotCount > 0 ? new VariableSlot[header.VarSlotCount] : Array.Empty<VariableSlot>();
            var enc = System.Text.Encoding.UTF8;
            for (int i = 0; i < header.VarSlotCount; i++)
            {
                int nameLen = BinaryFormat.ReadInt32LE(data, ref offset);
                string name;
                unsafe { fixed (byte* p = data) { name = enc.GetString(p + offset, nameLen); } }
                offset += nameLen;
                int slotIdx = BinaryFormat.ReadInt32LE(data, ref offset);
                varSlots[i] = new VariableSlot(name, slotIdx);
            }

            // ── 类型指纹校验 ──
            if (!FormulaFormat.ValidateTypeFingerprint<TData, TDef>(data, offset))
                throw new InvalidOperationException(
                    $"Type fingerprint mismatch: the .ff bytecode was compiled for a different TDef (expected {typeof(TDef).FullName}). " +
                    "Cross-definition bytecode injection blocked.");

            return new FluxFormula<TData, TDef>(instructions, header.Count, (FluxType)header.Type, header.ImmediateCount, varSlots, header.MaxRegister);
        }

        public override readonly string ToString() =>
            $"FluxFormula<{typeof(TData).Name}, {typeof(TDef).Name}> [Type: {Type}, Instructions: {Count}]";
    }
}
