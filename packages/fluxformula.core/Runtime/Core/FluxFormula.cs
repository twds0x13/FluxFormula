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

        /// <summary>链式求值中多少 link 后触发合并为原子公式的阈值</summary>
        public const int MergeThreshold = 8;
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
    internal struct ChainLink
    {
        /// <summary>字节码哈希——在缓存中查找 delegate 的键</summary>
        internal DualHash64 Key;

        /// <summary>字节码引用（指向原始公式的 Instruction[]，不复制）</summary>
        internal Instruction[] Bytecode;

        /// <summary>Instruction 数量</summary>
        internal int InstructionCount;

        /// <summary>Formula 或 Modifier</summary>
        internal FluxType Type;

        /// <summary>该片段的 Immediate 数（用于 SetIndex 偏移计算）</summary>
        internal int ImmediateCount;

        /// <summary>该片段的变量槽</summary>
        internal VariableSlot[] VarSlots;
    }

    public readonly struct FluxFormula<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        // ── 原子公式表示（Instruction[]）──
        private readonly Instruction[] _buffer;
        public readonly int Count;
        public readonly FluxType Type;
        public readonly int ImmediateCount;
        public readonly VariableSlot[] VariableSlots;

        // ── 链式表示（ChainLink[]）──
        private readonly ChainLink[] _chain;

        /// <summary>
        /// 类型初始化时校验 TOper 底层必须为 byte，防止 *(byte*)&oper 截断。
        /// </summary>
        static FluxFormula()
        {
            unsafe
            {
                if (sizeof(TOper) != 1)
                    throw new TypeInitializationException(
                        typeof(FluxFormula<TData, TOper>).FullName,
                        new NotSupportedException(
                            $"FluxFormula 要求 TOper 底层类型为 byte。当前: {typeof(TOper).Name} (sizeof={sizeof(TOper)})。请使用 `enum {typeof(TOper).Name} : byte`。"
                        )
                    );
            }
        }

        internal FluxFormula(Instruction[] buffer, int count, FluxType type, int immediateCount = 0, VariableSlot[] varSlots = null)
        {
            _buffer        = buffer;
            _chain         = null;
            Count          = count;
            Type           = type;
            ImmediateCount = immediateCount;
            VariableSlots  = varSlots ?? Array.Empty<VariableSlot>();
        }

        internal FluxFormula(ChainLink[] chain, FluxType type, int immediateCount, VariableSlot[] varSlots)
        {
            _chain         = chain;
            _buffer        = Array.Empty<Instruction>(); // chain 公式无合并 buffer
            Count          = chain.Length > 0 ? chain[chain.Length - 1].InstructionCount : 0; // approximative
            Type           = type;
            ImmediateCount = immediateCount;
            VariableSlots  = varSlots ?? Array.Empty<VariableSlot>();
        }

        /// <summary>空公式（Count=0），主要用于 Connect 边界场景</summary>
        public static FluxFormula<TData, TOper> Empty =>
            new(Array.Empty<Instruction>(), 0, FluxType.Formula);

        // ── 链式访问器 ──

        /// <summary>是否为链式公式（vs 原子字节码公式）</summary>
        public bool IsChained => _chain != null && _chain.Length > 0;

        /// <summary>链式表示的链接数（原子公式返回 0）</summary>
        public int ChainLength => _chain?.Length ?? 0;

        /// <summary>获取链式链接的只读视图</summary>
        internal ReadOnlySpan<ChainLink> GetChainLinks() =>
            _chain != null ? _chain.AsSpan() : default;

        // ── Formula ↔ Modifier 互转 ──

        /// <summary>
        /// 转换为 Modifier：移除第一个数据操作数，后续指令中对 dest 寄存器的引用全部重命名为 R1。
        /// 已为 Modifier 则原样返回。链式公式先转为原子再操作。
        /// </summary>
        public FluxFormula<TData, TOper> ToMultiplier()
        {
            if (Type == FluxType.Modifier) return this;
            if (IsChained) return ToAtomic().ToMultiplier();

            if (Count < 2)
                throw new InvalidOperationException("Cannot convert formula with fewer than 2 instructions to Modifier.");

            // 第一个指令必须是 Immediate（加载第一操作数）
            byte firstOp = _buffer[0].OpCode;
            // 通过 TDef 我们无法访问 GetKind，这里用 Formula 自身的上下文。
            // 第一个指令的 Dest 寄存器就是第一操作数所在寄存器。
            byte destReg = _buffer[0].Dest;
            int dataSlots = (Unsafe.SizeOf<TData>() + Unsafe.SizeOf<Instruction>() - 1) / Unsafe.SizeOf<Instruction>();

            // 新指令数 = 原指令数 - 1(Immediate) - dataSlots(Immediate 数据)
            int newCount = Count - 1 - dataSlots;
            var newBuffer = new Instruction[newCount];

            // 复制剩余指令（跳过第一个 Immediate 及其数据槽位）
            Array.Copy(_buffer, 1 + dataSlots, newBuffer, 0, newCount);

            // 寄存器重命名：destReg → 1 (R1)
            if (destReg != 1)
            {
                for (int i = 0; i < newCount; i++)
                {
                    ref var inst = ref newBuffer[i];
                    if (inst.Dest == destReg) inst.Dest = 1;
                    if (inst.Arg0 == destReg) inst.Arg0 = 1;
                    if (inst.Arg1 == destReg) inst.Arg1 = 1;
                    if (inst.Arg2 == destReg) inst.Arg2 = 1;
                    if (inst.Arg3 == destReg) inst.Arg3 = 1;
                    if (inst.Arg4 == destReg) inst.Arg4 = 1;
                    if (inst.Arg5 == destReg) inst.Arg5 = 1;
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

            return new FluxFormula<TData, TOper>(newBuffer, newCount, FluxType.Modifier,
                newImmCount, newSlots);
        }

        /// <summary>
        /// Modifier→Formula：插入命名变量替代 R1 输入。
        /// 已为 Formula 则原样返回。
        /// </summary>
        public FluxFormula<TData, TOper> ToFormula(string varName)
        {
            if (Type == FluxType.Formula) return this;
            if (IsChained) return ToAtomic().ToFormula(varName);

            int dataSlots = (Unsafe.SizeOf<TData>() + Unsafe.SizeOf<Instruction>() - 1) / Unsafe.SizeOf<Instruction>();

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

            // 重命名 R1(1) → newDest 在后续指令中
            for (int i = 1 + dataSlots; i < newCount; i++)
            {
                ref var inst = ref newBuffer[i];
                if (inst.Dest == 1) inst.Dest = newDest;
                if (inst.Arg0 == 1) inst.Arg0 = newDest;
                if (inst.Arg1 == 1) inst.Arg1 = newDest;
                if (inst.Arg2 == 1) inst.Arg2 = newDest;
                if (inst.Arg3 == 1) inst.Arg3 = newDest;
                if (inst.Arg4 == 1) inst.Arg4 = newDest;
                if (inst.Arg5 == 1) inst.Arg5 = newDest;
            }

            // 变量槽：新变量 SlotIndex=0，旧变量 SlotIndex+1
            var newSlots = new VariableSlot[VariableSlots.Length + 1];
            newSlots[0] = new VariableSlot(varName, 0);
            for (int i = 0; i < VariableSlots.Length; i++)
                newSlots[i + 1] = new VariableSlot(
                    VariableSlots[i].Name,
                    VariableSlots[i].SlotIndex + 1);

            return new FluxFormula<TData, TOper>(newBuffer, newCount, FluxType.Formula,
                ImmediateCount + 1, newSlots);
        }

        /// <summary>查找一条 bytecode 中未被占用的最低寄存器号（R0/R1 保留）</summary>
        private static byte FindFreeRegister(ReadOnlySpan<Instruction> program, int dataSlots)
        {
            Span<bool> used = stackalloc bool[256];
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
            for (byte r = 2; r < 255; r++)
                if (!used[r]) return r;
            return 254;
        }

        public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
        {
            if (this.Count == 0) return next;
            if (next.Count == 0) return this;
            if (this.Count == 1) return next;

            return ChainConnect(GetLinks(), next.GetLinks());
        }

        // ── Connect 辅助 ──

        /// <summary>为 Connect 提取链式链接（原子公式自动包装为单链接）</summary>
        private ChainLink[] GetLinks()
            => IsChained ? _chain : new[] { ToChainLink() };

        /// <summary>构建链式 FluxFormula，合并两段链接的变量槽并右移后半段 SlotIndex</summary>
        private static FluxFormula<TData, TOper> ChainConnect(
            ChainLink[] thisLinks, ChainLink[] nextLinks)
        {
            int totalLinks = thisLinks.Length + nextLinks.Length;
            var chain = new ChainLink[totalLinks];

            Array.Copy(thisLinks, 0, chain, 0, thisLinks.Length);

            int prevImmediateCount = 0;
            for (int i = 0; i < thisLinks.Length; i++)
                prevImmediateCount += thisLinks[i].ImmediateCount;

            for (int i = 0; i < nextLinks.Length; i++)
            {
                var src = nextLinks[i];
                var shiftedSlots = new VariableSlot[src.VarSlots.Length];
                for (int j = 0; j < src.VarSlots.Length; j++)
                    shiftedSlots[j] = new VariableSlot(
                        src.VarSlots[j].Name,
                        src.VarSlots[j].SlotIndex + prevImmediateCount);
                chain[thisLinks.Length + i] = new ChainLink
                {
                    Key              = src.Key,
                    Bytecode         = src.Bytecode,
                    InstructionCount = src.InstructionCount,
                    Type             = src.Type,
                    ImmediateCount   = src.ImmediateCount,
                    VarSlots         = shiftedSlots,
                };
            }

            // 合并所有 VariableSlots
            int totalSlots = 0;
            for (int i = 0; i < totalLinks; i++)
                totalSlots += chain[i].VarSlots.Length;
            var mergedSlots = new VariableSlot[totalSlots];
            int sidx = 0;
            for (int i = 0; i < totalLinks; i++)
                foreach (var vs in chain[i].VarSlots)
                    mergedSlots[sidx++] = vs;

            int totalImmediate = 0;
            for (int i = 0; i < totalLinks; i++)
                totalImmediate += chain[i].ImmediateCount;

            FluxType newType = (chain[0].Type == FluxType.Formula)
                ? FluxType.Formula : FluxType.Modifier;

            return new FluxFormula<TData, TOper>(chain, newType, totalImmediate, mergedSlots);
        }

        /// <summary>从原子公式创建单个 ChainLink（等效于 .ToChainLink()）</summary>
        private ChainLink ToChainLink()
        {
            return new ChainLink
            {
                Key              = GetByteHash(),
                Bytecode         = _buffer,
                InstructionCount = Count,
                Type             = Type,
                ImmediateCount   = ImmediateCount,
                VarSlots         = VariableSlots,
            };
        }

        // ── 链式 → 原子（.ToAtomic()）──

        /// <summary>
        /// 将链式公式合并为单个原子公式（完整字节码拼接 + 变量槽合并）。
        /// 所有 link 的 Instruction[] 原样拼接，中间 Return 由解释器处理为 R1 总线传递。
        /// 调用时机：JIT 求值前、长链（>8）解释器求值前。
        /// </summary>
        internal FluxFormula<TData, TOper> ToAtomic()
        {
            if (!IsChained) return this;

            var links = _chain;
            if (links.Length == 1)
            {
                return new FluxFormula<TData, TOper>(
                    links[0].Bytecode, links[0].InstructionCount,
                    links[0].Type, links[0].ImmediateCount, links[0].VarSlots);
            }

            // 完整拼接：不丢弃任何指令。中间 Return 由解释器语义处理（Dest→R1，继续执行）
            int totalCount = 0;
            for (int i = 0; i < links.Length; i++)
                totalCount += links[i].InstructionCount;

            var buffer = new Instruction[totalCount];
            int dst = 0;
            for (int i = 0; i < links.Length; i++)
            {
                Array.Copy(links[i].Bytecode, 0, buffer, dst, links[i].InstructionCount);
                dst += links[i].InstructionCount;
            }

            int totalSlots = 0;
            foreach (var ls in links) totalSlots += ls.VarSlots.Length;
            var slots = new VariableSlot[totalSlots];
            int sIdx = 0;
            foreach (var ls in links)
                foreach (var vs in ls.VarSlots)
                    slots[sIdx++] = vs;

            int totalImm = 0;
            foreach (var ls in links) totalImm += ls.ImmediateCount;

            return new FluxFormula<TData, TOper>(buffer, totalCount, links[0].Type,
                totalImm, slots);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Instruction> Raw() => _buffer.AsSpan(0, Count);

        // ================================================================
        // 哈希——公式字节码的内容寻址键
        // ================================================================

        /// <summary>
        /// 计算公式的 DualHash64 标识。
        /// 对原子公式：等效于 ToBytes() 的哈希。
        /// 对链式公式：顺序 Combine 各 link 的 Key。
        /// </summary>
        /// <remarks>
        /// 对 build-time 公式，此哈希存储在 Source Generator 生成的偏移表中。
        /// 对运行期 Connect 产物，用作 CompileCache/DelegateCache 的 key。
        /// </remarks>
        public readonly DualHash64 GetByteHash()
        {
            if (IsChained)
            {
                // 链式公式的哈希 = 所有 link 的顺序 Combine
                var h = _chain[0].Key;
                for (int i = 1; i < _chain.Length; i++)
                    h = DualHash64.Combine(h, _chain[i].Key);
                return h;
            }

            // 原子公式：序列化后计算哈希
            var bytes = ToBytes();
            return DualHash64.Compute(bytes);
        }

        // ================================================================
        // 零序列化持久化：字节码即文件格式
        // ================================================================

        /// <summary>
        /// 将公式序列化为字节数组。可直接写入磁盘，无需 JSON/XML。
        /// Instruction 是固定 8 字节结构体——这一事实本身就是序列化格式。
        /// </summary>
        public readonly byte[] ToBytes()
        {
            // 头部：Count(4) + Type(1) + ImmediateCount(4) + VarSlotCount(4) = 13 字节
            int varSlotCount = VariableSlots.Length;
            int instSize = Count * 8;

            // 预编码变量名
            var enc = System.Text.Encoding.UTF8;
            var nameBytesList = new byte[varSlotCount][];
            int nameBytesTotal = 0;
            for (int i = 0; i < varSlotCount; i++)
            {
                nameBytesList[i] = enc.GetBytes(VariableSlots[i].Name);
                nameBytesTotal += nameBytesList[i].Length;
            }

            // 每个变量槽：NameLen(4) + NameBytes + SlotIndex(4)
            int slotSectionSize = varSlotCount * 8 + nameBytesTotal;
            int totalSize = 13 + instSize + slotSectionSize;

            byte[] data = new byte[totalSize];
            int offset = 0;

            // ── 头部 ──
            WriteInt(data, ref offset, Count);
            data[offset++] = (byte)Type;
            WriteInt(data, ref offset, ImmediateCount);
            WriteInt(data, ref offset, varSlotCount);

            // ── 指令（每个 8 字节，写 Raw 字段）──
            for (int i = 0; i < Count; i++)
            {
                WriteLong(data, ref offset, _buffer[i].Raw);
            }

            // ── 变量槽 ──
            for (int i = 0; i < varSlotCount; i++)
            {
                byte[] nb = nameBytesList[i];
                WriteInt(data, ref offset, nb.Length);
                Buffer.BlockCopy(nb, 0, data, offset, nb.Length); offset += nb.Length;
                WriteInt(data, ref offset, VariableSlots[i].SlotIndex);
            }

            return data;
        }

        /// <summary>
        /// 从字节数组反序列化公式。与 <see cref="ToBytes"/> 配对使用。
        /// TData/TOper 类型参数由调用方提供（字节格式不携带泛型类型信息）。
        /// </summary>
        public static FluxFormula<TData, TOper> FromBytes(byte[] data)
        {
            return FromBytes(data.AsSpan());
        }

        /// <summary>
        /// 从只读字节跨度反序列化公式。
        /// 与 <see cref="FromBytes(byte[])"/> 相同逻辑，但接受 <see cref="ReadOnlySpan{T}"/>
        /// ——避免从 native 指针重建时需要临时 byte[] 分配。
        /// </summary>
        public static FluxFormula<TData, TOper> FromBytes(ReadOnlySpan<byte> data)
        {
            int offset = 0;

            // ── 头部 ──
            int count          = ReadInt(data, ref offset);
            var type           = (FluxType)data[offset++];
            int immediateCount = ReadInt(data, ref offset);
            int varSlotCount   = ReadInt(data, ref offset);

            if (count < 0 || immediateCount < 0 || varSlotCount < 0)
                throw new ArgumentException("Corrupted formula data: negative count fields.");

            // ── 指令 ──
            var instructions = new Instruction[count];
            for (int i = 0; i < count; i++)
            {
                instructions[i] = new Instruction { Raw = ReadLong(data, ref offset) };
            }

            // ── 变量槽 ──
            var varSlots = varSlotCount > 0 ? new VariableSlot[varSlotCount] : Array.Empty<VariableSlot>();
            var enc = System.Text.Encoding.UTF8;
            for (int i = 0; i < varSlotCount; i++)
            {
                int nameLen = ReadInt(data, ref offset);
                string name;
                unsafe { fixed (byte* p = data) { name = enc.GetString(p + offset, nameLen); } }
                offset += nameLen;
                int slotIdx = ReadInt(data, ref offset);
                varSlots[i] = new VariableSlot(name, slotIdx);
            }

            return new FluxFormula<TData, TOper>(instructions, count, type, immediateCount, varSlots);
        }

        // ── 小端序读写辅助（byte[] 版本）──

        private static void WriteInt(byte[] buf, ref int offset, int value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }

        private static void WriteLong(byte[] buf, ref int offset, long value)
        {
            buf[offset]     = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            buf[offset + 4] = (byte)(value >> 32);
            buf[offset + 5] = (byte)(value >> 40);
            buf[offset + 6] = (byte)(value >> 48);
            buf[offset + 7] = (byte)(value >> 56);
            offset += 8;
        }

        private static int ReadInt(byte[] buf, ref int offset)
        {
            int v = buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24);
            offset += 4;
            return v;
        }

        private static long ReadLong(byte[] buf, ref int offset)
        {
            long v = (long)buf[offset]
                   | ((long)buf[offset + 1] << 8)
                   | ((long)buf[offset + 2] << 16)
                   | ((long)buf[offset + 3] << 24)
                   | ((long)buf[offset + 4] << 32)
                   | ((long)buf[offset + 5] << 40)
                   | ((long)buf[offset + 6] << 48)
                   | ((long)buf[offset + 7] << 56);
            offset += 8;
            return v;
        }

        // ── 小端序读写辅助（ReadOnlySpan<byte> 版本）──

        private static int ReadInt(ReadOnlySpan<byte> data, ref int offset)
        {
            int v = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
            offset += 4;
            return v;
        }

        private static long ReadLong(ReadOnlySpan<byte> data, ref int offset)
        {
            long v = (long)data[offset]
                   | ((long)data[offset + 1] << 8)
                   | ((long)data[offset + 2] << 16)
                   | ((long)data[offset + 3] << 24)
                   | ((long)data[offset + 4] << 32)
                   | ((long)data[offset + 5] << 40)
                   | ((long)data[offset + 6] << 48)
                   | ((long)data[offset + 7] << 56);
            offset += 8;
            return v;
        }

        public override readonly string ToString() =>
            $"FluxFormula<{typeof(TData).Name}> [Type: {Type}, Instructions: {Count}]";
    }
}
