using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
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

    public readonly struct FluxFormula<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        private readonly Instruction[] _buffer;
        public readonly int Count;
        public readonly FluxType Type;
        public readonly int ImmediateCount;
        public readonly VariableSlot[] VariableSlots;

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
            _buffer = buffer;
            Count = count;
            Type = type;
            ImmediateCount = immediateCount;
            VariableSlots = varSlots ?? Array.Empty<VariableSlot>();
        }

        /// <summary>空公式（Count=0），主要用于 Connect 边界场景</summary>
        public static FluxFormula<TData, TOper> Empty => new(Array.Empty<Instruction>(), 0, FluxType.Formula);

        public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
        {
            if (this.Count == 0) return next;
            if (next.Count == 0) return this;
            if (this.Count == 1) return next;

            int newCount = (this.Count - 1) + next.Count;
            Instruction[] newBuffer = new Instruction[newCount];

            Array.Copy(this._buffer, 0, newBuffer, 0, this.Count - 1);
            Array.Copy(next._buffer, 0, newBuffer, this.Count - 1, next.Count);

            // 合并变量槽：后者 SlotIndex 需右移前段 ImmediateCount
            int totalSlots = this.VariableSlots.Length + next.VariableSlots.Length;
            var mergedSlots = new VariableSlot[totalSlots];
            Array.Copy(this.VariableSlots, mergedSlots, this.VariableSlots.Length);
            for (int i = 0; i < next.VariableSlots.Length; i++)
            {
                var s = next.VariableSlots[i];
                mergedSlots[this.VariableSlots.Length + i] =
                    new VariableSlot(s.Name, s.SlotIndex + this.ImmediateCount);
            }

            FluxType newType =
                (this.Type == FluxType.Formula) ? FluxType.Formula : FluxType.Modifier;

            return new FluxFormula<TData, TOper>(newBuffer, newCount, newType,
                this.ImmediateCount + next.ImmediateCount, mergedSlots);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Instruction> Raw() => _buffer.AsSpan(0, Count);

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
                string name = enc.GetString(data, offset, nameLen); offset += nameLen;
                int slotIdx = ReadInt(data, ref offset);
                varSlots[i] = new VariableSlot(name, slotIdx);
            }

            return new FluxFormula<TData, TOper>(instructions, count, type, immediateCount, varSlots);
        }

        // ── 小端序读写辅助（兼容 Unity .NET Standard 2.0）──

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

        public override readonly string ToString() =>
            $"FluxFormula<{typeof(TData).Name}> [Type: {Type}, Instructions: {Count}]";
    }
}
