using System;
using System.Collections.Generic;

using System.Text;
using System.Threading;

namespace FluxFormula.Core
{
    /// <summary>
    /// Write-Ahead Log 核心引擎（非泛型，操作原始字节）。
    /// 条目通过 <see cref="Append"/> 累积到内部缓冲区中，
    /// <see cref="CommitFrame"/> 时刷盘。
    /// </summary>
    public class FluxWAL : IDisposable
    {
        private readonly IWALStorage _storage;
        private NativeTruncateBuffer<byte> _pending;
        private readonly List<FramePtr> _frameTable;
        private readonly object _flushLock;
        private readonly object _frameLock;

        private long _totalBytes;
        private int _lastFlushPreambleLen = -1;
        private FramePtr _lastFramePtr;
        private byte[] _checkpointBlob;
        private FramePtr _checkpointLsn;
        private bool _disposed;

        public FluxWAL(string directoryPath, int pendingCapacity = 262144, int maxTruncations = 4096)
            : this(new WALFileStorage(directoryPath), pendingCapacity, maxTruncations) { }

        public FluxWAL(IWALStorage storage, int pendingCapacity = 262144, int maxTruncations = 4096)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _pending = new NativeTruncateBuffer<byte>((nuint)pendingCapacity, (uint)maxTruncations);
            _frameTable = new List<FramePtr>();
            _flushLock = new object();
            _frameLock = new object();

            _frameTable.Add(new FramePtr(0));
            _lastFramePtr = new FramePtr(0);

            if (_storage.Exists)
                Recover();
        }

        private void Recover()
        {
            try
            {
                byte[] fileData = _storage.ReadAll();
                if (fileData.Length < WALFormat.HeaderSize) return;

                if (!WALFormat.TryParseHeader(fileData, out WALHeader header))
                    return;

                if (header.Version != WALFormat.Version)
                    return;

                // Validate frame table bounds
                if (header.FrameCount > 100_000) return;
                long frameTableEnd = (long)header.FrameTableOffset
                    + (long)header.FrameCount * WALFormat.FramePtrSize;
                if (frameTableEnd > fileData.Length) return;

                // Validate checkpoint bounds
                if (header.HasCheckpoint)
                {
                    long checkpointEnd = (long)header.CheckpointOffset
                        + (long)header.CheckpointLength;
                    if (checkpointEnd > fileData.Length) return;
                }

                _frameTable.Clear();
                for (int i = 0; i < header.FrameCount; i++)
                    _frameTable.Add(WALFormat.ReadFramePtr(
                        fileData, (int)header.FrameTableOffset, i));

                if (_frameTable.Count > 0)
                    _lastFramePtr = _frameTable[_frameTable.Count - 1];

                if (header.HasCheckpoint)
                {
                    _checkpointBlob = new byte[header.CheckpointLength];
                    Array.Copy(fileData, (int)header.CheckpointOffset,
                        _checkpointBlob, 0, header.CheckpointLength);
                }

                ulong lastValidLsn = _lastFramePtr.Lsn;
                long entryStart = (long)header.CheckpointOffset
                    + (long)header.CheckpointLength;
                long entryDataEnd = entryStart + (long)lastValidLsn;
                if (entryDataEnd <= fileData.Length)
                    _totalBytes = (long)lastValidLsn;
            }
            catch
            {
                // Corrupt file — reset to clean state
                _frameTable.Clear();
                _frameTable.Add(new FramePtr(0));
                _lastFramePtr = new FramePtr(0);
                _checkpointBlob = null;
                _checkpointLsn = default;
                _totalBytes = 0;
            }
        }

        // ── 属性 ──

        public FramePtr CurrentLsn => new((ulong)Interlocked.Read(ref _totalBytes));
        public int FrameCount { get { lock (_frameLock) return _frameTable.Count; } }
        public bool HasCheckpoint => _checkpointBlob != null;

        public FramePtr CheckpointLsn => _checkpointLsn;

        // ── Append ──

        public FramePtr Append(
            DualHash64 formulaHash,
            (string Name, byte[] Value)[] bindings,
            byte[] meta)
        {
            byte[] serialized = SerializeEntry(formulaHash, bindings, meta);
            long reserved = Interlocked.Add(ref _totalBytes, serialized.Length);
            long myStart = reserved - serialized.Length;

            lock (_flushLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FluxWAL));
                _pending.AppendTruncate(serialized);
            }

            return new FramePtr((ulong)myStart);
        }

        internal static byte[] SerializeEntry(
            DualHash64 formulaHash,
            (string Name, byte[] Value)[] bindings,
            byte[] meta)
        {
            int entrySize = WALEntryHeader.Size + meta.Length;
            for (int i = 0; i < bindings.Length; i++)
                entrySize += 2 + Encoding.UTF8.GetByteCount(bindings[i].Name) + 2 + bindings[i].Value.Length;
            byte[] buf = new byte[entrySize];
            int off = 0;

            WALEntryHeader.Write(buf, ref off, formulaHash, (ushort)bindings.Length, (ushort)meta.Length);

            for (int i = 0; i < bindings.Length; i++)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(bindings[i].Name);
                BinaryFormat.WriteUInt16LE(buf, ref off, (ushort)nameBytes.Length);
                Array.Copy(nameBytes, 0, buf, off, nameBytes.Length); off += nameBytes.Length;
                BinaryFormat.WriteUInt16LE(buf, ref off, (ushort)bindings[i].Value.Length);
                Array.Copy(bindings[i].Value, 0, buf, off, bindings[i].Value.Length); off += bindings[i].Value.Length;
            }

            if (meta.Length > 0)
            {
                Array.Copy(meta, 0, buf, off, meta.Length);
                off += meta.Length;
            }

            return buf;
        }

        // ── 帧管理 ──

        public int CommitFrame()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FluxWAL));
            int newIndex;
            lock (_frameLock)
            {
                FramePtr current = CurrentLsn;
                if (current.Lsn <= _lastFramePtr.Lsn)
                    return -1;
                _frameTable.Add(current);
                _lastFramePtr = current;
                newIndex = _frameTable.Count - 1;
            }

            return newIndex;
        }

        public FramePtr GetFramePtr(int frameIndex)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FluxWAL));
            lock (_frameLock)
            {
                if (frameIndex < 0 || frameIndex >= _frameTable.Count)
                    throw new ArgumentOutOfRangeException(nameof(frameIndex));
                return _frameTable[frameIndex];
            }
        }

        public bool TryGetFrameRange(int frameIndex, out FramePtr start, out FramePtr end)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FluxWAL));
            lock (_frameLock)
            {
                if (frameIndex <= 0 || frameIndex >= _frameTable.Count)
                { start = end = default; return false; }
                start = _frameTable[frameIndex - 1];
                end = _frameTable[frameIndex];
                return true;
            }
        }

        // ── Checkpoint ──

        public void Checkpoint(byte[] checkpointRecord)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FluxWAL));
            if (checkpointRecord == null)
                throw new ArgumentNullException(nameof(checkpointRecord));

            Flush();
            _checkpointBlob = checkpointRecord;
            _checkpointLsn = CurrentLsn;
            WriteFullWalFile();
        }

        public byte[] ReadCheckpoint() => _checkpointBlob;

        // ── 重放 ──

        internal IEnumerable<(byte[] RawEntry, FramePtr Lsn)> EnumerateEntries(FramePtr afterLsn)
        {
            Flush();

            if (!_storage.Exists) yield break;

            byte[] fileData = _storage.ReadAll();
            if (!WALFormat.TryParseHeader(fileData, out WALHeader walHeader)) yield break;

            long entryStart = walHeader.CheckpointOffset + walHeader.CheckpointLength;
            long current = entryStart + (long)afterLsn.Lsn;
            while (current < fileData.Length)
            {
                if (current + WALEntryHeader.Size > fileData.Length) yield break;

                int off = (int)current;
                var header = WALEntryHeader.Read(fileData, ref off);

                for (int b = 0; b < header.BindingCount; b++)
                {
                    if (off + 2 > fileData.Length) yield break;
                    ushort nameLen = BinaryFormat.ReadUInt16LE(fileData, ref off);
                    off += nameLen;
                    if (off + 2 > fileData.Length) yield break;
                    ushort valueLen = BinaryFormat.ReadUInt16LE(fileData, ref off);
                    off += valueLen;
                    if (off > fileData.Length) yield break;
                }

                int entryLen = off - (int)current + header.MetaLen;
                if ((int)current + entryLen > fileData.Length) yield break;

                byte[] raw = new byte[entryLen];
                Array.Copy(fileData, (int)current, raw, 0, entryLen);

                yield return (raw, new FramePtr((ulong)(current - entryStart)));
                current += entryLen;
            }
        }

        // ── 持久化 ──

        private void Flush()
        {
            lock (_flushLock)
                WriteFullWalFile();
        }

        private void WriteFullWalFile()
        {
            int pendingLen = (int)_pending.ByteLength;

            List<FramePtr> frames;
            byte[] checkpoint;
            lock (_frameLock) { frames = new List<FramePtr>(_frameTable); }
            checkpoint = _checkpointBlob;

            uint frameCount = (uint)frames.Count;
            uint frameTableOff = (uint)WALFormat.HeaderSize;
            uint frameTableSize = frameCount * (uint)WALFormat.FramePtrSize;
            uint checkpointOff = frameTableOff + frameTableSize;
            uint checkpointLen = checkpoint != null ? (uint)checkpoint.Length : 0u;
            uint entryStart = checkpointOff + checkpointLen;

            // Build preamble: header + frameTable + checkpoint
            byte[] preamble = new byte[entryStart];
            int off = 0;
            WALFormat.WriteHeader(preamble, ref off, frameCount, frameTableOff, checkpointOff, checkpointLen);

            off = (int)frameTableOff;
            foreach (var fp in frames)
                WALFormat.WriteFramePtr(preamble, ref off, fp);

            if (checkpoint != null)
                Array.Copy(checkpoint, 0, preamble, (int)checkpointOff, checkpoint.Length);

            // Build pending entries blob
            byte[] pendingData = new byte[pendingLen];
            int writePos = 0;
            for (uint i = 1; i <= _pending.TruncationCount; i++)
            {
                ReadOnlySpan<byte> entry = _pending.ReadFrom(i);
                entry.CopyTo(new Span<byte>(pendingData, writePos, entry.Length));
                writePos += entry.Length;
            }

            int preambleLen = (int)entryStart;
            if (preambleLen != _lastFlushPreambleLen)
            {
                // Preamble size changed (frame table grew or checkpoint changed)
                byte[] full = new byte[preambleLen + pendingLen];
                Array.Copy(preamble, 0, full, 0, preambleLen);
                Array.Copy(pendingData, 0, full, preambleLen, pendingLen);
                _storage.Create(full);
                _lastFlushPreambleLen = preambleLen;
            }
            else
            {
                _storage.OverwritePreamble(preamble);
                if (pendingLen > 0)
                    _storage.Append(pendingData);
            }

            _pending.Clear();
        }

        // ── 生命周期 ──

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Flush();
            _pending.Dispose();
        }

        public void Delete()
        {
            _disposed = true;
            Flush();
            if (_storage.Exists)
                _storage.Delete();
        }
    }

    /// <summary>
    /// FluxWAL 的类型安全泛型包装。
    /// </summary>
    /// <typeparam name="TMeta">非托管用户自定义元数据结构体</typeparam>
    public class FluxWAL<TMeta> : IDisposable
        where TMeta : unmanaged
    {
        private readonly FluxWAL _inner;

        public FluxWAL(string directoryPath, int pendingCapacity = 262144, int maxTruncations = 4096)
            : this(new WALFileStorage(directoryPath), pendingCapacity, maxTruncations) { }
        public FluxWAL(IWALStorage storage, int pendingCapacity = 262144, int maxTruncations = 4096)
            => _inner = new FluxWAL(storage, pendingCapacity, maxTruncations);

        public FramePtr CurrentLsn => _inner.CurrentLsn;
        public int FrameCount => _inner.FrameCount;
        public bool HasCheckpoint => _inner.HasCheckpoint;
        public FramePtr CheckpointLsn => _inner.CheckpointLsn;

        /// <summary>通过公式引用追加 entry（自动提取 formulaHash）。</summary>
        public FramePtr Append<TData, TDef>(
            FluxFormula<TData, TDef> formula,
            (string Name, byte[] Value)[] bindings,
            TMeta meta)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            return Append(formula.GetByteHash(), bindings, meta);
        }

        /// <summary>通过 DualHash64 直接追加。</summary>
        public FramePtr Append(
            DualHash64 formulaHash,
            (string Name, byte[] Value)[] bindings,
            TMeta meta)
        {
            byte[] metaBytes = MetaToBytes(in meta);
            return _inner.Append(formulaHash, bindings, metaBytes);
        }

        internal FluxWAL Inner => _inner;

        /// <inheritdoc cref="WALReplay.Replay{TMeta, TData, TDef}"/>
        public IEnumerable<(WALEntry<TMeta> Entry, TData Result)> Replay<TData, TDef>(
            FluxCurryEvaluator<TData, TDef> evaluator, FramePtr afterLsn)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
            => WALReplay.Replay<TMeta, TData, TDef>(this, evaluator, afterLsn);

        /// <inheritdoc cref="WALReplay.Checkpoint{TMeta, TData, TDef}"/>
        public void Checkpoint<TData, TDef>(FluxCurryEvaluator<TData, TDef> evaluator)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
            => WALReplay.Checkpoint<TMeta, TData, TDef>(this, evaluator);

        /// <inheritdoc cref="WALReplay.Restore{TMeta, TData, TDef}"/>
        public FluxCurryEvaluator<TData, TDef> Restore<TData, TDef>(TDef definition)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
            => WALReplay.Restore<TMeta, TData, TDef>(this, definition);

        /// <inheritdoc cref="WALReplay.Rollback{TMeta, TData, TDef}"/>
        public FluxCurryEvaluator<TData, TDef> Rollback<TData, TDef>(
            TDef definition, FramePtr targetFrame)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
            => WALReplay.Rollback<TMeta, TData, TDef>(this, definition, targetFrame);

        /// <summary>
        /// 开启一个事务作用域。收集绑定后在 Dispose 时作为单条目自动提交。
        /// </summary>
        public FluxTransaction<TMeta, TData, TDef> Begin<TData, TDef>(
            FluxFormula<TData, TDef> formula,
            TMeta meta)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            byte[] metaBytes = MetaToBytes(in meta);
            var evaluator = new FluxAssembler<TData, TDef>(default).Curry(formula);
            return new FluxTransaction<TMeta, TData, TDef>(
                _inner, formula.GetByteHash(), evaluator, metaBytes, meta);
        }

        public int CommitFrame() => _inner.CommitFrame();
        public FramePtr GetFramePtr(int frameIndex) => _inner.GetFramePtr(frameIndex);
        public bool TryGetFrameRange(int frameIndex, out FramePtr start, out FramePtr end)
            => _inner.TryGetFrameRange(frameIndex, out start, out end);
        public void Dispose() => _inner.Dispose();
        public void Delete() => _inner.Delete();

        private static unsafe byte[] MetaToBytes(in TMeta meta)
        {
            byte[] bytes = new byte[sizeof(TMeta)];
            fixed (byte* p = &bytes[0])
                *(TMeta*)p = meta;
            return bytes;
        }
    }
}
