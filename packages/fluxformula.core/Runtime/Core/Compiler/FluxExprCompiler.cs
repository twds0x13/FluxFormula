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
    internal readonly ref struct FluxExprCompiler<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {

        /// <summary>
        /// <see cref="IsDefault"/> 的 MethodInfo 缓存：在 Expression 树中调用，
        /// 避免 <c>Expression.Equal</c> 对无 <c>op_Equality</c> 的自定义 TData 抛出。
        /// </summary>
        private static readonly System.Reflection.MethodInfo _isDefaultMethod =
            typeof(FluxExprCompiler<TData, TDef>).GetMethod(
                nameof(IsDefault),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        /// <summary>
        /// 使用 <see cref="System.Collections.Generic.EqualityComparer{T}"/>.Default 比较，
        /// 兼容无 <c>==</c>/<c>!=</c> 运算符的自定义值类型（TData : unmanaged）。
        /// </summary>
        private static bool IsDefault(TData value)
        {
            return System.Collections.Generic.EqualityComparer<TData>.Default.Equals(value, default);
        }

        public static CompiledFunc<TData> Compile(
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

            // 扫描 bytecode 查找实际最大寄存器号（防御 ToFormula 等场景未更新 MaxRegister）
            byte actualMax = maxRegister > Registers.Bus ? maxRegister : Registers.FirstAlloc;
            for (int i = 0; i < raw.Length; i++)
            {
                var inst = raw[i];
                OpType kind = definition.GetKind(inst.OpCode);
                if (kind == OpType.Return) break;
                if (inst.Dest > actualMax) actualMax = inst.Dest;
                if (kind == OpType.Instruction)
                {
                    int a = definition.GetArity(inst.OpCode);
                    if (a > 0 && inst.Arg0 > actualMax) actualMax = inst.Arg0;
                    if (a > 1 && inst.Arg1 > actualMax) actualMax = inst.Arg1;
                    if (a > 2 && inst.Arg2 > actualMax) actualMax = inst.Arg2;
                    if (a > 3 && inst.Arg3 > actualMax) actualMax = inst.Arg3;
                    if (a > 4 && inst.Arg4 > actualMax) actualMax = inst.Arg4;
                    if (a > 5 && inst.Arg5 > actualMax) actualMax = inst.Arg5;
                }
                else if (kind != OpType.Immediate)
                {
                    throw new InvalidOperationException(
                        $"Unknown OpType in JIT compiler (reg scan): {kind}. " +
                        "If you added a new OpType, update the JIT compiler dispatch.");
                }
            }
            int regCount = actualMax + 1;

            var regs = new ParameterExpression[regCount];
            for (int i = 0; i < regs.Length; i++)
                regs[i] = Expression.Variable(typeof(TData), $"r{i}");

            LabelTarget returnTarget = Expression.Label(typeof(TData), "exit");
            var body = new List<Expression>();
            int currentDataIdx = 0;

            var castMethod = typeof(FluxExprCompiler<TData, TDef>).GetMethod(
                nameof(SafeCast),
                BindingFlags.NonPublic | BindingFlags.Static
            );

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

                    // Expression.Equal/NotEqual 要求 TData 定义 op_Equality/op_Inequality；
                    // 对无运算符的自定义值类型，使用 EqualityComparer<T>.Default.Equals 代替。
                    body.Add(
                        Expression.IfThen(
                            Expression.Not(Expression.Call(
                                _isDefaultMethod, regs[Registers.Error])),
                            Expression.Return(returnTarget, regs[Registers.Error])
                        )
                    );
                }
                else if (kind == OpType.Return)
                {
                    Expression hasError = Expression.Not(
                        Expression.Call(_isDefaultMethod, regs[Registers.Error]));
                    var resultExpr = Expression.Condition(
                        hasError,
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

            return Expression.Lambda<CompiledFunc<TData>>(block, bufferParam)
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
