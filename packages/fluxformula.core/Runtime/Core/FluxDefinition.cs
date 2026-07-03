using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace FluxFormula.Core
{
    // ═══════════════════════════════════════════════════════
    // Enums & Structs (originally from FluxInstruction.cs)
    // ═══════════════════════════════════════════════════════

    internal enum FluxType : byte
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

    public struct OpPair
    {
        public Pair PairRole;
        public byte TargetLeft;
        public bool EmitOnMatch;
        public byte EmitOpCode;
        /// <summary>参数分隔符（如逗号）：触发 emit 但不弹出 Left 括号。</summary>
        public bool IsSeparator;

        public override readonly string ToString() =>
            PairRole == Pair.None
                ? "Pair: None"
                : $"Pair: {PairRole} (Target: 0x{TargetLeft:X2}, Emit: {EmitOnMatch} [0x{EmitOpCode:X2}])";
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

    public interface IFluxDefinition<TData>
        where TData : unmanaged
    {
        byte GetReturnOp();
        int GetArity(byte op);
        OpType GetKind(byte op);
        int GetPrecedence(byte op);
        OpPair GetPair(byte op);
        Associativity GetAssociativity(byte op);
        /// <summary>
        /// 执行操作符。默认实现从 <paramref name="registers"/> 读取操作数并返回结果。
        /// 若要触发 R0 短路（提前终止求值），写入 <c>registers[Registers.Error]</c> 为非 default 值。
        /// </summary>
        TData Compute(byte op, Instruction inst, Span<TData> registers);

        /// <summary>
        /// 通过内存指针执行操作符——IL 发射路径的调用目标。
        /// 默认实现桥接到 <see cref="Compute(byte, Instruction, Span{TData})"/>；
        /// 覆写此方法可消除 span 构造开销。
        /// </summary>
        TData Compute(byte op, Instruction inst, IntPtr registers, int regCount)
        {
            unsafe { return Compute(op, inst, new Span<TData>((void*)registers, regCount)); }
        }

        /// <summary>
        /// 根据位置上下文消歧 Token。
        /// Lexer 产出的符号（如 '-' → Sub）在 OperandExpected 位置应被重新解释。
        /// 返回 0 表示不消歧、保持原 Oper。
        /// </summary>
        byte ResolveToken(byte oper, TokenContext context);

        /// <summary>
        /// 操作码的显示名称，用于编辑器/调试/工具链。
        /// 默认返回 null，定义实现者应覆写以提供有意义的名称。
        /// 返回 null 的操作码被视为无效/未定义，编辑器会跳过。
        /// </summary>
        string GetOperatorName(byte op) => null;
    }

    public interface IFluxExprDefinition<TData> : IFluxDefinition<TData>
        where TData : unmanaged
    {
        Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers);
    }
}
