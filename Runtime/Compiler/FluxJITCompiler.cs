using FluxFormula.Core;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FluxFormula.Compiler
{
    internal readonly ref struct FluxJITCompiler<TData, TOper, TProvider>
        where TData : unmanaged
        where TOper : unmanaged, Enum
        where TProvider : struct, IFluxJITDefinition<TData, TOper>
    {
        public delegate TData CompiledFunc(Instruction[] dataBuffer);

        public static CompiledFunc Compile(
            ReadOnlySpan<Instruction> origin,
            TProvider provider,
            out Instruction[] payload
        )
        {
            int instSize = Marshal.SizeOf<Instruction>();
            int dataSize = Marshal.SizeOf<TData>();
            int dataSlotsPerParam = (dataSize + instSize - 1) / instSize;

            int totalDataSlots = 0;
            for (int i = 0; i < origin.Length; i++)
            {
                if (provider.GetKind(origin[i].OpCode) == OpType.Immediate)
                {
                    totalDataSlots += dataSlotsPerParam;
                    i += dataSlotsPerParam;
                }
            }

            payload = new Instruction[totalDataSlots];
            var bufferParam = Expression.Parameter(typeof(Instruction[]), "dataBuffer");

            var regs = new ParameterExpression[256];
            for (int i = 0; i < 256; i++)
                regs[i] = Expression.Variable(typeof(TData), $"r{i}");

            LabelTarget returnTarget = Expression.Label(typeof(TData), "exit");
            var body = new List<Expression>();
            int currentDataIdx = 0;

            var castMethod = typeof(FluxJITCompiler<TData, TOper, TProvider>).GetMethod(
                nameof(SafeCast),
                BindingFlags.NonPublic | BindingFlags.Static
            );

            TData zeroValue = default;
            var defaultTDataExpr = Expression.Constant(zeroValue, typeof(TData));

            for (int ip = 0; ip < origin.Length; ip++)
            {
                var inst = origin[ip];
                OpType kind = provider.GetKind(inst.OpCode);

                if (kind == OpType.Immediate)
                {
                    origin.Slice(ip + 1, dataSlotsPerParam).CopyTo(payload.AsSpan(currentDataIdx));

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
                    var operExpr = provider.GetExpression(inst.OpCode, inst, regs);
                    body.Add(Expression.Assign(regs[inst.Dest], operExpr));

                    body.Add(
                        Expression.IfThen(
                            Expression.NotEqual(regs[0], defaultTDataExpr),
                            Expression.Return(returnTarget, regs[0])
                        )
                    );
                }
                else if (kind == OpType.Return)
                {
                    var resultExpr = Expression.Condition(
                        Expression.NotEqual(regs[0], defaultTDataExpr),
                        regs[0],
                        regs[inst.Dest]
                    );
                    body.Add(Expression.Return(returnTarget, resultExpr));
                    break;
                }
            }

            body.Add(Expression.Label(returnTarget, regs[1]));

            var block = Expression.Block(regs, body);

            return Expression.Lambda<CompiledFunc>(block, bufferParam).Compile();
        }

        private static unsafe TData SafeCast(Instruction[] buffer, int index)
        {
            fixed (Instruction* pBase = buffer)
            {
                return *(TData*)(pBase + index);
            }
        }
    }
}
