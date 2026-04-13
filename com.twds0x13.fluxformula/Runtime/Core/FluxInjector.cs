using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 数据注入器 (统一支持普通模式与JIT模式)
    /// </summary>
    internal readonly struct FluxInjector<TData>
        where TData : unmanaged
    {
        private readonly Instruction[] _buffer;
        private readonly int[] _offsets;
        private readonly int _slotsPerData;

        // 变量查找：并行数组，无 Dictionary/List，零碎片
        private readonly string[] _varNames;       // 唯一变量名
        private readonly int[][] _varSlotIndexes;  // 对应槽位索引组
        private readonly int _varCount;

        public FluxInjector(Instruction[] buffer)
        {
            _buffer = buffer;
            _offsets = null;
            _varNames = null;
            _varSlotIndexes = null;
            _varCount = 0;
            unsafe
            {
                _slotsPerData = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            }
        }

        internal FluxInjector(Instruction[] buffer, int[] offsets, VariableSlot[] varSlots = null)
        {
            _buffer = buffer;
            _offsets = offsets;
            unsafe
            {
                _slotsPerData = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            }

            if (varSlots != null && varSlots.Length > 0)
            {
                // ── 去重分组：同名变量归入同一槽位索引组 ──
                int uniqueCount = 0;
                for (int i = 0; i < varSlots.Length; i++)
                {
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (varSlots[j].Name == varSlots[i].Name)
                        { seen = true; break; }
                    }
                    if (!seen) uniqueCount++;
                }

                _varNames = new string[uniqueCount];
                _varSlotIndexes = new int[uniqueCount][];
                int groupIdx = 0;

                for (int i = 0; i < varSlots.Length; i++)
                {
                    bool alreadyGrouped = false;
                    for (int g = 0; g < groupIdx; g++)
                    {
                        if (_varNames[g] == varSlots[i].Name)
                        { alreadyGrouped = true; break; }
                    }
                    if (alreadyGrouped) continue;

                    int cnt = 1;
                    for (int j = i + 1; j < varSlots.Length; j++)
                        if (varSlots[j].Name == varSlots[i].Name) cnt++;

                    int[] slots = new int[cnt];
                    int si = 0;
                    for (int j = i; j < varSlots.Length; j++)
                        if (varSlots[j].Name == varSlots[i].Name)
                            slots[si++] = varSlots[j].SlotIndex;

                    _varNames[groupIdx] = varSlots[i].Name;
                    _varSlotIndexes[groupIdx] = slots;
                    groupIdx++;
                }

                // ── 按变量名排序：为二分查找准备 ──
                Array.Sort(_varNames, _varSlotIndexes, 0, uniqueCount, StringComparer.Ordinal);
                _varCount = uniqueCount;
            }
            else
            {
                _varNames = null;
                _varSlotIndexes = null;
                _varCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly FluxInjector<TData> SetIndex(int paramIndex, TData value)
        {
            int offset;
            if (_offsets == null)
            {
                offset = paramIndex * _slotsPerData;
                if (offset + _slotsPerData > _buffer.Length)
                    throw new IndexOutOfRangeException(
                        $"Parameter index {paramIndex} is out of bounds."
                    );
            }
            else
            {
                if (paramIndex < 0 || paramIndex >= _offsets.Length)
                    throw new IndexOutOfRangeException(
                        $"Parameter index {paramIndex} is out of bounds."
                    );
                offset = _offsets[paramIndex];
            }

            unsafe
            {
                fixed (Instruction* pBase = _buffer)
                {
                    *(TData*)(pBase + offset) = value;
                }
            }
            return this;
        }

        /// <summary>按变量名安全注入。同名变量（如 [x]+[x]）全部槽位一起写入。
        /// 名称不存在则抛 ArgumentException。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly FluxInjector<TData> Set(string name, TData value)
        {
            // 内联二分查找：直接 CompareOrdinal，避掉 IComparer<T> 虚调用
            int lo = 0, hi = _varCount - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                int cmp = string.CompareOrdinal(_varNames[mid], name);
                if (cmp == 0)
                {
                    int[] slotIndexes = _varSlotIndexes[mid];
                    unsafe
                    {
                        fixed (Instruction* pBase = _buffer)
                        {
                            for (int i = 0; i < slotIndexes.Length; i++)
                            {
                                int offset = _offsets != null
                                    ? _offsets[slotIndexes[i]]
                                    : slotIndexes[i] * _slotsPerData;
                                *(TData*)(pBase + offset) = value;
                            }
                        }
                    }
                    return this;
                }
                if (cmp < 0) lo = mid + 1;
                else         hi = mid - 1;
            }

            throw new ArgumentException(
                $"Variable '{name}' is not defined in this formula.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Instruction[] GetBuffer() => _buffer;

        public override readonly string ToString()
        {
            return $"FluxInjector<{typeof(TData).Name}>";
        }
    }
}
