using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    // ═══════════════════════════════════════════════════════
    // Enums & Structs (originally from FluxInstruction.cs)
    // ═══════════════════════════════════════════════════════

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

    public enum TokenContext
    {
        OperandExpected,
        OperatorExpected,
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

    // ═══════════════════════════════════════════════════════
    // Interfaces (originally from IFluxDefinition.cs)
    // ═══════════════════════════════════════════════════════

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

        /// <summary>
        /// 根据位置上下文消歧 Token。
        /// Lexer 产出的符号（如 '-' → Sub）在 OperandExpected 位置应被重新解释。
        /// 返回 default(TOper) 表示不消歧、保持原 Oper。
        /// </summary>
        TOper ResolveToken(TOper oper, TokenContext context);
    }

    public interface IFluxJITDefinition<TData, TOper> : IFluxDefinition<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers);
    }
}
