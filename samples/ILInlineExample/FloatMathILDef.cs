using System;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FluxFormula.Compiler;
using FluxFormula.Core;

// ═══════════════════════════════════════════════════════════════
// EmitOp 内联 IL 内联示例：FloatMathILDef
// 对 Add / Mul 操作码手写内联 IL，完全消除虚调用开销。
// 其余操作码返回 false，编译器自动回退 Compute 指针 指针调用。
// ═══════════════════════════════════════════════════════════════

public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, Return = 255,
}

public readonly struct FloatMathILDef : IFluxILDefinition<float>, IFluxExprDefinition<float>
{
    // ── 基础接口（与普通 Definition 完全相同）──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetReturnOp() => (byte)FloatOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2,
        FloatOp.Mul => 2, FloatOp.Div => 2,
        FloatOp.Neg => 1,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpType GetKind(byte op) => (FloatOp)op switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1,
        FloatOp.Mul => 2, FloatOp.Div => 2,
        FloatOp.Neg => 3,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op) => new() { PairRole = Pair.None };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op) => (FloatOp)op switch
    {
        FloatOp.Neg => Associativity.Right,
        _           => Associativity.Left,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ResolveToken(byte oper, TokenContext context)
    {
        if ((FloatOp)oper == FloatOp.Sub && context == TokenContext.OperandExpected)
            return (byte)FloatOp.Neg;
        return 0;
    }

    public string GetOperatorName(byte op) => ((FloatOp)op).ToString();

    // ── Expression 树（JIT 回退路径）──

    public System.Linq.Expressions.Expression GetExpression(
        byte op, Instruction inst, System.Linq.Expressions.ParameterExpression[] regs)
    {
        return (FloatOp)op switch
        {
            FloatOp.Add => System.Linq.Expressions.Expression.Add(
                regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => System.Linq.Expressions.Expression.Subtract(
                regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => System.Linq.Expressions.Expression.Multiply(
                regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => System.Linq.Expressions.Expression.Divide(
                regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Neg => System.Linq.Expressions.Expression.Negate(regs[inst.Arg0]),
            _ => System.Linq.Expressions.Expression.Constant(0f),
        };
    }

    // ── Span 版 Compute（解释器路径）──

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return (FloatOp)op switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Div => regs[inst.Arg1] != 0f
                ? regs[inst.Arg0] / regs[inst.Arg1] : float.NaN,
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // ── 指针版 Compute（Compute 指针：IL 编译器回退路径）──

    float IFluxDefinition<float>.Compute(byte op, Instruction inst, IntPtr rp, int _)
    {
        unsafe
        {
            float* r = (float*)rp;
            return (FloatOp)op switch
            {
                FloatOp.Add => r[inst.Arg0] + r[inst.Arg1],
                FloatOp.Sub => r[inst.Arg0] - r[inst.Arg1],
                FloatOp.Mul => r[inst.Arg0] * r[inst.Arg1],
                FloatOp.Div => r[inst.Arg1] != 0f
                    ? r[inst.Arg0] / r[inst.Arg1] : float.NaN,
                FloatOp.Neg => -r[inst.Arg0],
                _ => 0f,
            };
        }
    }

    // ═══════════════════════════════════════════════════════════
    // EmitOp 内联：IFluxILDefinition.EmitOp —— 手写内联 IL
    // ═══════════════════════════════════════════════════════════

    public bool EmitOp(byte op, Instruction inst, ILGenerator il, LocalBuilder regArr)
    {
        // ── Add: regArr[dest] = regArr[arg0] + regArr[arg1] ──
        // stelem 栈顺序: array, index, value（栈顶 = value）
        // 因此必须先推 array/index，最后推 value。
        if ((FloatOp)op == FloatOp.Add)
        {
            il.Emit(OpCodes.Ldloc, regArr);          // [arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Dest); // [arr, destIdx]
            il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0); // [arr, destIdx, arr, idx0]
            il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0]]
            il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr[arg0], arr]
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1); // [arr, destIdx, arr[arg0], arr, idx1]
            il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0], arr[arg1]]
            il.Emit(OpCodes.Add);                    // [arr, destIdx, sum]
            il.Emit(OpCodes.Stelem, typeof(float));  // arr[destIdx] = sum; []
            return true;
        }

        // ── Mul: regArr[dest] = regArr[arg0] * regArr[arg1] ──
        if ((FloatOp)op == FloatOp.Mul)
        {
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Dest);
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0);
            il.Emit(OpCodes.Ldelem, typeof(float));
            il.Emit(OpCodes.Ldloc, regArr);
            il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1);
            il.Emit(OpCodes.Ldelem, typeof(float));
            il.Emit(OpCodes.Mul);                    // ※ 与 Add 唯一不同之处
            il.Emit(OpCodes.Stelem, typeof(float));
            return true;
        }

        // ── 其他操作码：返回 false，编译器回退 Compute 指针 ──
        return false;
    }
}
