using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 数据注入器 (统一支持普通模式与JIT模式)
    /// </summary>
    public struct FluxBinder<TData>
        where TData : unmanaged
    {
        internal readonly Instruction[] Buffer;
        private readonly int[] _offsets;
        private readonly int _slotsPerData;
        private int _cursor;

        public FluxBinder(Instruction[] payload)
        {
            Buffer = payload;
            _offsets = null;
            unsafe
            {
                _slotsPerData = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            }
            _cursor = 0;
        }

        internal FluxBinder(Instruction[] buffer, int[] offsets)
        {
            Buffer = buffer;
            _offsets = offsets;
            unsafe
            {
                _slotsPerData = (sizeof(TData) + sizeof(Instruction) - 1) / sizeof(Instruction);
            }
            _cursor = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly FluxBinder<TData> Inject(int paramIndex, TData value)
        {
            int offset;
            if (_offsets == null)
            {
                offset = paramIndex * _slotsPerData;
                if (offset + _slotsPerData > Buffer.Length)
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
                fixed (Instruction* pBase = Buffer)
                {
                    *(TData*)(pBase + offset) = value;
                }
            }
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxBinder<TData> InjectNext(TData value)
        {
            Inject(_cursor, value);
            _cursor++;
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FluxBinder<TData> Seek(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            _cursor = index;
            return this;
        }

        public override readonly string ToString()
        {
            int maxParams = _offsets?.Length ?? (Buffer.Length / _slotsPerData);
            return $"FluxBinder<{typeof(TData).Name}> [Cursor: {_cursor}/{maxParams}]";
        }
    }
}
