using System;
using System.Collections.Generic;


namespace FluxFormula.Core
{
    /// <summary>
    /// WAL 事务作用域。收集绑定后在 Dispose 时作为单条目提交。
    /// </summary>
    public sealed class FluxTransaction<TMeta, TData, TDef> : IDisposable
        where TMeta : unmanaged
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        private readonly FluxWAL _wal;
        private readonly DualHash64 _formulaHash;
        private readonly byte[] _metaBytes;
        private readonly TMeta _meta;
        private FluxCurryEvaluator<TData, TDef> _evaluator;
        private readonly List<(string Name, byte[] Value)> _bindings;
        private bool _disposed;

        internal FluxTransaction(
            FluxWAL wal,
            DualHash64 formulaHash,
            FluxCurryEvaluator<TData, TDef> evaluator,
            byte[] metaBytes,
            TMeta meta)
        {
            _wal = wal;
            _formulaHash = formulaHash;
            _evaluator = evaluator;
            _metaBytes = metaBytes;
            _meta = meta;
            _bindings = new List<(string, byte[])>();
        }

        /// <summary>累积一个变量绑定。不会立即写入 WAL。</summary>
        public unsafe FluxTransaction<TMeta, TData, TDef> Bind(string name, TData value)
        {
            if (_disposed) throw new ObjectDisposedException("FluxTransaction");
            byte[] bytes = new byte[sizeof(TData)];
            fixed (byte* p = &bytes[0])
                *(TData*)p = value;
            _bindings.Add((name, bytes));
            _evaluator = _evaluator.TryBind(name, value);
            return this;
        }

        /// <summary>当前累积绑定的求值结果。</summary>
        public TData Result => _evaluator.Result;

        /// <summary>已绑定的变量数。</summary>
        public int BoundCount => _bindings.Count;

        /// <summary>求值器是否已完成（所有变量已绑定）。</summary>
        public bool IsCompleted => _evaluator.IsCompleted;

        /// <summary>
        /// 提交事务：将全部绑定作为一条完整条目写入 WAL 并创建帧边界。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _wal.Append(_formulaHash, _bindings.ToArray(), _metaBytes);
            _wal.CommitFrame();
        }
    }
}
