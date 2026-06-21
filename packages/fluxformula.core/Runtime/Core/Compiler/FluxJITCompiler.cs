using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
#if FLUX_FAST_EXPRESSION_COMPILER
using FastExpressionCompiler;
#endif
using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    internal readonly ref struct FluxJITCompiler<TData, TOper, TDef>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TDef : unmanaged, IFluxJITDefinition<TData, TOper>
    {
        public delegate TData CompiledFunc(Instruction[] dataBuffer);

        public static CompiledFunc Compile(
            ReadOnlySpan<Instruction> raw,
            TDef definition,
            out Instruction[] payload,
            bool pruneRegisters = false,
            byte maxRegister = 0
        )
        {
            int dataSlotsPerParam = FormulaFormat.DataSlots<TData>();

            int totalDataSlots = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (definition.GetKind(raw[i].OpCode) == OpType.Immediate)
                {
                    totalDataSlots += dataSlotsPerParam;
                    i += dataSlotsPerParam;
                }
            }

            payload = new Instruction[totalDataSlots];
            var bufferParam = Expression.Parameter(typeof(Instruction[]), "dataBuffer");

            int regCount = maxRegister > Registers.Bus ? maxRegister + 1 : FluxPlatform.MaxRegisters + 1;
            if (pruneRegisters)
            {
                // Scan instructions to find the highest register index actually used.
                // Error(R0) and Bus(R1) are always kept.
                int maxReg = Registers.FirstAlloc;
                for (int i = 0; i < raw.Length; i++)
                {
                    var inst = raw[i];
                    OpType kind = definition.GetKind(inst.OpCode);
                    if (kind == OpType.Return) break;
                    if (inst.Dest > maxReg) maxReg = inst.Dest;
                    if (kind == OpType.Instruction)
                    {
                        int a = definition.GetArity(inst.OpCode);
                        if (a > 0 && inst.Arg0 > maxReg) maxReg = inst.Arg0;
                        if (a > 1 && inst.Arg1 > maxReg) maxReg = inst.Arg1;
                        if (a > 2 && inst.Arg2 > maxReg) maxReg = inst.Arg2;
                        if (a > 3 && inst.Arg3 > maxReg) maxReg = inst.Arg3;
                        if (a > 4 && inst.Arg4 > maxReg) maxReg = inst.Arg4;
                        if (a > 5 && inst.Arg5 > maxReg) maxReg = inst.Arg5;
                    }
                    else if (kind != OpType.Immediate)
                    {
                        throw new InvalidOperationException(
                            $"Unknown OpType in JIT compiler (reg scan): {kind}. " +
                            "If you added a new OpType, update the JIT compiler dispatch.");
                    }
                }
                regCount = maxReg + 1;
            }

            var regs = new ParameterExpression[regCount];
            for (int i = 0; i < regs.Length; i++)
                regs[i] = Expression.Variable(typeof(TData), $"r{i}");

            LabelTarget returnTarget = Expression.Label(typeof(TData), "exit");
            var body = new List<Expression>();
            int currentDataIdx = 0;

            var castMethod = typeof(FluxJITCompiler<TData, TOper, TDef>).GetMethod(
                nameof(SafeCast),
                BindingFlags.NonPublic | BindingFlags.Static
            );

            TData zeroValue = default;
            var defaultTDataExpr = Expression.Constant(zeroValue, typeof(TData));

            for (int ip = 0; ip < raw.Length; ip++)
            {
                var inst = raw[ip];
                OpType kind = definition.GetKind(inst.OpCode);

                if (kind == OpType.Immediate)
                {
                    raw.Slice(ip + 1, dataSlotsPerParam).CopyTo(payload.AsSpan(currentDataIdx));

                    body.Add(
                        Expression.Assign(
                            regs[inst.Dest],
                            Expression.Call(
                                castMethod,
                                bufferParam,
                                Expression.Constant(currentDataIdx)
                            )
                        )
                    );

                    currentDataIdx += dataSlotsPerParam;
                    ip += dataSlotsPerParam;
                }
                else if (kind == OpType.Instruction)
                {
                    var operExpr = definition.GetExpression(inst.OpCode, inst, regs);
                    body.Add(Expression.Assign(regs[inst.Dest], operExpr));

                    body.Add(
                        Expression.IfThen(
                            Expression.NotEqual(regs[Registers.Error], defaultTDataExpr),
                            Expression.Return(returnTarget, regs[Registers.Error])
                        )
                    );
                }
                else if (kind == OpType.Return)
                {
                    var resultExpr = Expression.Condition(
                        Expression.NotEqual(regs[Registers.Error], defaultTDataExpr),
                        regs[Registers.Error],
                        regs[inst.Dest]
                    );
                    body.Add(Expression.Return(returnTarget, resultExpr));
                    break;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unknown OpType in JIT compiler: {kind} (opCode=0x{inst.OpCode:X2}). " +
                        "If you added a new OpType, update the JIT compiler dispatch.");
                }
            }

            body.Add(Expression.Label(returnTarget, regs[Registers.Bus]));

            var block = Expression.Block(regs, body);

            return Expression.Lambda<CompiledFunc>(block, bufferParam)
#if FLUX_FAST_EXPRESSION_COMPILER
                .CompileFast();
#else
                .Compile();
#endif
        }

        private static unsafe TData SafeCast(Instruction[] raw, int index)
        {
            fixed (Instruction* pBase = raw)
            {
                return *(TData*)(pBase + index);
            }
        }
    }
}
