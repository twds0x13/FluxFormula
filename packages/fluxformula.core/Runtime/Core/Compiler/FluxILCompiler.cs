using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    /// <summary>
    /// IL 发射编译器——通过 <see cref="DynamicMethod"/> + <see cref="ILGenerator"/>
    /// 直接将字节码编译为委托，跳过 Expression 树的构建开销。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="FluxExprCompiler{TData, TDef}"/> 共享同一个委托类型
    /// <see cref="FluxExprCompiler{TData, TDef}.CompiledFunc"/> 和缓存入口。</para>
    ///
    /// <para>IL 路径仅在 <see cref="System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"/>
    /// 为 true 时可用（Mono / CoreCLR）；IL2CPP 平台自动降级到 Expression 树路径。</para>
    /// </remarks>
    [ExcludeFromCodeCoverage]  // DynamicMethod 生成的代码无法被静态覆盖率工具插桩；排除以避免兼容性崩溃
    internal readonly ref struct FluxILCompiler<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxExprDefinition<TData>
    {
        /// <summary>sizeof(TData) 向上取整到 8 字节（Instruction 大小）</summary>
        private static readonly int DataSlots = FormulaFormat.DataSlots<TData>();

        /// <summary>TDef 是否实现了 IL 内联发射接口（Tier B）</summary>
        private static readonly bool HasTierB =
            typeof(IFluxILDefinition<TData>).IsAssignableFrom(typeof(TDef));

        /// <summary>EqualityComparer&lt;TData&gt;.Default.Equals —— 用于 IL 中的 IsDefault 检查</summary>
        private static readonly MethodInfo EqualsMethod =
            typeof(EqualityComparer<TData>).GetMethod(
                nameof(EqualityComparer<TData>.Equals),
                new[] { typeof(TData), typeof(TData) })!;

        /// <summary>EqualityComparer&lt;TData&gt;.Default 静态属性</summary>
        private static readonly PropertyInfo DefaultComparerProp =
            typeof(EqualityComparer<TData>).GetProperty(nameof(EqualityComparer<TData>.Default))!;

        /// <summary>
        /// IFluxDefinition&lt;TData&gt;.Compute(byte, Instruction, IntPtr, int) —— IL 调用目标
        /// </summary>
        private static readonly MethodInfo ComputePtrMethod =
            typeof(IFluxDefinition<TData>).GetMethod(
                nameof(IFluxDefinition<TData>.Compute),
                new[] { typeof(byte), typeof(Instruction), typeof(IntPtr), typeof(int) })!;

        /// <summary>Instruction.Raw 字段（FieldOffset(0) 的 long），用于 type-safe IL 存储</summary>
        private static readonly FieldInfo RawField =
            typeof(Instruction).GetField(nameof(Instruction.Raw))!;

        /// <summary>
        /// 编译字节码为可执行委托（与 <see cref="FluxExprCompiler{TData, TDef}.Compile"/> 签名一致）。
        /// </summary>
        internal static CompiledFunc<TData> Compile(
            ReadOnlySpan<Instruction> raw,
            TDef definition,
            out Instruction[] payload,
            bool pruneRegisters = false,
            byte maxRegister = 0)
        {
            // ── Pass 1: 构建 payload（立即数数据缓冲区）──
            int totalDataSlots = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (definition.GetKind(raw[i].OpCode) == OpType.Immediate)
                {
                    totalDataSlots += DataSlots;
                    i += DataSlots;
                }
            }
            payload = new Instruction[totalDataSlots];

            // ── Pass 2: 确定寄存器数量 ──
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
            }
            int regCount = actualMax + 1;

            // ── Pass 3: IL 发射 ──
            var dm = new DynamicMethod(
                "FluxEval_IL",
                typeof(TData),
                new[] { typeof(Instruction[]) },
                typeof(FluxILCompiler<TData, TDef>).Module,
                skipVisibility: true);

            var il = dm.GetILGenerator();

            // 寄存器——使用 TData[] 数组保证内存连续，fixed pinning 获取指针
            var regArr = il.DeclareLocal(typeof(TData[]));
            il.Emit(OpCodes.Ldc_I4, regCount);
            il.Emit(OpCodes.Newarr, typeof(TData));
            il.Emit(OpCodes.Stloc, regArr);          // regArr = new TData[regCount]

            // def local (TDef struct)
            var defLocal = il.DeclareLocal(typeof(TDef));
            il.Emit(OpCodes.Ldloca, defLocal);
            il.Emit(OpCodes.Initobj, typeof(TDef));

            // Instruction local (for call value-type param)
            var instLocal = il.DeclareLocal(typeof(Instruction));

            // Temp local for default(TData) in error check
            var defaultTmp = il.DeclareLocal(typeof(TData));

            // ── Helper: emit load reg[i] onto stack ──
            void EmitLoadReg(int regIdx)
            {
                il.Emit(OpCodes.Ldloc, regArr);
                il.Emit(OpCodes.Ldc_I4, regIdx);
                il.Emit(OpCodes.Ldelem, typeof(TData));
            }

            // ── Helper: emit store value → reg[i] ──
            // Caller MUST push value AFTER calling this (stack: [arr, idx, value])
            // Use EmitStoreFinal after value is on stack.
            void EmitStoreAddr(int regIdx)
            {
                il.Emit(OpCodes.Ldloc, regArr);
                il.Emit(OpCodes.Ldc_I4, regIdx);
            }
            void EmitStelem()
            {
                il.Emit(OpCodes.Stelem, typeof(TData));
            }

            // ── 填充 payload 并发射逐条指令 ──
            int currentDataIdx = 0;
            for (int ip = 0; ip < raw.Length; ip++)
            {
                var inst = raw[ip];
                OpType kind = definition.GetKind(inst.OpCode);

                if (kind == OpType.Immediate)
                {
                    // 拷贝立即数数据到 payload
                    raw.Slice(ip + 1, DataSlots).CopyTo(payload.AsSpan(currentDataIdx));

                    // emit: regs[inst.Dest] = ReadData(payload, currentDataIdx)
                    // Stack order: arr, idx, value → stelem
                    EmitStoreAddr(inst.Dest);
                    il.Emit(OpCodes.Ldarg_0);               // payload
                    il.Emit(OpCodes.Ldc_I4, currentDataIdx);  // index
                    il.Emit(OpCodes.Call, ReadDataMethod);   // TData value
                    EmitStelem();

                    currentDataIdx += DataSlots;
                    ip += DataSlots;
                }
                else if (kind == OpType.Instruction)
                {
                    // ── Tier B check: delegate to IFluxILDefinition.EmitOp ──
                    bool tierBHandled = false;
                    if (HasTierB)
                    {
                        // Boxing unavoidable for struct→interface cast, but this is compile-time only
                        var ilDef = (IFluxILDefinition<TData>)(object)definition;
                        tierBHandled = ilDef.EmitOp(inst.OpCode, inst, il, regArr);
                    }

                    if (!tierBHandled)
                    {
                    // ── emit: regs[inst.Dest] = def.Compute(op, inst, regPtr, regCount) ──

                    // Store Instruction raw value via type-safe field write:
                    // Mono IL verifier rejects ldc.i8 → stloc on a typed struct local.
                    // Writing to Raw (FieldOffset(0)) instead produces correct 8-byte value.
                    il.Emit(OpCodes.Ldloca, instLocal);
                    il.Emit(OpCodes.Ldc_I8, inst.Raw);
                    il.Emit(OpCodes.Stfld, RawField);

                    // Call IFluxDefinition<TData>.Compute(byte, Instruction, IntPtr, int)
                    il.Emit(OpCodes.Ldloca, defLocal);       // &def (this)
                    il.Emit(OpCodes.Ldc_I4, (int)inst.OpCode); // op
                    il.Emit(OpCodes.Ldloc, instLocal);        // Instruction (value type)
                    il.Emit(OpCodes.Ldloc, regArr);           // TData[]
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldelema, typeof(TData));  // &arr[0]
                    il.Emit(OpCodes.Conv_I);                  // → IntPtr
                    il.Emit(OpCodes.Ldc_I4, regCount);       // regCount
                    il.Emit(OpCodes.Constrained, typeof(TDef));
                    il.Emit(OpCodes.Callvirt, ComputePtrMethod);

                    // Store result: stack has TData value → arr[idx] = value
                    var resultTmp = il.DeclareLocal(typeof(TData));
                    il.Emit(OpCodes.Stloc, resultTmp);
                    EmitStoreAddr(inst.Dest);
                    il.Emit(OpCodes.Ldloc, resultTmp);
                    EmitStelem();
                    } // end if (!tierBHandled)

                    // ── R0 错误检查 (shared by Tier A + Tier B) ──
                    il.Emit(OpCodes.Call, DefaultComparerProp.GetGetMethod()!);
                    EmitLoadReg(Registers.Error);              // r0 value
                    il.Emit(OpCodes.Ldloca, defaultTmp);
                    il.Emit(OpCodes.Initobj, typeof(TData));
                    il.Emit(OpCodes.Ldloc, defaultTmp);        // default(TData)
                    il.Emit(OpCodes.Callvirt, EqualsMethod);
                    var okLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue_S, okLabel);        // r0 == default → continue
                    EmitLoadReg(Registers.Error);              // r0 != default → return r0
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(okLabel);
                }
                else if (kind == OpType.Return)
                {
                    // ── emit: return hasError ? r0 : regs[inst.Dest] ──
                    il.Emit(OpCodes.Call, DefaultComparerProp.GetGetMethod()!);
                    EmitLoadReg(Registers.Error);              // r0
                    il.Emit(OpCodes.Ldloca, defaultTmp);
                    il.Emit(OpCodes.Initobj, typeof(TData));
                    il.Emit(OpCodes.Ldloc, defaultTmp);        // default(TData)
                    il.Emit(OpCodes.Callvirt, EqualsMethod);
                    var returnOkLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue_S, returnOkLabel);   // r0 == default → return dest

                    EmitLoadReg(Registers.Error);              // r0 != default → return r0
                    il.Emit(OpCodes.Ret);

                    il.MarkLabel(returnOkLabel);
                    EmitLoadReg(inst.Dest);                    // return regs[dest]
                    il.Emit(OpCodes.Ret);
                    break;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unknown OpType in IL compiler: {kind} (opCode=0x{inst.OpCode:X2}).");
                }
            }

            return (CompiledFunc<TData>)dm.CreateDelegate(typeof(CompiledFunc<TData>));
        }

        /// <summary>
        /// 从 Instruction[] payload 中按索引读取 TData 值。
        /// 等价于 <c>*(TData*)(pBase + index)</c>，通过 fixed pinning 实现类型重解释。
        /// </summary>
        private static unsafe TData ReadData(Instruction[] payload, int index)
        {
            fixed (Instruction* pBase = payload)
            {
                return *(TData*)(pBase + index);
            }
        }

        private static readonly MethodInfo ReadDataMethod =
            typeof(FluxILCompiler<TData, TDef>).GetMethod(
                nameof(ReadData),
                BindingFlags.NonPublic | BindingFlags.Static)!;
    }
}
