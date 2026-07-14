using System;

namespace FluxFormula.Core
{
    public readonly partial struct FluxCurryEvaluator<TData, TDef>
    {
        // ── 格式常量 ──

        private static readonly byte[] Magic = { (byte)'C', (byte)'U', (byte)'R', (byte)'Y' };
        private const byte Version = 1;

        /// <summary>固定头大小（字节）：Magic(4) + Version(1) + DualHash64(16) + _ip(4) + _boundCount(4) + VariableCount(4) + _completed(1) + RegsLength(1)</summary>
        private const int HeaderSize = 35;

        // ── 公共 API ──

        /// <summary>
        /// 将当前 evaluator 的状态序列化为字节数组（"Record"）。
        /// 字节码不包含在内，仅存储 <see cref="DualHash64"/> 引用。
        /// 反序列化端通过 <see cref="FormulaCache"/> 按哈希查找字节码。
        /// </summary>
        public byte[] ToRecord()
        {
            int dataSize;
            unsafe { dataSize = sizeof(TData); }

            int varCount = _varImmIndices.Length;
            int maskBytes = (varCount + 7) / 8;
            int regsLen = _regs.Length;

            int resultBytes = _completed ? dataSize : 0;
            int totalSize = HeaderSize
                          + maskBytes
                          + varCount * dataSize
                          + regsLen * dataSize
                          + resultBytes;

            var buf = new byte[totalSize];
            int offset = 0;

            // Magic
            buf[offset++] = Magic[0];
            buf[offset++] = Magic[1];
            buf[offset++] = Magic[2];
            buf[offset++] = Magic[3];

            // Version
            buf[offset++] = Version;

            // DualHash64
            BinaryFormat.WriteInt64LE(buf, ref offset, (long)_byteHash.XxHash64);
            BinaryFormat.WriteInt64LE(buf, ref offset, (long)_byteHash.FnvHash64);

            // _ip
            BinaryFormat.WriteInt32LE(buf, ref offset, _ip);

            // _boundCount
            BinaryFormat.WriteInt32LE(buf, ref offset, _boundCount);

            // VariableCount
            BinaryFormat.WriteInt32LE(buf, ref offset, varCount);

            // _completed
            buf[offset++] = _completed ? (byte)1 : (byte)0;

            // RegsLength
            buf[offset++] = (byte)regsLen;

            // _boundMask bitfield: bit 0 = _boundMask[0], LSB first
            for (int b = 0; b < maskBytes; b++)
            {
                byte val = 0;
                for (int bit = 0; bit < 8 && b * 8 + bit < varCount; bit++)
                    if (_boundMask[b * 8 + bit])
                        val |= (byte)(1 << bit);
                buf[offset++] = val;
            }

            // _boundValues（全量槽位）
            unsafe
            {
                fixed (TData* p = _boundValues)
                {
                    var span = new ReadOnlySpan<byte>(p, varCount * dataSize);
                    span.CopyTo(buf.AsSpan(offset));
                    offset += span.Length;
                }
            }

            // _regs
            unsafe
            {
                fixed (TData* p = _regs)
                {
                    var span = new ReadOnlySpan<byte>(p, regsLen * dataSize);
                    span.CopyTo(buf.AsSpan(offset));
                    offset += span.Length;
                }
            }

            // _result（仅 completed）
            if (_completed)
            {
                unsafe
                {
                    fixed (TData* p = &_result)
                    {
                        var span = new ReadOnlySpan<byte>(p, dataSize);
                        span.CopyTo(buf.AsSpan(offset));
                    }
                }
            }

            return buf;
        }

        /// <summary>
        /// 从 Record 字节数组反序列化，重建 evaluator 状态。
        /// 字节码按 Record 中存储的 <see cref="DualHash64"/> 从 <see cref="FormulaCache"/> 查找。
        /// </summary>
        /// <param name="data">由 <see cref="ToRecord"/> 生成的字节数组</param>
        /// <param name="definition">TDef 定义体（必须与原始字节码匹配；不匹配为未定义行为）</param>
        /// <exception cref="FormatException">magic 或版本不匹配</exception>
        /// <exception cref="InvalidOperationException">字节码未在 FormulaCache 中找到</exception>
        public static FluxCurryEvaluator<TData, TDef> FromRecord(
            ReadOnlySpan<byte> data, TDef definition)
        {
            int dataSize;
            unsafe { dataSize = sizeof(TData); }

            int offset = 0;

            // ── Magic ──
            if (data.Length < 4
                || data[0] != Magic[0] || data[1] != Magic[1]
                || data[2] != Magic[2] || data[3] != Magic[3])
                throw new FormatException("Not a valid CurryEvaluator record (magic mismatch).");

            offset = 4;

            // ── Version ──
            byte version = data[offset++];
            if (version != Version)
                throw new FormatException(
                    $"Unsupported record version: {version}. Expected: {Version}.");

            // ── DualHash64 ──
            ulong xxHash = BinaryFormat.ReadUInt64LE(data, offset); offset += 8;
            ulong fnvHash = BinaryFormat.ReadUInt64LE(data, offset); offset += 8;
            var hash = new DualHash64(xxHash, fnvHash);

            // ── 从 FormulaCache 查找字节码 ──
            if (!FormulaCache.Instance.TryGet(hash, out IntPtr bytecodePtr, out int bytecodeLen))
                throw new InvalidOperationException(
                    $"Formula bytecode not found in FormulaCache for hash: {hash}. " +
                    "Load the formula bytes into FormulaCache before deserializing.");

            ReadOnlySpan<byte> bytecodeBytes;
            unsafe { bytecodeBytes = new ReadOnlySpan<byte>((void*)bytecodePtr, bytecodeLen); }

            var header = FormulaFormat.ReadHeader(bytecodeBytes);
            var instSpan = FormulaFormat.GetInstructionSpan(bytecodeBytes);
            var varSlots = FormulaFormat.ReadVariableSlots(bytecodeBytes, baseSlotOffset: 0);

            // ── _ip ──
            int ip = BinaryFormat.ReadInt32LE(data, ref offset);

            // ── _boundCount ──
            int boundCount = BinaryFormat.ReadInt32LE(data, ref offset);

            // ── VariableCount ──
            int varCount = BinaryFormat.ReadInt32LE(data, ref offset);

            // ── _completed ──
            bool completed = data[offset++] != 0;

            // ── RegsLength ──
            int regsLen = data[offset++];

            // ── _boundMask bitfield ──
            int maskBytes = (varCount + 7) / 8;
            var boundMask = new bool[varCount];
            for (int b = 0; b < maskBytes; b++)
            {
                byte val = data[offset++];
                for (int bit = 0; bit < 8 && b * 8 + bit < varCount; bit++)
                    boundMask[b * 8 + bit] = (val & (1 << bit)) != 0;
            }

            // ── _boundValues ──
            var boundValues = new TData[varCount];
            if (varCount > 0)
            {
                unsafe
                {
                    fixed (TData* p = boundValues)
                    {
                        var span = new Span<byte>(p, varCount * dataSize);
                        data.Slice(offset, span.Length).CopyTo(span);
                        offset += span.Length;
                    }
                }
            }

            // ── _regs ──
            var regs = new TData[regsLen];
            {
                unsafe
                {
                    fixed (TData* p = regs)
                    {
                        var span = new Span<byte>(p, regsLen * dataSize);
                        data.Slice(offset, span.Length).CopyTo(span);
                        offset += span.Length;
                    }
                }
            }

            // ── _result（conditionally present）──
            TData result = default;
            if (completed)
            {
                unsafe
                {
                    result = System.Runtime.InteropServices.MemoryMarshal.Read<TData>(
                        data.Slice(offset));
                }
            }

            // ── 从字节码重建不可变字段 ──
            var bytecode = instSpan.ToArray();
            int immCount = header.ImmediateCount;
            int instrCount = header.Count;
            byte maxRegister = header.MaxRegister;

            var varImmIndices = new int[varCount];
            var varNames = new string[varCount];
            for (int i = 0; i < varCount; i++)
            {
                varImmIndices[i] = varSlots[i].SlotIndex;
                varNames[i] = varSlots[i].Name;
            }

            // ── 构造 ──
            return new FluxCurryEvaluator<TData, TDef>(
                definition,
                bytecode,
                varImmIndices,
                varNames,
                immCount,
                instrCount,
                maxRegister,
                hash,
                regs,
                boundValues,
                boundMask,
                boundCount,
                ip,
                completed,
                result);
        }

        /// <summary>
        /// 从 Record 字节数组反序列化（便利重载）。
        /// </summary>
        public static FluxCurryEvaluator<TData, TDef> FromRecord(
            byte[] data, TDef definition)
        {
            return FromRecord((ReadOnlySpan<byte>)data, definition);
        }
    }
}
