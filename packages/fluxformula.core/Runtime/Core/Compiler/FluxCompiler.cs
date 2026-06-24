using System;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    internal readonly unsafe ref struct FluxCompiler<TData, TDef>
        where TData : unmanaged
        where TDef : struct, IFluxDefinition<TData>
    {
        private readonly TDef _provider;
        private const int MaxStackDepth = 64;

        public FluxCompiler(TDef provider) => _provider = provider;

        public int Compile(
            ReadOnlySpan<FluxToken<TData>> infix,
            Span<Instruction> instructions,
            out int immediateCount,
            out int varSlotCount,
            out byte maxRegister,
            string[] varNames = null,
            VariableSlot[] varSlots = null
        )
        {
            varSlotCount = 0;
            byte* opStack = stackalloc byte[MaxStackDepth];
            byte* regStack = stackalloc byte[MaxStackDepth];

            int opTop = -1;
            int regTop = -1;
            int instIdx = 0;
            int immCount = 0;
            byte nextReg = Registers.FirstAlloc;

            // 上下文追踪：用于同符号多语义消歧
            TokenContext ctx = TokenContext.OperandExpected;

            fixed (Instruction* pDest = instructions)
            {
                for (int i = 0; i < infix.Length; i++)
                {
                    ref readonly var token = ref infix[i];
                    byte opByte = token.Oper;
                    OpType kind = _provider.GetKind(opByte);

                    // ── 上下文消歧 ────────────────────────
                    if (kind == OpType.Instruction && ctx == TokenContext.OperandExpected)
                    {
                        byte resolved = _provider.ResolveToken(opByte, TokenContext.OperandExpected);
                        if (resolved != 0) // 非 default → 消歧
                        {
                            opByte = resolved;
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

                    // 处理括号与运算符（使用消歧后的 opByte）
                    var pairInfo = _provider.GetPair(opByte);

                    if (pairInfo.PairRole == Pair.Left)
                    {
                        if (opTop >= MaxStackDepth - 1)
                            throw new StackOverflowException("Operator stack overflow.");
                        opStack[++opTop] = token.Oper;
                        ctx = TokenContext.OperandExpected; // '(' 后期待操作数
                    }
                    else if (pairInfo.PairRole == Pair.Right)
                    {
                        while (opTop >= 0 && opStack[opTop] != pairInfo.TargetLeft)
                        {
                            byte emitOp = opStack[opTop--];
                            var emitPair = _provider.GetPair(emitOp);
                            byte actualOp = emitPair.EmitOnMatch ? emitPair.EmitOpCode : emitOp;
                            EmitOp(
                                pDest,
                                ref instIdx,
                                instructions.Length,
                                actualOp,
                                regStack,
                                ref regTop,
                                ref nextReg
                            );
                        }

                        if (opTop < 0)
                            throw new FormatException($"Unmatched right bracket: 0x{token.Oper:X2}");

                        if (pairInfo.IsSeparator)
                        {
                            // 逗号分隔符：发射中间运算符但不弹出 '('
                            ctx = TokenContext.OperandExpected; // 逗号后期待下一个参数
                        }
                        else
                        {
                            opTop--; // 弹出 '(' (EmitOnMatch on Left bracket 未被使用)

                            // 检查 '(' 下方是否有函数运算符（如 select/lerp）待发射
                            if (opTop >= 0)
                            {
                                byte checkByte = opStack[opTop];
                                if (_provider.GetArity(checkByte) > 0
                                    && _provider.GetPair(checkByte).PairRole == Pair.None)
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
                            }
                            ctx = TokenContext.OperatorExpected; // ')' 后期待运算符
                        }
                    }
                    else
                    {
                        // 处理优先级与结合性
                        while (opTop >= 0)
                        {
                            byte topOp = opStack[opTop];
                            if (_provider.GetPair(topOp).PairRole == Pair.Left)
                                break;

                            int topPrec = _provider.GetPrecedence(topOp);
                            int currPrec = _provider.GetPrecedence(opByte);
                            Associativity assoc = _provider.GetAssociativity(opByte);

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
                        opStack[++opTop] = opByte; // 使用消歧后的 opByte（非原始 token.Oper）
                        ctx = TokenContext.OperandExpected; // 运算符后期待操作数
                    }
                }

                // 清空操作符栈
                while (opTop >= 0)
                {
                    byte topOp = opStack[opTop--];
                    var topPair = _provider.GetPair(topOp);

                    if (topPair.PairRole == Pair.Left)
                        throw new FormatException("Unmatched left parenthesis.");

                    byte actualOp = topPair.EmitOnMatch ? topPair.EmitOpCode : topOp;
                    EmitOp(
                        pDest,
                        ref instIdx,
                        instructions.Length,
                        actualOp,
                        regStack,
                        ref regTop,
                        ref nextReg
                    );
                }

                if (instIdx >= instructions.Length)
                    throw new IndexOutOfRangeException("Buffer too small for return.");

                Instruction* ret = pDest + (instIdx++);
                byte retOp = _provider.GetReturnOp();
                ret->OpCode = retOp;
                ret->Dest = regTop >= 0 ? regStack[0] : Registers.Bus;
            }
            immediateCount = immCount;
            maxRegister    = (byte)(nextReg > Registers.FirstAlloc ? nextReg - 1 : Registers.Bus);
            return instIdx;
        }

        private void EmitOp(
            Instruction* pDest,
            ref int idx,
            int maxLen,
            byte opByte,
            byte* regStack,
            ref int regTop,
            ref byte nextReg
        )
        {
            if (idx >= maxLen)
                throw new IndexOutOfRangeException("Instruction overflow.");

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
                    regStack[0] = Registers.Bus;
                    regTop++;
                }

                int firstRegIdx = regTop - (arity - 1);
                byte destReg = (firstRegIdx == 0) ? Registers.Bus : regStack[firstRegIdx];
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
                throw new InvalidOperationException(
                    $"EmitOp invoked with arity 0 (opCode=0x{opByte:X2}). " +
                    "If this is a bracket/separator token, its PairRole should be Left or Right.");
            }

        }
    }
}
