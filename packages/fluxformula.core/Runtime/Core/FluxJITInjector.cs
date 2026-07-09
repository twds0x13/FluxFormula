using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// JIT 热路径专用注入器：仅持有 payload buffer 和步长，零分支 SetIndex。
    /// 不含名称查找、值回读、偏移表——这些由 <see cref="FluxInjector{TData}"/> 负责。
    /// </summary>
    internal readonly struct FluxJITInjector<TData>
        where TData : unmanaged
    {
        private readonly Instruction[] _buffer;
        private readonly int _slotsPerData;

        internal FluxJITInjector(Instruction[] buffer)
        {
            _buffer = buffer;
            _slotsPerData = FormulaFormat.DataSlots<TData>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly FluxJITInjector<TData> SetIndex(int paramIndex, TData value)
        {
            int offset = paramIndex * _slotsPerData;
            unsafe
            {
                fixed (Instruction* pBase = _buffer)
                    *(TData*)(pBase + offset) = value;
            }
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly Instruction[] GetBuffer() => _buffer;
    }
}
