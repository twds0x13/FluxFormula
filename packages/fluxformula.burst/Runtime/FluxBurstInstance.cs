using System;
using FluxFormula.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace FluxFormula.Burst
{
    /// <summary>
    /// Burst 公式求值器胶水层。持有 NativeArray 字节码和寄存器，提供 Set/SetIndex/Run/Schedule API。
    /// 对标 <see cref="FluxInstance{TData, TDef}"/> 的使用体验。
    /// </summary>
    /// <remarks>
    /// <para>构造时调用 <c>formula.ToBytes()</c> 将字节码拷贝到 <see cref="NativeArray{Byte}"/>，
    /// 脱离托管堆。所有后续操作（Set/SetIndex/Run）均在 NativeArray 上通过指针完成。</para>
    ///
    /// <para>使用完毕后必须调用 <see cref="Dispose"/> 释放 NativeArray。</para>
    ///
    /// <para>线程安全：<see cref="SetIndex"/> 和 <see cref="Set"/> 应在主线程调用。
    /// <see cref="Run()"/> 同步执行。多 Job 并发执行需每个 Job 持有自己的实例。</para>
    /// </remarks>
    public struct FluxBurstInstance<TData, TDef> : IDisposable
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        NativeArray<byte> _bytecode;
        NativeArray<TData> _registers;
        VariableSlot[] _varSlots;
        int[] _slotOffsets;
        byte _maxRegister;
        bool _disposed;

        // ── 构造 ──

        /// <summary>
        /// 从 <see cref="FluxFormula{TData, TDef}"/> 构造 Burst 求值器。
        /// 将字节码拷贝到 NativeArray，预计算 Immediate 槽位偏移量。
        /// </summary>
        public FluxBurstInstance(FluxFormula<TData, TDef> formula)
        {
            byte[] raw = formula.ToBytes();
            _bytecode = new NativeArray<byte>(raw, Allocator.Persistent);
            _registers = new NativeArray<TData>(256, Allocator.Persistent);
            _varSlots = formula.VariableSlots;
            _maxRegister = formula.MaxRegister;
            _disposed = false;

            // 预计算每个 Immediate 槽位的字节偏移量
            _slotOffsets = ComputeSlotOffsets(raw, formula.ImmediateCount);
        }

        /// <summary>
        /// 扫描字节码的 Instruction 体，定位每个 Immediate 槽位中 TData 值的字节偏移。
        /// </summary>
        private static unsafe int[] ComputeSlotOffsets(byte[] bytecode, int immediateCount)
        {
            var offsets = new int[immediateCount];
            if (immediateCount == 0) return offsets;

            int count = BinaryFormat.ReadInt32LE(new ReadOnlySpan<byte>(bytecode, 0, 4));
            int slotIdx = 0;

            fixed (byte* p = bytecode)
            {
                Instruction* pBase = (Instruction*)(p + FormulaFormat.HeaderSize);
                int dataSlots = (sizeof(TData) + 7) / 8;

                for (int ip = 0; ip < count && slotIdx < immediateCount; ip++)
                {
                    byte opCode = pBase[ip].OpCode;
                    OpType kind = default(TDef).GetKind(opCode);

                    if (kind == OpType.Immediate)
                    {
                        // TData 紧跟在当前指令之后
                        int byteOffset = FormulaFormat.HeaderSize + (ip + 1) * FormulaFormat.InstructionSize;
                        offsets[slotIdx++] = byteOffset;
                        ip += dataSlots;
                    }
                }
            }

            return offsets;
        }

        // ── Set / SetIndex ──

        /// <summary>
        /// 按 Immediate 槽位索引注入值。对标 <see cref="FluxInstance{TData, TDef}.SetIndex"/>。
        /// </summary>
        public FluxBurstInstance<TData, TDef> SetIndex(int index, TData value)
        {
            CheckDisposed();
            if ((uint)index >= (uint)_slotOffsets.Length) return this;

            unsafe
            {
                TData* pData = (TData*)((byte*)_bytecode.GetUnsafePtr() + _slotOffsets[index]);
                *pData = value;
            }
            return this;
        }

        /// <summary>
        /// 按变量名注入值。在托管堆上通过 <see cref="VariableSlot"/> 解析为槽位索引后调用 <see cref="SetIndex"/>。
        /// </summary>
        public FluxBurstInstance<TData, TDef> Set(string name, TData value)
        {
            CheckDisposed();
            if (_varSlots == null) return this;

            for (int i = 0; i < _varSlots.Length; i++)
            {
                if (_varSlots[i].Name == name)
                    return SetIndex(_varSlots[i].SlotIndex, value);
            }
            return this;
        }

        // ── Run ──

        /// <summary>
        /// 同步执行公式求值。在当前线程上调用 Burst 解释器。
        /// </summary>
        public TData Run()
        {
            CheckDisposed();
            unsafe
            {
                return FluxBurstEvaluator<TData, TDef>.Execute(
                    (byte*)_bytecode.GetUnsafePtr(),
                    (TData*)_registers.GetUnsafePtr(),
                    _maxRegister);
            }
        }

        /// <summary>
        /// 异步调度——将公式求值提交到 Unity Job 系统。
        /// 调用 <see cref="JobHandle.Complete"/> 后通过 <see cref="Result"/> 获取结果。
        /// </summary>
        /// <param name="dependency">前置 Job 依赖（可选）</param>
        public JobHandle Schedule(JobHandle dependency = default)
        {
            CheckDisposed();
            var job = new BurstJob
            {
                Bytecode  = _bytecode,
                Registers = _registers,
                MaxRegister = _maxRegister,
            };
            return job.Schedule(dependency);
        }

        /// <summary>
        /// R1 总线中的求值结果。仅在 <see cref="Run()"/> 或 Job 完成后有效。
        /// </summary>
        public TData Result
        {
            get
            {
                CheckDisposed();
                return _registers[Registers.Bus];
            }
        }

        // ── Dispose ──

        public void Dispose()
        {
            if (_disposed) return;
            if (_bytecode  .IsCreated) _bytecode  .Dispose();
            if (_registers .IsCreated) _registers .Dispose();
            _disposed = true;
        }

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FluxBurstInstance<TData, TDef>));
        }

        // ── Internal IJob ──

        /// <summary>
        /// Burst 编译的内部 Job——单次公式求值。由 <see cref="Schedule()"/> 创建。
        /// </summary>
        [BurstCompile]
        struct BurstJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Bytecode;
            public NativeArray<TData> Registers;
            public byte MaxRegister;

            public unsafe void Execute()
            {
                FluxBurstEvaluator<TData, TDef>.Execute(
                    (byte*)Bytecode.GetUnsafeReadOnlyPtr(),
                    (TData*)Registers.GetUnsafePtr(),
                    MaxRegister);
            }
        }
    }
}
