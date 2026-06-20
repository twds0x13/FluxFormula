using System;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    internal readonly unsafe ref struct FluxCompiler<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : struct, IFluxDefinition<TData, TOper>
    {
        private readonly TDef _provider;
        private const int MaxStackDepth = 64;

        public FluxCompiler(TDef provider) => _provider = provider;

        public int Compile(
            ReadOnlySpan<FluxToken<TData, TOper>> infix,
            Span<Instruction> instructions,
            out int immediateCount,
            out int varSlotCount,
            string[] varNames = null,
            VariableSlot[] varSlots = null
        )
        {
            varSlotCount = 0;
            TOper* opStack = stackalloc TOper[MaxStackDepth];
            byte* regStack = stackalloc byte[MaxStackDepth];

            int opTop = -1;
            int regTop = -1;
            int instIdx = 0;
            int immCount = 0;
            byte nextReg = 2; // R0: Error, R1: Bus/Return

            // 上下文追踪：用于同符号多语义消歧
            TokenContext ctx = TokenContext.OperandExpected;

            fixed (Instruction* pDest = instructions)
            {
                for (int i = 0; i < infix.Length; i++)
                {
                    ref readonly var token = ref infix[i];
                    TOper oper = token.Oper;
                    byte opByte = *(byte*)&oper;
                    OpType kind = _provider.GetKind(opByte);

                    // ── 上下文消歧 ────────────────────────
                    if (kind == OpType.Instruction && ctx == TokenContext.OperandExpected)
                    {
                        TOper resolved = _provider.ResolveToken(oper, TokenContext.OperandExpected);
                        if (*(byte*)&resolved != 0) // 非 default → 消歧
                        {
                            oper   = resolved;
                            opByte = *(byte*)&oper;
                            kind   = _provider.GetKind(opByte);
                        }
                    }

                    // 处理操作数
                    if (kind == OpType.Immediate)
                    {
                        ctx = TokenContext.OperatorExpected; // 操作数后期待运算符

                        if (regTop >= MaxStackDepth - 1)
                            throw new StackOverflowException("Register stack overflow.");

                        if (nextReg == FluxPlatform.MaxRegisters)
                            throw new InvalidOperationException("Out of registers.");

                        byte reg = nextReg++;
                        regStack[++regTop] = reg;

                        int dataSize = sizeof(TData);
                        int instSize = sizeof(Instruction);
                        int slots = (dataSize + instSize - 1) / instSize;

                        if (instIdx + 1 + slots > instructions.Length)
                            throw new IndexOutOfRangeException("Instruction buffer too small.");

                        Instruction* instHeader = pDest + (instIdx++);
                        instHeader->OpCode = opByte;
                        instHeader->Dest = reg;

                        TData* pDataPayload = (TData*)(pDest + instIdx);
                        *pDataPayload = token.Data;

                        // 记录变量名 → Immediate 槽位序号
                        if (varNames != null && i < varNames.Length && varNames[i] != null && varSlots != null)
                            varSlots[varSlotCount++] = new VariableSlot(varNames[i], immCount);

                        immCount++;
                        instIdx += slots;
                        continue;
                    }

                    // 处理括号与运算符（使用消歧后的 oper）
                    var pairInfo = _provider.GetPair(oper);

                    if (pairInfo.PairRole == Pair.Left)
                    {
                        if (opTop >= MaxStackDepth - 1)
                            throw new StackOverflowException("Operator stack overflow.");
                        opStack[++opTop] = token.Oper;
                        ctx = TokenContext.OperandExpected; // '(' 后期待操作数
                    }
                    else if (pairInfo.PairRole == Pair.Right)
                    {
                        while (opTop >= 0 && !opStack[opTop].Equals(pairInfo.TargetLeft))
                        {
                            EmitOp(
                                pDest,
                                ref instIdx,
                                instructions.Length,
                                opStack[opTop--],
                                regStack,
                                ref regTop,
                                ref nextReg
                            );
                        }

                        if (opTop < 0)
                            throw new FormatException($"Unmatched right parenthesis: {token.Oper}");

                        TOper leftOp = opStack[opTop--];
                        var leftParenBehavior = _provider.GetPair(leftOp);
                        if (leftParenBehavior.EmitOnMatch)
                        {
                            EmitOp(
                                pDest,
                                ref instIdx,
                                instructions.Length,
                                leftParenBehavior.EmitOpCode,
                                regStack,
                                ref regTop,
                                ref nextReg
                            );
                        }
                        ctx = TokenContext.OperatorExpected; // ')' 后期待运算符
                    }
                    else
                    {
                        // 处理优先级与结合性
                        while (opTop >= 0)
                        {
                            TOper topOp = opStack[opTop];
                            if (_provider.GetPair(topOp).PairRole == Pair.Left)
                                break;

                            int topPrec = _provider.GetPrecedence(topOp);
                            int currPrec = _provider.GetPrecedence(oper);
                            Associativity assoc = _provider.GetAssociativity(oper);

                            bool shouldPop =
                                (topPrec > currPrec)
                                || (topPrec == currPrec && assoc == Associativity.Left);

                            if (!shouldPop)
                                break;

                            EmitOp(
                                pDest,
                                ref instIdx,
                                instructions.Length,
                                opStack[opTop--],
                                regStack,
                                ref regTop,
                                ref nextReg
                            );
                        }

                        if (opTop >= MaxStackDepth - 1)
                            throw new StackOverflowException("Operator stack overflow.");
                        opStack[++opTop] = oper; // 使用消歧后的 oper（非原始 token.Oper）
                        ctx = TokenContext.OperandExpected; // 运算符后期待操作数
                    }
                }

                // 清空操作符栈
                while (opTop >= 0)
                {
                    TOper topOp = opStack[opTop--];
                    if (_provider.GetPair(topOp).PairRole == Pair.Left)
                        throw new FormatException("Unmatched left parenthesis.");

                    EmitOp(
                        pDest,
                        ref instIdx,
                        instructions.Length,
                        topOp,
                        regStack,
                        ref regTop,
                        ref nextReg
                    );
                }

                if (instIdx >= instructions.Length)
                    throw new IndexOutOfRangeException("Buffer too small for return.");

                Instruction* ret = pDest + (instIdx++);
                TOper retOp = _provider.GetReturnOp();
                ret->OpCode = *(byte*)&retOp;
                ret->Dest = regTop >= 0 ? regStack[0] : (byte)1; // 默认返回 R1
            }
            immediateCount = immCount;
            return instIdx;
        }

        private void EmitOp(
            Instruction* pDest,
            ref int idx,
            int maxLen,
            TOper oper,
            byte* regStack,
            ref int regTop,
            ref byte nextReg
        )
        {
            if (idx >= maxLen)
                throw new IndexOutOfRangeException("Instruction overflow.");

            byte opByte = *(byte*)&oper;
            int arity = _provider.GetArity(opByte);

            Instruction* inst = pDest + (idx++);
            inst->OpCode = opByte;

            if (arity > 0)
            {
                while (regTop + 1 < arity)
                {
                    if (regTop >= MaxStackDepth - 1)
                        throw new StackOverflowException("Reg stack overflow on R1 injection.");
                    for (int i = regTop; i >= 0; i--)
                        regStack[i + 1] = regStack[i];
                    regStack[0] = 1;
                    regTop++;
                }

                int firstRegIdx = regTop - (arity - 1);
                byte destReg = (firstRegIdx == 0) ? (byte)1 : regStack[firstRegIdx];
                inst->Dest = destReg;

                if (arity > 0)
                    inst->Arg0 = regStack[firstRegIdx];
                if (arity > 1)
                    inst->Arg1 = regStack[firstRegIdx + 1];
                if (arity > 2)
                    inst->Arg2 = regStack[firstRegIdx + 2];
                if (arity > 3)
                    inst->Arg3 = regStack[firstRegIdx + 3];
                if (arity > 4)
                    inst->Arg4 = regStack[firstRegIdx + 4];
                if (arity > 5)
                    inst->Arg5 = regStack[firstRegIdx + 5];

                regTop = firstRegIdx;
                regStack[regTop] = destReg;
            }
            
            else
            {
                inst->Dest = 1;
                if (regTop >= MaxStackDepth - 1)
                    throw new StackOverflowException("Reg stack overflow.");
                regStack[++regTop] = 1;
            }
            
        }
    }
}
