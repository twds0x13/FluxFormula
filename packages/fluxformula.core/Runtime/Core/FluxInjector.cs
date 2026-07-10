using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 数据注入器：支持按位置 (SetIndex) 和按变量名 (Set) 注入，同时维护值数组支持回读。
    /// </summary>
    internal readonly struct FluxInjector<TData>
        where TData : unmanaged
    {
        private readonly Instruction[] _buffer;
        private readonly int[] _offsets;
        private readonly int _slotsPerData;

        // 变量查找：并行数组，二分查找，零 GC
        private readonly string[] _varNames;
        private readonly int[][] _varSlotIndexes;
        private readonly int _varCount;

        // 值回读数组：按 SlotIndex 索引，Set/SetIndex 写入，GetValue 读取
        private readonly TData[] _values;

        // ── 构造 ──

        internal FluxInjector(Instruction[] buffer)
        {
            _buffer    = buffer;
            _offsets   = null;
            _varNames  = null;
            _varSlotIndexes = null;
            _varCount  = 0;
            _values    = null;
            _slotsPerData = FormulaFormat.DataSlots<TData>();
        }

        internal FluxInjector(Instruction[] buffer, int[] offsets, VariableSlot[] varSlots = null)
        {
            _buffer  = buffer;
            _offsets = offsets;
            _slotsPerData = FormulaFormat.DataSlots<TData>();

            if (varSlots != null && varSlots.Length > 0)
            {
                // ── 值回读数组：SlotIndex → 值 ──
                int maxSlot = 0;
                for (int i = 0; i < varSlots.Length; i++)
                    if (varSlots[i].SlotIndex > maxSlot)
                        maxSlot = varSlots[i].SlotIndex;
                _values = new TData[maxSlot + 1];

                // ── 去重分组 ──
                int uniqueCount = 0;
                for (int i = 0; i < varSlots.Length; i++)
                {
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                        if (varSlots[j].Name == varSlots[i].Name) { seen = true; break; }
                    if (!seen) uniqueCount++;
                }

                _varNames      = new string[uniqueCount];
                _varSlotIndexes = new int[uniqueCount][];
                int groupIdx = 0;

                for (int i = 0; i < varSlots.Length; i++)
                {
                    bool alreadyGrouped = false;
                    for (int g = 0; g < groupIdx; g++)
                        if (_varNames[g] == varSlots[i].Name) { alreadyGrouped = true; break; }
                    if (alreadyGrouped) continue;

                    int cnt = 1;
                    for (int j = i + 1; j < varSlots.Length; j++)
                        if (varSlots[j].Name == varSlots[i].Name) cnt++;

                    int[] slots = new int[cnt];
                    int si = 0;
                    for (int j = i; j < varSlots.Length; j++)
                        if (varSlots[j].Name == varSlots[i].Name)
                            slots[si++] = varSlots[j].SlotIndex;

                    _varNames[groupIdx]      = varSlots[i].Name;
                    _varSlotIndexes[groupIdx] = slots;
                    groupIdx++;
                }

                Array.Sort(_varNames, _varSlotIndexes, 0, uniqueCount, StringComparer.Ordinal);
                _varCount = uniqueCount;
            }
            else
            {
                _values         = null;
                _varNames       = null;
                _varSlotIndexes = null;
                _varCount       = 0;
            }
        }

        // ── 注入 ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly FluxInjector<TData> SetIndex(int paramIndex, TData value)
        {
            // 值回读
            if (_values != null && paramIndex < _values.Length)
                _values[paramIndex] = value;

            int offset;
            if (_offsets == null)
            {
                offset = paramIndex * _slotsPerData;
                if (offset + _slotsPerData > _buffer.Length)
                    throw new IndexOutOfRangeException(
                        $"Parameter index {paramIndex} is out of bounds.");
            }
            else
            {
                if (paramIndex < 0 || paramIndex >= _offsets.Length)
                    throw new IndexOutOfRangeException(
                        $"Parameter index {paramIndex} is out of bounds.");
                offset = _offsets[paramIndex];
            }

            unsafe
            {
                fixed (Instruction* pBase = _buffer)
                    *(TData*)(pBase + offset) = value;
            }
            return this;
        }

        /// <summary>按变量名安全注入。同名变量全部槽位一起写入。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly FluxInjector<TData> Set(string name, TData value)
        {
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
                                int si = slotIndexes[i];
                                // 值回读
                                if (_values != null && si < _values.Length)
                                    _values[si] = value;

                                int offset = _offsets != null
                                    ? _offsets[si]
                                    : si * _slotsPerData;
                                *(TData*)(pBase + offset) = value;
                            }
                        }
                    }
                    return this;
                }
                if (cmp < 0) lo = mid + 1;
                else         hi = mid - 1;
            }

            throw new ArgumentException($"Variable '{name}' is not defined in this formula.");
        }

        /// <summary>按变量名安全注入。同名变量全部槽位一起写入。变量名不存在时静默跳过。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly FluxInjector<TData> TrySet(string name, TData value)
        {
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
                                int si = slotIndexes[i];
                                if (_values != null && si < _values.Length)
                                    _values[si] = value;

                                int offset = _offsets != null
                                    ? _offsets[si]
                                    : si * _slotsPerData;
                                *(TData*)(pBase + offset) = value;
                            }
                        }
                    }
                    return this;
                }
                if (cmp < 0) lo = mid + 1;
                else         hi = mid - 1;
            }

            // 变量名不存在：静默跳过
            return this;
        }

        // ── 回读 ──

        /// <summary>
        /// 按 SlotIndex 读取已注入的值。仅用于链式求值的 per-link buffer 构建。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly TData GetValue(int slotIndex)
        {
            if (_values != null && (uint)slotIndex < (uint)_values.Length)
                return _values[slotIndex];
            return default;
        }

        // ── 其他 ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly Instruction[] GetBuffer() => _buffer;

        public override readonly string ToString() =>
            $"FluxInjector<{typeof(TData).Name}>";
    }
}
