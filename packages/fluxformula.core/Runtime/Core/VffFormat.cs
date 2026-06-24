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
        /// <summary>公式类型：0=Modifier, 1=Formula</summary>
        public readonly byte Type;

        // +20 (2)
        /// <summary>该 link 的变量槽数</summary>
        public readonly ushort VarSlotCount;

        public VffLinkEntry(DualHash64 hash, byte immCount, ushort instCount, byte type, ushort varSlotCount)
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
    public readonly struct VffResolveResult<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        /// <summary>解析产出的链式公式（可传入 Instantiate）</summary>
        public readonly FluxFormula<TData, TDef> Formula;

        /// <summary>参数覆写列表（空数组 = 纯引用拼接无覆写）</summary>
        public readonly VffOverride<TData>[] Overrides;

        public VffResolveResult(FluxFormula<TData, TDef> formula, VffOverride<TData>[] overrides)
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
        /// <returns>解析结果——链式 <see cref="FluxFormula{TData, TDef}"/> + 覆写元数据</returns>
        /// <exception cref="InvalidOperationException">
        /// 缓存未命中、条目不是 VFF、版本不支持、或引用的公式不在缓存中。
        /// </exception>
        /// <summary>
        /// 从 <see cref="FormulaCache"/> 读取 VFF 条目，解析为链式公式。
        /// </summary>
        /// <param name="vffHash">VFF 条目自身的 DualHash64（偏移表中存储的键）</param>
        /// <returns>解析结果——链式 <see cref="FluxFormula{TData, TDef}"/> + 覆写元数据</returns>
        /// <exception cref="InvalidOperationException">
        /// 缓存未命中、条目不是 VFF、版本不支持、或引用的公式不在缓存中。
        /// </exception>
        public static unsafe VffResolveResult<TData, TDef> Resolve<TData, TDef>(
            DualHash64 vffHash)
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            if (!FormulaCache.Instance.TryGet(vffHash, out IntPtr vffPtr, out int vffLen))
                throw new InvalidOperationException(
                    $"VFF entry not found in cache for hash: {vffHash}");

            var vffBytes = new ReadOnlySpan<byte>((void*)vffPtr, vffLen);
            var visited = new System.Collections.Generic.HashSet<DualHash64>();
            visited.Add(vffHash); // 顶层 VFF 自身入栈
            return ParseAndResolve<TData, TDef>(vffBytes, visited);
        }

        /// <summary>
        /// 从裸字节数组解析 VFF，产出链式公式。
        /// 被引用的公式仍通过 <see cref="FormulaCache"/> 查找——调用前须将依赖公式注入缓存。
        /// </summary>
        /// <param name="data">VFF 格式的字节数组（以 "VFF\0" magic 开头）</param>
        /// <returns>解析结果——链式 <see cref="FluxFormula{TData, TDef}"/> + 覆写元数据</returns>
        /// <exception cref="InvalidOperationException">
        /// 字节不是 VFF、版本不支持、或引用的公式不在缓存中。
        /// </exception>
        public static VffResolveResult<TData, TDef> FromBytes<TData, TDef>(byte[] data)
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            var vffBytes = new ReadOnlySpan<byte>(data);
            // FromBytes 没有顶层哈希——VFF 字节来自外部，不在缓存中，因此无法被其他 VFF 引用形成循环。
            var visited = new System.Collections.Generic.HashSet<DualHash64>();
            return ParseAndResolve<TData, TDef>(vffBytes, visited);
        }

        /// <summary>
        /// 将链式公式引用序列化为 VFF 字节数组。
        /// 与 <see cref="FromBytes{TData, TOper}"/> 配对使用——往返保证链路等价。
        /// </summary>
        /// <param name="links">链式链接数组（如来自 <see cref="FluxFormula{TData, TDef}.GetChainLinks"/>）</param>
        /// <param name="overrides">参数覆写列表（无覆写传空数组）</param>
        /// <returns>VFF 格式字节数组</returns>
        public static byte[] ToBytes<TData>(
            ChainLink[] links,
            VffOverride<TData>[] overrides)
            where TData : unmanaged
        {
            int linkCount = links?.Length ?? 0;
            int overrideCount = overrides?.Length ?? 0;

            // 确定 Flags
            byte flags = 0;
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    if (overrides[i].Kind == VffOverrideKind.Constant)
                    {
                        flags |= FlagHasConstants;
                        break;
                    }
                }
            }

            // 计算 OverrideTable 总大小
            int overrideTableSize = 0;
            int dataLen = 0;
            unsafe { dataLen = sizeof(TData); }
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    overrideTableSize += 3; // GlobalSlot(2) + Kind(1)
                    if (overrides[i].Kind == VffOverrideKind.Constant)
                        overrideTableSize += 1 + dataLen; // DataLen(1) + Data
                }
            }

            int totalSize = HeaderSize + linkCount * LinkEntrySize + overrideTableSize;
            var buf = new byte[totalSize];
            int offset = 0;

            // ── 头部 ──
            buf[offset++] = Magic[0];
            buf[offset++] = Magic[1];
            buf[offset++] = Magic[2];
            buf[offset++] = Magic[3];
            buf[offset++] = 1; // Version
            buf[offset++] = (byte)linkCount;
            buf[offset++] = (byte)overrideCount;
            buf[offset++] = flags;

            // ── 链接表 ──
            if (links != null)
            {
                for (int i = 0; i < links.Length; i++)
                {
                    var link = links[i];
                    BinaryFormat.WriteInt64LE(buf, ref offset, (long)link.Key.XxHash64);
                    BinaryFormat.WriteInt64LE(buf, ref offset, (long)link.Key.FnvHash64);
                    buf[offset++] = (byte)link.ImmediateCount;
                    BinaryFormat.WriteUInt16LE(buf, ref offset, (ushort)link.InstructionCount);
                    buf[offset++] = (byte)link.Type;
                    BinaryFormat.WriteUInt16LE(buf, ref offset, (ushort)(link.VarSlots?.Length ?? 0));
                }
            }

            // ── 覆写表 ──
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    var ov = overrides[i];
                    BinaryFormat.WriteUInt16LE(buf, ref offset, (ushort)ov.GlobalSlot);
                    buf[offset++] = (byte)ov.Kind;

                    if (ov.Kind == VffOverrideKind.Constant)
                    {
                        buf[offset++] = (byte)dataLen;
                        unsafe
                        {
                            fixed (byte* p = &buf[offset])
                            {
                                *(TData*)p = ov.ConstantValue;
                            }
                        }
                        offset += dataLen;
                    }
                }
            }

            return buf;
        }

        /// <summary>
        /// 解析 VFF 字节码的公共逻辑。供 <see cref="Resolve{TData, TOper}"/> 和
        /// <see cref="FromBytes{TData, TOper}"/> 共享。
        /// </summary>
        /// <param name="vffBytes">VFF 字节码跨度</param>
        /// <param name="visited">已访问的 VFF 哈希集合（用于循环检测）</param>
        private static unsafe VffResolveResult<TData, TDef> ParseAndResolve<TData, TDef>(
            ReadOnlySpan<byte> vffBytes,
            System.Collections.Generic.HashSet<DualHash64> visited)
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            // ── 解析头部 ──
            if (!IsVff(vffBytes))
                throw new InvalidOperationException(
                    "Data is not a VFF entry (magic mismatch).");

            byte version = vffBytes[4];
            if (version != 1)
                throw new InvalidOperationException(
                    $"Unsupported VFF version: {version}. Expected: 1.");

            // ── 递归解析链接表 ──
            var (links, overrides, totalImm) = ResolveLinks<TData, TDef>(vffBytes, visited);

            // ── 合并变量槽 ──
            int totalSlots = 0;
            for (int i = 0; i < links.Length; i++)
                totalSlots += links[i].VarSlots.Length;

            var mergedSlots = new VariableSlot[totalSlots];
            int sidx = 0;
            for (int i = 0; i < links.Length; i++)
                for (int j = 0; j < links[i].VarSlots.Length; j++)
                    mergedSlots[sidx++] = links[i].VarSlots[j];

            var chainType = (links.Length > 0 && links[0].Type == FluxType.Modifier)
                ? FluxType.Modifier : FluxType.Formula;

            var formula = new FluxFormula<TData, TDef>(links, chainType, totalImm, mergedSlots);

            return new VffResolveResult<TData, TDef>(formula, overrides);
        }

        /// <summary>
        /// 递归解析 VFF 链接表。当 link 引用的目标是另一个 VFF 时，递归展开其链接并展平到当前链路中。
        /// </summary>
        /// <param name="vffBytes">当前 VFF 条目的完整字节码</param>
        /// <param name="visited">当前递归栈中的 VFF 哈希集合（用于循环检测）</param>
        /// <returns>(展平的 ChainLink[], 展平的覆写列表, 总 ImmediateCount)</returns>
        private static unsafe (ChainLink[] links, VffOverride<TData>[] overrides, int totalImm)
            ResolveLinks<TData, TDef>(
                ReadOnlySpan<byte> vffBytes,
                System.Collections.Generic.HashSet<DualHash64> visited)
            where TData : unmanaged
            where TDef : unmanaged, IFluxJITDefinition<TData>
        {
            byte linkCount     = vffBytes[5];
            byte overrideCount = vffBytes[6];

            int linkTableStart = HeaderSize;
            var links = new System.Collections.Generic.List<ChainLink>();
            var ovrds = new System.Collections.Generic.List<VffOverride<TData>>();
            int cumImm = 0;

            for (int i = 0; i < linkCount; i++)
            {
                int lo = linkTableStart + i * LinkEntrySize;
                var entry = MemoryMarshal.Read<VffLinkEntry>(vffBytes.Slice(lo));

                // 从 FormulaCache 查找被引用条目的字节码
                if (!FormulaCache.Instance.TryGet(entry.Hash, out IntPtr fPtr, out int fLen))
                    throw new InvalidOperationException(
                        $"VFF link [{i}] references entry not in cache. Hash: {entry.Hash}");

                var fBytes = new ReadOnlySpan<byte>((void*)fPtr, fLen);

                if (IsVff(fBytes))
                {
                    // ── 嵌套 VFF：递归展开 ──
                    if (!visited.Add(entry.Hash))
                        throw new InvalidOperationException(
                            $"Circular VFF reference detected: link [{i}] references VFF " +
                            $"{entry.Hash} which is already in the resolution stack. " +
                            "VFF recursion must form a DAG, not a cycle.");

                    var (nestedLinks, nestedOverrides, nestedImm) =
                        ResolveLinks<TData, TDef>(fBytes, visited);

                    // 展平嵌套 links——SlotIndex 偏移 cumImm
                    for (int ni = 0; ni < nestedLinks.Length; ni++)
                    {
                        var nl = nestedLinks[ni];
                        if (nl.VarSlots.Length > 0 && cumImm > 0)
                        {
                            var offsetSlots = new VariableSlot[nl.VarSlots.Length];
                            for (int s = 0; s < nl.VarSlots.Length; s++)
                                offsetSlots[s] = new VariableSlot(
                                    nl.VarSlots[s].Name,
                                    nl.VarSlots[s].SlotIndex + cumImm);
                            nl.VarSlots = offsetSlots;
                        }
                        links.Add(nl);
                    }

                    // 展平嵌套 overrides——GlobalSlot 偏移 cumImm
                    for (int no = 0; no < nestedOverrides.Length; no++)
                    {
                        var nov = nestedOverrides[no];
                        ovrds.Add(new VffOverride<TData>(
                            nov.GlobalSlot + cumImm, nov.Kind, nov.ConstantValue));
                    }

                    cumImm += nestedImm;
                    visited.Remove(entry.Hash); // 允许 DAG 中不同分支共享同一 VFF
                }
                else
                {
                    // ── 普通公式 link：构建 ChainLink ──
                    var fHeader = FormulaFormat.ReadHeader(fBytes);
                    var instSpan = FormulaFormat.GetInstructionSpan(fBytes);
                    var bytecode = instSpan.ToArray();

                    var varSlots = FormulaFormat.ReadVariableSlots(fBytes, baseSlotOffset: cumImm);

                    links.Add(new ChainLink
                    {
                        Key              = entry.Hash,
                        Bytecode         = bytecode,
                        InstructionCount = bytecode.Length,
                        Type             = (FluxType)entry.Type,
                        ImmediateCount   = entry.ImmCount,
                        VarSlots         = varSlots,
                        MaxRegister      = fHeader.MaxRegister,
                    });

                    cumImm += entry.ImmCount;
                }
            }

            // ── 解析当前 VFF 的 Override Table ──
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

            // 合并当前 VFF 自身的 overrides 和递归展平的 overrides
            var allOverrides = new VffOverride<TData>[overrideCount + ovrds.Count];
            if (overrideCount > 0)
                Array.Copy(overrides, 0, allOverrides, 0, overrideCount);
            if (ovrds.Count > 0)
                ovrds.CopyTo(allOverrides, overrideCount);

            return (links.ToArray(), allOverrides, cumImm);
        }
    }
}
