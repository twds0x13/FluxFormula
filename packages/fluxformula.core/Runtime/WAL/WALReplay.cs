using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// WAL 重放、检查点恢复等求值相关操作。
    /// 从 <see cref="FluxWAL{TMeta}"/> 中分离，保持存储层不含求值逻辑。
    /// </summary>
    public static class WALReplay
    {
        /// <summary>从指定 LSN 开始重放全部 entry 到 curry evaluator。</summary>
        public static IEnumerable<(WALEntry<TMeta> Entry, TData Result)> Replay<TMeta, TData, TDef>(
            this FluxWAL<TMeta> wal,
            FluxCurryEvaluator<TData, TDef> evaluator,
            FramePtr afterLsn)
            where TMeta : unmanaged
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            foreach (var (raw, _) in wal.Inner.EnumerateEntries(afterLsn))
            {
                var entry = new WALEntry<TMeta>(raw, 0, raw.Length);
                var curry = evaluator;

                foreach (var (name, valueBytes) in entry.GetBindings())
                {
                    TData value = default;
                    if (valueBytes.Length == Unsafe.SizeOf<TData>())
                        value = Unsafe.ReadUnaligned<TData>(ref valueBytes[0]);
                    curry = curry.TryBind(name, value);
                }

                yield return (entry, curry.Result);
            }
        }

        /// <summary>类型安全 checkpoint——序列化 curry evaluator 状态存入 WAL。</summary>
        public static void Checkpoint<TMeta, TData, TDef>(
            this FluxWAL<TMeta> wal,
            FluxCurryEvaluator<TData, TDef> evaluator)
            where TMeta : unmanaged
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            wal.Inner.Checkpoint(evaluator.ToRecord());
        }

        /// <summary>从最近 checkpoint 恢复 curry evaluator。</summary>
        public static FluxCurryEvaluator<TData, TDef> Restore<TMeta, TData, TDef>(
            this FluxWAL<TMeta> wal,
            TDef definition)
            where TMeta : unmanaged
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            byte[] record = wal.Inner.ReadCheckpoint();
            if (record == null || record.Length == 0)
                throw new InvalidOperationException("No checkpoint available.");
            return FluxCurryEvaluator<TData, TDef>.FromRecord(record, definition);
        }

        /// <summary>
        /// 回滚到指定帧。
        /// 恢复 checkpoint 状态后，累积重放 checkpoint 到 targetFrame 之间的所有条目。
        /// </summary>
        public static FluxCurryEvaluator<TData, TDef> Rollback<TMeta, TData, TDef>(
            this FluxWAL<TMeta> wal,
            TDef definition,
            FramePtr targetFrame)
            where TMeta : unmanaged
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            var evaluator = Restore<TMeta, TData, TDef>(wal, definition);
            var checkpointLsn = wal.CheckpointLsn;

            foreach (var (raw, lsn) in wal.Inner.EnumerateEntries(checkpointLsn))
            {
                if (lsn >= targetFrame)
                    break;

                var entry = new WALEntry<TMeta>(raw, 0, raw.Length);
                foreach (var (name, valueBytes) in entry.GetBindings())
                {
                    TData value = default;
                    if (valueBytes.Length == Unsafe.SizeOf<TData>())
                        value = Unsafe.ReadUnaligned<TData>(ref valueBytes[0]);
                    evaluator = evaluator.TryBind(name, value);
                }
            }

            return evaluator;
        }
    }
}
