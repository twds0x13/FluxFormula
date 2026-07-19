using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace FluxFormula.Core
{
    internal unsafe struct NativeTruncateBuffer<T> : IDisposable where T : unmanaged
    {
        private void* _data;
        private uint* _truncations;
        private nuint _dataCapacity;
        private uint _pos;
        private uint _truncCount;
        private uint _truncCapacity;
        private bool _disposed;
        private SpinLock _sync;

        public NativeTruncateBuffer(nuint initialCapacity, uint maxTruncations)
        {
            _data = (void*)Marshal.AllocHGlobal((nint)initialCapacity);
            _truncations = (uint*)(void*)Marshal.AllocHGlobal((nint)(maxTruncations * (nuint)sizeof(uint)));
            _dataCapacity = initialCapacity;
            _truncCapacity = maxTruncations;
            _pos = 0;
            _truncCount = 0;
            _disposed = false;
            _sync = new SpinLock(Debugger.IsAttached);
        }

        public uint TruncationCount
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _sync.Enter(ref lockTaken);
                    return _truncCount;
                }
                finally
                {
                    if (lockTaken) _sync.Exit();
                }
            }
        }

        public nuint ByteLength
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _sync.Enter(ref lockTaken);
                    return (nuint)_pos * (nuint)sizeof(T);
                }
                finally
                {
                    if (lockTaken) _sync.Exit();
                }
            }
        }

        // ── 写操作 ──

        public void Append(T[] source) => Append(new ReadOnlySpan<T>(source));

        public void Append(ReadOnlySpan<T> source)
        {
            if (source.IsEmpty) return;
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (_disposed) throw new ObjectDisposedException(nameof(NativeTruncateBuffer<T>));

                nuint needed = ((nuint)_pos + (nuint)source.Length) * (nuint)sizeof(T);
                if (needed > _dataCapacity)
                    ExpandData(needed);

                source.CopyTo(new Span<T>((T*)_data + _pos, source.Length));
                _pos += (uint)source.Length;
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        public void AppendTruncate(T[] source) => AppendTruncate(new ReadOnlySpan<T>(source));

        public void AppendTruncate(ReadOnlySpan<T> source)
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (_disposed) throw new ObjectDisposedException(nameof(NativeTruncateBuffer<T>));

                if (!source.IsEmpty)
                {
                    nuint needed = ((nuint)_pos + (nuint)source.Length) * (nuint)sizeof(T);
                    if (needed > _dataCapacity)
                        ExpandData(needed);
                    source.CopyTo(new Span<T>((T*)_data + _pos, source.Length));
                    _pos += (uint)source.Length;
                }

                if (_truncCount == _truncCapacity)
                    ExpandTruncations();
                _truncations[_truncCount++] = _pos;
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        public void Truncate()
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (_disposed) throw new ObjectDisposedException(nameof(NativeTruncateBuffer<T>));

                if (_truncCount == _truncCapacity)
                    ExpandTruncations();
                _truncations[_truncCount++] = _pos;
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        // ── 读操作 ──

        public ReadOnlySpan<T> ReadFrom(uint truncIndex)
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (truncIndex == 0 || truncIndex > _truncCount)
                    throw new ArgumentOutOfRangeException(nameof(truncIndex));

                int start = truncIndex == 1 ? 0 : (int)_truncations[truncIndex - 2];
                int end = (int)_truncations[truncIndex - 1];
                return new ReadOnlySpan<T>((T*)_data + start, end - start);
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        // ── 状态管理 ──

        public void RevertTo(uint truncIndex)
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (truncIndex > _truncCount)
                    throw new ArgumentOutOfRangeException(nameof(truncIndex));
                if (truncIndex == 0)
                {
                    _pos = 0;
                    _truncCount = 0;
                }
                else
                {
                    _truncCount = truncIndex;
                    _pos = _truncations[truncIndex - 1];
                }
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        public void Clear()
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                _pos = 0;
                _truncCount = 0;
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        public void Dispose()
        {
            bool lockTaken = false;
            try
            {
                _sync.Enter(ref lockTaken);
                if (_disposed) return;
                _disposed = true;
                Marshal.FreeHGlobal((nint)_data);
                Marshal.FreeHGlobal((nint)_truncations);
            }
            finally
            {
                if (lockTaken) _sync.Exit();
            }
        }

        // ── 内部 ──

        private void ExpandData(nuint needed)
        {
            nuint newSize = _dataCapacity;
            while (newSize < needed)
                newSize *= 4;
            _data = (void*)Marshal.ReAllocHGlobal((nint)_data, (nint)newSize);
            _dataCapacity = newSize;
        }

        private void ExpandTruncations()
        {
            _truncCapacity *= 4;
            _truncations = (uint*)(void*)Marshal.ReAllocHGlobal(
                (nint)_truncations, (nint)(_truncCapacity * (nuint)sizeof(uint)));
        }
    }
}
