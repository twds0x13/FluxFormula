using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace FluxFormula.Core
{
    /// <summary>
    /// WAL entry 的固定 20 字节头。
    /// </summary>
    public struct WALEntryHeader
    {
        public DualHash64 FormulaHash;
        public ushort BindingCount;
        public ushort MetaLen;

        public const int Size = 20;

        public WALEntryHeader(DualHash64 formulaHash, ushort bindingCount, ushort metaLen)
            => (FormulaHash, BindingCount, MetaLen) = (formulaHash, bindingCount, metaLen);

        public static void Write(byte[] buf, ref int offset,
            DualHash64 formulaHash, ushort bindingCount, ushort metaLen)
        {
            BinaryFormat.WriteUInt64LE(buf, ref offset, formulaHash.XxHash64);
            BinaryFormat.WriteUInt64LE(buf, ref offset, formulaHash.FnvHash64);
            BinaryFormat.WriteUInt16LE(buf, ref offset, bindingCount);
            BinaryFormat.WriteUInt16LE(buf, ref offset, metaLen);
        }

        public static WALEntryHeader Read(ReadOnlySpan<byte> data, ref int offset)
        {
            ulong xxh = BinaryFormat.ReadUInt64LE(data, ref offset);
            ulong fnv = BinaryFormat.ReadUInt64LE(data, ref offset);
            ushort count = BinaryFormat.ReadUInt16LE(data, ref offset);
            ushort metaLen = BinaryFormat.ReadUInt16LE(data, ref offset);
            return new WALEntryHeader(new DualHash64(xxh, fnv), count, metaLen);
        }
    }

    /// <summary>
    /// WAL entry 的只读视图，不拥有内存。
    /// </summary>
    /// <typeparam name="TMeta">非托管用户自定义元数据结构体，存储于 entry 尾部</typeparam>
    public readonly struct WALEntry<TMeta> where TMeta : unmanaged
    {
        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _length;

        public WALEntry(byte[] buffer, int offset, int length)
            => (_buffer, _offset, _length) = (buffer, offset, length);

        /// <summary>公式字节码的内容哈希。</summary>
        public DualHash64 FormulaHash
        {
            get
            {
                int off = _offset;
                return WALEntryHeader.Read(_buffer, ref off).FormulaHash;
            }
        }

        /// <summary>绑定数量。</summary>
        public ushort BindingCount
        {
            get
            {
                int off = _offset;
                return WALEntryHeader.Read(_buffer, ref off).BindingCount;
            }
        }

        /// <summary>用户定义元数据，从 entry 尾部直接读取。</summary>
        public TMeta Meta
        {
            get
            {
                int tailOffset = _offset + _length - Unsafe.SizeOf<TMeta>();
                return Unsafe.ReadUnaligned<TMeta>(ref _buffer[tailOffset]);
            }
        }

        /// <summary>遍历绑定对。</summary>
        public WALBindingEnumerator GetBindings()
            => new(_buffer, _offset, BindingCount);
    }

    /// <summary>
    /// 栈分配绑定枚举器。
    /// </summary>
    public ref struct WALBindingEnumerator
    {
        private readonly byte[] _data;
        private readonly int _baseOffset;
        private readonly ushort _bindingCount;
        private int _index;
        private int _readOffset;

        internal WALBindingEnumerator(byte[] data, int baseOffset, ushort bindingCount)
        {
            _data = data;
            _baseOffset = baseOffset;
            _bindingCount = bindingCount;
            _index = 0;
            _readOffset = baseOffset + WALEntryHeader.Size;
        }

        public bool MoveNext()
        {
            if (_index >= _bindingCount) return false;
            _index++;
            return true;
        }

        public (string Name, byte[] Value) Current
        {
            get
            {
                int off = _readOffset;
                ushort nameLen = BinaryFormat.ReadUInt16LE(_data, ref off);
                string name = Encoding.UTF8.GetString(_data, off, nameLen);
                off += nameLen;
                ushort valueLen = BinaryFormat.ReadUInt16LE(_data, ref off);
                byte[] value = new byte[valueLen];
                Array.Copy(_data, off, value, 0, valueLen);
                off += valueLen;
                _readOffset = off;
                return (name, value);
            }
        }

        public WALBindingEnumerator GetEnumerator() => this;
    }
}
