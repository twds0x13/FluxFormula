using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    public enum FluxType : byte
    {
        Modifier,
        Formula,
    }

    public enum OpType : byte
    {
        Immediate,
        Instruction,
        Return,
    }

    public enum Associativity
    {
        Left,
        Right,
    }

    public enum Pair : byte
    {
        None,
        Left,
        Right,
    }

    public struct OpPair<TOper>
        where TOper : unmanaged, Enum
    {
        public Pair PairRole;
        public TOper TargetLeft;
        public bool EmitOnMatch;
        public TOper EmitOpCode;

        public override readonly string ToString() =>
            PairRole == Pair.None
                ? "Pair: None"
                : $"Pair: {PairRole} (Target: {TargetLeft}, Emit: {EmitOnMatch} [{EmitOpCode}])";
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Instruction
    {
        [FieldOffset(0)]
        public byte OpCode;

        [FieldOffset(1)]
        public byte Dest;

        [FieldOffset(2)]
        public byte Arg0;

        [FieldOffset(3)]
        public byte Arg1;

        [FieldOffset(4)]
        public byte Arg2;

        [FieldOffset(5)]
        public byte Arg3;

        [FieldOffset(6)]
        public byte Arg4;

        [FieldOffset(7)]
        public byte Arg5;

        [FieldOffset(0)]
        public long Raw;

        public override readonly string ToString() =>
            $"[Op: {OpCode:D3}] R{Dest} <- (R{Arg0}, R{Arg1}, R{Arg2}, R{Arg3}, R{Arg4}, R{Arg5})";
    }

    public interface IFluxDefinition<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        TOper GetReturnOp();
        int GetArity(byte op);
        OpType GetKind(byte op);
        int GetPrecedence(TOper op);
        OpPair<TOper> GetPair(TOper op);
        Associativity GetAssociativity(TOper op);
        TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers);
    }

    public interface IFluxJITDefinition<TData, TOper> : IFluxDefinition<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers);
    }

    internal readonly unsafe ref struct FluxEvaluator<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxDefinition<TData, TOper>
    {
        private readonly TDef _definition;

        public FluxEvaluator(TDef definition)
        {
            _definition = definition;
        }

        public TData Compute(ReadOnlySpan<Instruction> raw)
        {
            byte* rawPtr = stackalloc byte[sizeof(TData) * byte.MaxValue + 63];
            long addr = (long)rawPtr;
            TData* regsPtr = (TData*)((addr + 63) & ~63);
            Span<TData> registers = new(regsPtr, byte.MaxValue);

            regsPtr[0] = default;
            regsPtr[1] = default;

            fixed (Instruction* pBase = raw)
            {
                for (int ip = 0; ip < raw.Length; ip++)
                {
                    Instruction* inst = pBase + ip;
                    byte operByte = inst->OpCode;
                    OpType kind = _definition.GetKind(operByte);

                    if (kind == OpType.Immediate)
                    {
                        TData* pData = (TData*)(pBase + ip + 1);
                        regsPtr[inst->Dest] = *pData;
                        ip += (sizeof(TData) + 7) / 8;
                    }
                    else if (kind == OpType.Instruction)
                    {
                        regsPtr[inst->Dest] = _definition.Compute(operByte, *inst, registers);

                        if (!IsDefault(&regsPtr[0]))
                            return regsPtr[0];
                    }
                    else if (kind == OpType.Return)
                    {
                        break;
                    }
                }
            }

            return IsDefault(&regsPtr[0]) ? regsPtr[1] : regsPtr[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDefault(TData* ptr)
        {
            TData zero = default;
            return new ReadOnlySpan<byte>(ptr, sizeof(TData)).SequenceEqual(
                new ReadOnlySpan<byte>(&zero, sizeof(TData))
            );
        }
    }
}
