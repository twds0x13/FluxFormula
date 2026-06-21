using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    // ═══════════════════════════════════════════════════════
    // Magic & Constants
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// VFF (Virtual FluxFormula) 字节格式定义与解析器。
    /// </summary>
    /// <remarks>
    /// <para>VFF 不是独立资产类型——它是 blob 中的一种条目，通过 <c>"VFF\0"</c> magic 与公式条目区分。
    /// VFF 条目和公式条目共存于同一个 blob，使用同一套 <see cref="FluxBlob.Entry"/> 偏移表。</para>
    ///
    /// <para>字节布局：</para>
    /// <code>
    /// Header (8B):     Magic(4 "VFF\0") + Version(1) + LinkCount(1) + OverrideCount(1) + Flags(1)
    /// LinkTable:       LinkCount × 22B — Hash(16) + ImmCount(1) + InstCount(2) + Type(1) + VarSlotCount(2)
    /// OverrideTable:   OverrideCount × variable — GlobalSlot(2) + Kind(1) + [DataLen(1) + Data(var)]
    /// </code>
    /// <para>变量名不存储在 VFF 内——resolve 时通过 <see cref="FormulaFormat.ReadVariableSlots"/>
    /// 从被引用公式的字节码中直接读取。</para>
    /// </remarks>
    public static partial class VffFormat
    {
        /// <summary>VFF 条目识别 magic bytes: "VFF\0"</summary>
        public static readonly byte[] Magic = { (byte)'V', (byte)'F', (byte)'F', 0 };

        public const int HeaderSize   = 8;
        public const int LinkEntrySize = 22;

        // Flags
        public const byte FlagHasConstants = 1 << 0;
    }

    // ═══════════════════════════════════════════════════════
    // Header
    // ═══════════════════════════════════════════════════════

    /// <summary>VFF 8 字节头。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
    public readonly struct VffHeader
    {
        // byte 0-3: Magic
        /// <summary>格式版本号（当前为 1）</summary>
        public readonly byte Version;

        /// <summary>被引用公式的数量</summary>
        public readonly byte LinkCount;

        /// <summary>参数覆写数量</summary>
        public readonly byte OverrideCount;

        /// <summary>标志位（bit0: 包含硬编码常量数据）</summary>
        public readonly byte Flags;

        public VffHeader(byte version, byte linkCount, byte overrideCount, byte flags)
        {
            Version      = version;
            LinkCount    = linkCount;
            OverrideCount = overrideCount;
            Flags        = flags;
        }

        public bool HasConstants => (Flags & VffFormat.FlagHasConstants) != 0;
    }

    // ═══════════════════════════════════════════════════════
    // Link entry (22 bytes)
    // ═══════════════════════════════════════════════════════

    /// <summary>VFF 链接表中一条公式引用（22 字节）。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 22)]
    public readonly struct VffLinkEntry
    {
        // +0  (16): DualHash64
        /// <summary>被引用公式的 DualHash64</summary>
        public readonly DualHash64 Hash;

        // +16 (1)
        /// <summary>该 link 的 Immediate 数量</summary>
        public readonly byte ImmCount;

        // +17 (2)
        /// <summary>该 link 的 Instruction 数量</summary>
        public readonly ushort InstCount;

        // +19 (1)
        /// <summary>Formula(0) 还是 Modifier(1)</summary>
        public readonly FluxType Type;

        // +20 (2)
        /// <summary>该 link 的变量槽数</summary>
        public readonly ushort VarSlotCount;

        public VffLinkEntry(DualHash64 hash, byte immCount, ushort instCount, FluxType type, ushort varSlotCount)
        {
            Hash         = hash;
            ImmCount     = immCount;
            InstCount    = instCount;
            Type         = type;
            VarSlotCount = varSlotCount;
        }
    }

    // ═══════════════════════════════════════════════════════
    // Override
    // ═══════════════════════════════════════════════════════

    /// <summary>参数覆写类型。</summary>
    public enum VffOverrideKind : byte
    {
        /// <summary>求值时由调用方通过 Injector 注入</summary>
        Inject = 0,

        /// <summary>VFF 定义时已硬编码为固定值</summary>
        Constant = 1,
    }

    /// <summary>解析后的参数覆写元数据。</summary>
    public readonly struct VffOverride<TData>
        where TData : unmanaged
    {
        /// <summary>合并管道中的 Immediate 全局序号</summary>
        public readonly int GlobalSlot;

        /// <summary>覆写类型</summary>
        public readonly VffOverrideKind Kind;

        /// <summary>Kind=Constant 时的硬编码值（否则为 default）</summary>
        public readonly TData ConstantValue;

        public VffOverride(int globalSlot, VffOverrideKind kind, TData constantValue = default)
        {
            GlobalSlot    = globalSlot;
            Kind          = kind;
            ConstantValue = constantValue;
        }
    }

    // ═══════════════════════════════════════════════════════
    // Resolve result
    // ═══════════════════════════════════════════════════════

    /// <summary>VFF 解析结果——包含构建好的链式公式和参数覆写元数据。</summary>
    public readonly struct VffResolveResult<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        /// <summary>解析产出的链式公式（可传入 Instantiate）</summary>
        public readonly FluxFormula<TData, TOper> Formula;

        /// <summary>参数覆写列表（空数组 = 纯引用拼接无覆写）</summary>
        public readonly VffOverride<TData>[] Overrides;

        public VffResolveResult(FluxFormula<TData, TOper> formula, VffOverride<TData>[] overrides)
        {
            Formula   = formula;
            Overrides = overrides ?? Array.Empty<VffOverride<TData>>();
        }
    }

    // ═══════════════════════════════════════════════════════
    // VffFormat static methods
    // ═══════════════════════════════════════════════════════

    public static partial class VffFormat
    {
        /// <summary>检测一段字节码是否为 VFF 条目。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsVff(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length >= 4
                && bytes[0] == Magic[0]
                && bytes[1] == Magic[1]
                && bytes[2] == Magic[2]
                && bytes[3] == Magic[3];
        }

        /// <summary>
        /// 从 <see cref="FormulaCache"/> 读取 VFF 条目，解析为链式公式。
        /// </summary>
        /// <param name="vffHash">VFF 条目自身的 DualHash64（偏移表中存储的键）</param>
        /// <returns>解析结果——链式 <see cref="FluxFormula{TData, TOper}"/> + 覆写元数据</returns>
        /// <exception cref="InvalidOperationException">
        /// 缓存未命中、条目不是 VFF、版本不支持、或引用的公式不在缓存中。
        /// </exception>
        public static unsafe VffResolveResult<TData, TOper> Resolve<TData, TOper>(
            DualHash64 vffHash)
            where TData : unmanaged
            where TOper : unmanaged, Enum
        {
            if (!ConnectCache.Cache.TryGet(vffHash, out IntPtr vffPtr, out int vffLen))
                throw new InvalidOperationException(
                    $"VFF entry not found in cache for hash: {vffHash}");

            var vffBytes = new ReadOnlySpan<byte>((void*)vffPtr, vffLen);

            // ── 解析头部 ──
            if (!IsVff(vffBytes))
                throw new InvalidOperationException(
                    $"Blob entry is not a VFF (magic mismatch). Hash: {vffHash}");

            byte version       = vffBytes[4];
            byte linkCount     = vffBytes[5];
            byte overrideCount = vffBytes[6];
            byte flags         = vffBytes[7];

            if (version != 1)
                throw new InvalidOperationException(
                    $"Unsupported VFF version: {version}. Expected: 1.");

            // ── 解析 Link Table ──
            int linkTableStart = HeaderSize;
            var links = new ChainLink[linkCount];
            int cumImm = 0;

            for (int i = 0; i < linkCount; i++)
            {
                int lo = linkTableStart + i * LinkEntrySize;
                var entry = MemoryMarshal.Read<VffLinkEntry>(vffBytes.Slice(lo));

                // 从 FormulaCache 查找被引用公式的字节码
                if (!ConnectCache.Cache.TryGet(entry.Hash, out IntPtr fPtr, out int fLen))
                    throw new InvalidOperationException(
                        $"VFF link [{i}] references formula not in cache. Hash: {entry.Hash}");

                var fBytes = new ReadOnlySpan<byte>((void*)fPtr, fLen);

                if (IsVff(fBytes))
                    throw new InvalidOperationException(
                        $"VFF link [{i}] references another VFF — recursive resolve not supported in v1. Hash: {entry.Hash}");

                // 从公式字节码提取 Instruction[]（零拷贝）
                var fHeader = FormulaFormat.ReadHeader(fBytes);
                var instSpan = FormulaFormat.GetInstructionSpan(fBytes);
                var bytecode = instSpan.ToArray();

                // 读取变量槽（SlotIndex 全局化为 cumImm 偏移）
                var varSlots = FormulaFormat.ReadVariableSlots(fBytes, baseSlotOffset: cumImm);

                links[i] = new ChainLink
                {
                    Key              = entry.Hash,
                    Bytecode         = bytecode,
                    InstructionCount = bytecode.Length,
                    Type             = entry.Type,
                    ImmediateCount   = entry.ImmCount,
                    VarSlots         = varSlots,
                    MaxRegister      = fHeader.MaxRegister,
                };

                cumImm += entry.ImmCount;
            }

            // ── 解析 Override Table ──
            int ovrdTableStart = linkTableStart + linkCount * LinkEntrySize;
            var overrides = new VffOverride<TData>[overrideCount];
            int ovOff = ovrdTableStart;

            int dataLen = sizeof(TData);
            for (int i = 0; i < overrideCount; i++)
            {
                int globalSlot = vffBytes[ovOff] | (vffBytes[ovOff + 1] << 8);
                var kind = (VffOverrideKind)vffBytes[ovOff + 2];
                ovOff += 3;

                TData constVal = default;
                if (kind == VffOverrideKind.Constant)
                {
                    if (vffBytes[ovOff] != dataLen)
                        throw new InvalidOperationException(
                            $"VFF override [{i}] constant data length mismatch: expected {dataLen}, got {vffBytes[ovOff]}");
                    ovOff++;
                    constVal = MemoryMarshal.Read<TData>(vffBytes.Slice(ovOff));
                    ovOff += dataLen;
                }

                overrides[i] = new VffOverride<TData>(globalSlot, kind, constVal);
            }

            // ── 合并变量槽 ──
            int totalSlots = 0;
            for (int i = 0; i < linkCount; i++)
                totalSlots += links[i].VarSlots.Length;

            var mergedSlots = new VariableSlot[totalSlots];
            int sidx = 0;
            for (int i = 0; i < linkCount; i++)
                for (int j = 0; j < links[i].VarSlots.Length; j++)
                    mergedSlots[sidx++] = links[i].VarSlots[j];

            int totalImm = 0;
            for (int i = 0; i < linkCount; i++)
                totalImm += links[i].ImmediateCount;

            var chainType = (links.Length > 0 && links[0].Type == FluxType.Modifier)
                ? FluxType.Modifier : FluxType.Formula;

            var formula = new FluxFormula<TData, TOper>(links, chainType, totalImm, mergedSlots);

            return new VffResolveResult<TData, TOper>(formula, overrides);
        }
    }
}
