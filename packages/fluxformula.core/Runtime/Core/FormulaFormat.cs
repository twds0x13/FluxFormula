using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 原子公式（.ff）字节码格式定义与读写辅助。
    /// </summary>
    /// <remarks>
    /// <para>字节布局：</para>
    /// <code>
    /// Header (14B): Count(4 LE) + Type(1) + ImmediateCount(4 LE) + VarSlotCount(4 LE) + MaxRegister(1)
    /// Body:         Instruction[Count] (Count × 8B)
    /// Tail:         VariableSlot[] — 每槽: NameLen(4 LE) + UTF8 + SlotIndex(4 LE)
    /// </code>
    ///
    /// <para>与 <see cref="VffFormat"/> 的关系：VFF 通过 "VFF\0" magic 与公式条目区分。
    /// 读取侧先调用 <see cref="VffFormat.IsVff"/> 判类型，非 VFF 则走本格式。</para>
    ///
    /// <para><b>修改 Instruction 结构体大小时的有序步骤：</b></para>
    /// <list type="number">
    /// <item><description><c>Instruction.cs</c> — 调整 <c>FieldOffset</c> 和 <c>Raw</c> 字段以覆盖新宽度。</description></item>
    /// <item><description><c>FluxFormula.ToBytes()</c> / <c>FromBytes()</c> — 序列化读/写新宽度的 Raw 字段。</description></item>
    /// <item><description>重新生成所有 blob（<c>FluxBlobBuilder.Build()</c>），旧格式字节码与新版不兼容。</description></item>
    /// <item><description>运行 152 tests 确认自举完成。</description></item>
    /// </list>
    /// <para><see cref="InstructionSize"/> 和 <see cref="DataSlots{TData}"/> 自动通过 <c>sizeof</c> 跟踪，
    /// 修改者无需手动更新这两处。</para>
    /// </remarks>
    public static partial class FormulaFormat
    {
        /// <summary>头部大小（字节）</summary>
        public const int HeaderSize = 14;

        /// <summary>Instruction 段起始偏移（字节）= HeaderSize</summary>
        public const int InstructionOffset = 14;

        /// <summary>
        /// 每条 Instruction 的字节数 = sizeof(Instruction)。
        /// 非 const：当 Instruction 结构体大小变化时自动跟踪。
        /// </summary>
        public static int InstructionSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { unsafe { return sizeof(Instruction); } }
        }

        /// <summary>
        /// TData 值在 Instruction 数组中占用的槽位数 = ceil(sizeof(TData) / sizeof(Instruction))。
        /// 项目中所有 dataSlots 计算的唯一来源。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DataSlots<TData>()
            where TData : unmanaged
        {
            unsafe
            {
                return (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            }
        }

        // ── Header 字段偏移 ──

        private const int OffCount          = 0;  // int32 LE
        private const int OffType           = 4;  // byte
        private const int OffImmediateCount = 5;  // int32 LE
        private const int OffVarSlotCount   = 9;  // int32 LE
        private const int OffMaxRegister    = 13; // byte

        /// <summary>类型指纹后缀大小（字节）。置于 .ff 文件末尾，Header + Body + VarSlots 之后。</summary>
        public const int FingerprintSize = 8;

        // ── 类型指纹 ──

        /// <summary>
        /// 计算 TData/TDef 组合的类型指纹。
        /// 指纹 = xxHash64(typeof(TDef).FullName + "|" + sizeof(TData))。
        /// 用于 .ff 文件反序列化时的类型校验，防止跨定义注入。
        /// </summary>
        public static ulong ComputeTypeFingerprint<TData, TDef>()
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            unsafe
            {
                string key = $"{typeof(TDef).FullName}|{sizeof(TData)}";
                byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                return DualHash64.Compute(keyBytes).XxHash64;
            }
        }

        /// <summary>向字节缓冲区末尾写入类型指纹。</summary>
        public static void WriteTypeFingerprint<TData, TDef>(byte[] buf, ref int offset)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            BinaryFormat.WriteInt64LE(buf, ref offset, (long)ComputeTypeFingerprint<TData, TDef>());
        }

        /// <summary>
        /// 从字节跨度指定偏移量读取 ulong 指纹，验证与 TData/TDef 匹配。
        /// 若指纹为零（旧格式无指纹），跳过后向兼容。
        /// </summary>
        /// <returns>true = 通过；false = 指纹不匹配（跨定义注入或数据损坏）。调用方应抛出。</returns>
        public static bool ValidateTypeFingerprint<TData, TDef>(ReadOnlySpan<byte> data, int fingerprintOffset)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            if (fingerprintOffset + FingerprintSize > data.Length)
                return true; // not enough data for fingerprint — old format, assume valid

            ulong stored = BinaryFormat.ReadUInt64LE(data, fingerprintOffset);
            if (stored == 0)
                return true; // old format (no fingerprint written) — skip validation

            ulong expected = ComputeTypeFingerprint<TData, TDef>();
            return stored == expected;
        }
    }

    /// <summary>公式字节码头：不包含 magic byte（非 VFF 即为公式）。</summary>
    public readonly struct FormulaHeader
    {
        /// <summary>Instruction 条数</summary>
        public readonly int Count;

        /// <summary>公式类型：0=Modifier, 1=Formula</summary>
        public readonly byte Type;

        /// <summary>Immediate 数据槽数</summary>
        public readonly int ImmediateCount;

        /// <summary>变量槽数</summary>
        public readonly int VarSlotCount;

        /// <summary>
        /// 该公式使用的最大寄存器索引（0=未分析，回退到 <see cref="FluxPlatform.MaxRegisters"/>）。
        /// 不含 R0/R1 保留寄存器：R0 和 R1 始终存在，实际分配量为 MaxRegister+1。
        /// </summary>
        public readonly byte MaxRegister;

        public FormulaHeader(int count, byte type, int immediateCount, int varSlotCount, byte maxRegister = 0)
        {
            Count          = count;
            Type           = type;
            ImmediateCount = immediateCount;
            VarSlotCount   = varSlotCount;
            MaxRegister    = maxRegister;
        }
    }

    public static partial class FormulaFormat
    {
        /// <summary>
        /// 判断一段字节码是否为公式条目（非 VFF 且头部可解析）。
        /// 当前公式条目无 magic：通过排除 VFF + 基本合理性检查实现。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFormula(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < HeaderSize) return false;

            // 排除 VFF magic
            // (VffFormat 在同一程序集但为保持单向依赖，内联 magic 检查)
            if (bytes.Length >= 4
                && bytes[0] == (byte)'V'
                && bytes[1] == (byte)'F'
                && bytes[2] == (byte)'F'
                && bytes[3] == 0)
                return false;

            // 基本合理性：count > 0 且指令段不越界
            int count = bytes[OffCount] | (bytes[OffCount + 1] << 8)
                      | (bytes[OffCount + 2] << 16) | (bytes[OffCount + 3] << 24);

            return count > 0 && HeaderSize + count * InstructionSize <= bytes.Length;
        }

        /// <summary>从字节码读取头部。</summary>
        public static FormulaHeader ReadHeader(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < HeaderSize)
                throw new ArgumentException($"Formula bytecode too short for header: {bytes.Length} bytes.");

            int count          = (int)BinaryFormat.ReadUInt32LE(bytes, OffCount);
            byte type          = bytes[OffType];
            int immediateCount = (int)BinaryFormat.ReadUInt32LE(bytes, OffImmediateCount);
            int varSlotCount   = (int)BinaryFormat.ReadUInt32LE(bytes, OffVarSlotCount);
            byte maxRegister   = bytes[OffMaxRegister];

            return new FormulaHeader(count, type, immediateCount, varSlotCount, maxRegister);
        }

        /// <summary>
        /// 从字节码提取 Instruction 跨度：零拷贝 MemoryMarshal.Cast。
        /// 调用方负责确保 bytes 是从 <see cref="FormulaCache"/> 获取的有效公式字节码。
        /// </summary>
        public static ReadOnlySpan<Instruction> GetInstructionSpan(ReadOnlySpan<byte> bytes)
        {
            var header = ReadHeader(bytes);
            int instByteLen = header.Count * InstructionSize;

            if (InstructionOffset + instByteLen > bytes.Length)
                throw new ArgumentException(
                    $"Instruction section exceeds bytecode length. count={header.Count}, avail={bytes.Length - InstructionOffset}");

            return MemoryMarshal.Cast<byte, Instruction>(
                bytes.Slice(InstructionOffset, instByteLen));
        }

        /// <summary>
        /// 从字节码解析变量槽。
        /// </summary>
        /// <param name="bytes">完整公式字节码</param>
        /// <param name="baseSlotOffset">SlotIndex 的全局偏移（链式公式中相对于前序 link 的累加值）</param>
        /// <returns>变量槽数组（若 VarSlotCount=0 则返回空数组）</returns>
        public static VariableSlot[] ReadVariableSlots(ReadOnlySpan<byte> bytes, int baseSlotOffset = 0)
        {
            var header = ReadHeader(bytes);

            if (header.VarSlotCount <= 0)
                return Array.Empty<VariableSlot>();

            int vsStart = InstructionOffset + header.Count * InstructionSize;
            var slots = new VariableSlot[header.VarSlotCount];
            int off = vsStart;

            for (int i = 0; i < header.VarSlotCount; i++)
            {
                int nameLen = BinaryFormat.ReadInt32LE(bytes, ref off);

                string name;
                unsafe
                {
                    fixed (byte* p = bytes)
                    {
                        name = System.Text.Encoding.UTF8.GetString(p + off, nameLen);
                    }
                }
                off += nameLen;

                int slotIndex = BinaryFormat.ReadInt32LE(bytes, ref off);

                slots[i] = new VariableSlot(name, slotIndex + baseSlotOffset);
            }

            return slots;
        }

        // ── 写入 ──

        /// <summary>向字节缓冲区写入公式头。</summary>
        public static void WriteHeader(byte[] buf, ref int offset, FormulaHeader header)
        {
            BinaryFormat.WriteInt32LE(buf, ref offset, header.Count);
            buf[offset++] = header.Type;
            BinaryFormat.WriteInt32LE(buf, ref offset, header.ImmediateCount);
            BinaryFormat.WriteInt32LE(buf, ref offset, header.VarSlotCount);
            buf[offset++] = header.MaxRegister;
        }
    }
}
