using System;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

// ═══════════════════════════════════════════════════════
// TData 结构体
// ═══════════════════════════════════════════════════════

[LiteralTemplate("<float Damage>|<optional>draw <byte DrawsProvide>|</optional>idx:<byte StartIndex>")]
public struct SpellContext : IEquatable<SpellContext>
{
    public float Damage;              // 累积伤害
    public byte DrawsProvide;         // 剩余抽牌数
    public byte ConsumedThisRound;    // 本轮已消费卡数
    public byte StartIndex;           // 本轮起始卡位置
#pragma warning disable CS0169
    byte _padding;                    // 保留（填充至 8 字节对齐）
#pragma warning restore CS0169

    public SpellContext(float damage, int draws, int consumed = 0, int startIndex = 0)
        => (Damage, DrawsProvide, ConsumedThisRound, StartIndex)
            = (damage, (byte)draws, (byte)consumed, (byte)startIndex);

    public readonly bool Equals(SpellContext other)
        => Damage == other.Damage && DrawsProvide == other.DrawsProvide
                                  && ConsumedThisRound == other.ConsumedThisRound
                                  && StartIndex == other.StartIndex;

    public override readonly string ToString()
        => $"({Damage:F1} dmg, {DrawsProvide} draws, {ConsumedThisRound} consumed, start={StartIndex})";
}

// ═══════════════════════════════════════════════════════
// 操作符枚举
// ═══════════════════════════════════════════════════════

public enum SpellOp : byte
{
    Const, Add,
    LParen, RParen, Return,
}

// ═══════════════════════════════════════════════════════
// 定义体
// ═══════════════════════════════════════════════════════

public readonly struct SpellDef : IFluxExprDefinition<SpellContext>
{
    public byte GetReturnOp() => (byte)SpellOp.Return;

    public int GetArity(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => 2, _ => 0,
    };

    public OpType GetKind(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Const  => OpType.Immediate,
        SpellOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => 1, _ => 0,
    };

    public OpPair GetPair(byte op) => ((SpellOp)op) switch
    {
        SpellOp.LParen => new OpPair { PairRole = Pair.Left },
        SpellOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)SpellOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public OperandPosition GetFirstPosition(byte op) => ((SpellOp)op) switch
    {
        SpellOp.Add => OperandPosition.Left,
        _           => OperandPosition.Right,
    };

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public SpellContext Compute(byte op, Instruction inst, Span<SpellContext> regs)
    {
        SpellContext a = regs[inst.Arg0];
        SpellContext b = regs[inst.Arg1];
        return ((SpellOp)op) switch
        {
            SpellOp.Add => EvaluateAdd(a, b, regs),
            _ => default,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((SpellOp)op) switch
        {
            SpellOp.Add => Expression.Call(
                typeof(SpellDef).GetMethod(nameof(EvaluateAddJit),
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic)!,
                regs[inst.Arg0], regs[inst.Arg1], regs[Registers.Error]),
            _ => Expression.Constant(default(SpellContext)),
        };
    }

    // 解释器路径：抽数耗尽时写 R0 触发框架短路
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SpellContext EvaluateAdd(SpellContext a, SpellContext b, Span<SpellContext> regs)
    {
        if (b.StartIndex < a.StartIndex)
            return a;
        if (a.DrawsProvide <= 0)
        {
            regs[Registers.Error] = a;
            return default;
        }
        return new SpellContext(
            a.Damage + b.Damage,
            a.DrawsProvide + b.DrawsProvide - 1,
            a.ConsumedThisRound + 1,
            a.StartIndex);
    }

    // JIT 路径：通过 ref 参数写入 R0，Expression 树编译器映射为寄存器写入
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SpellContext EvaluateAddJit(SpellContext a, SpellContext b, ref SpellContext r0)
    {
        if (b.StartIndex < a.StartIndex)
            return a;
        if (a.DrawsProvide <= 0)
        {
            r0 = a;
            return default;
        }
        return new SpellContext(
            a.Damage + b.Damage,
            a.DrawsProvide + b.DrawsProvide - 1,
            a.ConsumedThisRound + 1,
            a.StartIndex);
    }
}

// ═══════════════════════════════════════════════════════
// 追踪结构体：SpellTracker
// ═══════════════════════════════════════════════════════

public struct SpellTracker : IEquatable<SpellTracker>
{
    public SpellContext Context;      // 链公式输出
    public ulong ConsumedMask;        // 已消费位掩码
    public ulong RequiredMask;        // 终止掩码 = (1 << (maxIndex + 1)) - 1

    public SpellTracker(SpellContext context, ulong mask, ulong requiredMask)
        => (Context, ConsumedMask, RequiredMask) = (context, mask, requiredMask);

    public readonly bool Equals(SpellTracker other)
        => Context.Equals(other.Context) && ConsumedMask == other.ConsumedMask
                                         && RequiredMask == other.RequiredMask;

    public override readonly string ToString()
        => $"(mask: 0x{ConsumedMask:X}, req: 0x{RequiredMask:X}, {Context})";
}

// ═══════════════════════════════════════════════════════
// 追踪操作符
// ═══════════════════════════════════════════════════════

public enum TrackerOp : byte
{
    Const, Track, Return,
}

// ═══════════════════════════════════════════════════════
// 追踪定义体
// ═══════════════════════════════════════════════════════

public readonly struct TrackerDef : IFluxExprDefinition<SpellTracker>
{
    public byte GetReturnOp() => (byte)TrackerOp.Return;

    public int GetArity(byte op) => ((TrackerOp)op) switch
    {
        TrackerOp.Track => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((TrackerOp)op) switch
    {
        TrackerOp.Const  => OpType.Immediate,
        TrackerOp.Return => OpType.Return,
        _                => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => 0;

    public OpPair GetPair(byte op) => new OpPair { PairRole = Pair.None };

    public Associativity GetAssociativity(byte op) => Associativity.Left;

    public OperandPosition GetFirstPosition(byte op) => ((TrackerOp)op) switch
    {
        TrackerOp.Track => OperandPosition.Right,  // 前缀运算符: Track [prev]
        _               => OperandPosition.Left,
    };

    public byte ResolveToken(byte op, TokenContext ctx) => op;

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
        => throw new NotSupportedException("TrackerDef does not support JIT compilation");

    public SpellTracker Compute(byte op, Instruction inst, Span<SpellTracker> regs)
    {
        SpellTracker t = regs[inst.Arg0];
        if ((t.ConsumedMask & t.RequiredMask) == t.RequiredMask)
            return t;  // 所有卡已消费，终止

        byte consumed = t.Context.ConsumedThisRound;
        if (consumed <= 0)
            return t;  // 本轮无消费，透传

        // 找第一个未消费位 = 本轮起始卡位置
        int pos = BitOperations.TrailingZeroCount(~t.ConsumedMask);

        ulong mask = t.ConsumedMask | (((1ul << consumed) - 1) << pos);

        var ctx = t.Context;
        ctx.ConsumedThisRound = 0;
        ctx.StartIndex = (byte)(pos + consumed);  // 下一轮从此位置继续
        return new SpellTracker(ctx, mask, t.RequiredMask);
    }
}
