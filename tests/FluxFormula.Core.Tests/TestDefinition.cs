using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

// ============================================================
// 测试用操作符定义：单精度浮点四则运算 + 取负
// ============================================================

public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, Select, Lerp, Sum6, Question, Colon, Comma, LParen, RParen, Return,
}

public readonly struct FloatMathDef : IFluxJITDefinition<float, FloatOp>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FloatOp GetReturnOp() => FloatOp.Return;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetArity(byte op) => (FloatOp)op switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1,
        FloatOp.Select => 3, FloatOp.Lerp => 3,
        FloatOp.Sum6 => 6,
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
    public int GetPrecedence(FloatOp op) => op switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 3,
        FloatOp.Select => 4, FloatOp.Lerp => 4, FloatOp.Sum6 => 4,
        FloatOp.Question => -100,
        _ => 0,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair<FloatOp> GetPair(FloatOp op) => op switch
    {
        FloatOp.LParen => new OpPair<FloatOp> { PairRole = Pair.Left },
        FloatOp.RParen => new OpPair<FloatOp>
        {
            PairRole   = Pair.Right,
            TargetLeft = FloatOp.LParen,
        },
        FloatOp.Question => new OpPair<FloatOp>
        {
            PairRole    = Pair.None,
            EmitOnMatch = true,
            EmitOpCode  = FloatOp.Select,
        },
        FloatOp.Colon => new OpPair<FloatOp>
        {
            PairRole    = Pair.Right,
            TargetLeft  = FloatOp.Question,
            IsSeparator = true,
        },
        FloatOp.Comma => new OpPair<FloatOp>
        {
            PairRole    = Pair.Right,
            TargetLeft  = FloatOp.LParen,
            IsSeparator = true,
        },
        _ => new OpPair<FloatOp> { PairRole = Pair.None },
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(FloatOp op) => op switch
    {
        FloatOp.Neg => Associativity.Right,
        _           => Associativity.Left,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return (FloatOp)op switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN : regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            FloatOp.Select => regs[inst.Arg0] != 0f ? regs[inst.Arg1] : regs[inst.Arg2],
            FloatOp.Lerp => regs[inst.Arg0] + (regs[inst.Arg1] - regs[inst.Arg0]) * regs[inst.Arg2],
            FloatOp.Sum6 => regs[inst.Arg0] + regs[inst.Arg1] + regs[inst.Arg2]
                          + regs[inst.Arg3] + regs[inst.Arg4] + regs[inst.Arg5],
            _ => 0f,
        };
    }

    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return (FloatOp)op switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero), nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            FloatOp.Select => Expression.Condition(
                Expression.NotEqual(regs[inst.Arg0], zero),
                regs[inst.Arg1], regs[inst.Arg2]),
            FloatOp.Lerp => Expression.Add(
                regs[inst.Arg0],
                Expression.Multiply(
                    Expression.Subtract(regs[inst.Arg1], regs[inst.Arg0]),
                    regs[inst.Arg2])),
            FloatOp.Sum6 => Expression.Add(
                Expression.Add(
                    Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
                    Expression.Add(regs[inst.Arg2], regs[inst.Arg3])),
                Expression.Add(regs[inst.Arg4], regs[inst.Arg5])),
            _ => Expression.Constant(0f),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FloatOp ResolveToken(FloatOp oper, TokenContext context)
    {
        if (oper == FloatOp.Sub && context == TokenContext.OperandExpected)
            return FloatOp.Neg;
        return default;
    }
}

// ============================================================
// 共享测试辅助方法
// ============================================================

public static class TestHelper
{
    public static readonly FloatMathDef Def = default;

    public static FluxToken<float, FloatOp> C(float v) =>
        new() { Oper = FloatOp.Const, Data = v };

    public static FluxToken<float, FloatOp> Op(FloatOp o) =>
        new() { Oper = o };

    public static float Eval(ReadOnlySpan<FluxToken<float, FloatOp>> tokens, bool jit = false)
    {
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        return runner.Build(tokens, jit).Run();
    }

    public static float EvalFormula(FluxFormula<float, FloatOp> formula, bool jit = false)
    {
        var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(Def);
        return runner.Instantiate(formula, jit).Run();
    }

    public static FluxLexer<float, FloatOp> CreateMathLexer()
    {
        return new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f')),
            LiteralOper    = FloatOp.Const,
            Operators =
            {
                new("+", FloatOp.Add), new("-", FloatOp.Sub),
                new("*", FloatOp.Mul), new("/", FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", FloatOp.LParen, FloatOp.RParen),
            },
        });
    }

    /// <summary>创建启用隐式乘法的 Lexer（2(3), (a)(b)）</summary>
    public static FluxLexer<float, FloatOp> CreateImplicitMulLexer()
    {
        var lexer = new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f')),
            LiteralOper    = FloatOp.Const,
            Operators =
            {
                new("+", FloatOp.Add), new("-", FloatOp.Sub),
                new("*", FloatOp.Mul), new("/", FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", FloatOp.LParen, FloatOp.RParen),
            },
            ImplicitOperators = { FloatOp.Mul },
        });
        return lexer;
    }

    /// <summary>创建支持函数调用语法的 Lexer（select, lerp + 逗号分隔）</summary>
    public static FluxLexer<float, FloatOp> CreateFuncLexer()
    {
        return new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f')),
            LiteralOper    = FloatOp.Const,
            Operators =
            {
                new("select", FloatOp.Select, "(", ")"),
                new("lerp", FloatOp.Lerp, "(", ")"),
                new("?", FloatOp.Question),
                new(":", FloatOp.Colon),
                new("+", FloatOp.Add), new("-", FloatOp.Sub),
                new("*", FloatOp.Mul), new("/", FloatOp.Div),
                new(",", FloatOp.Comma),
            },
            Brackets =
            {
                new("(", ")", FloatOp.LParen, FloatOp.RParen),
            },
        });
    }

    /// <summary>创建支持变量模式的 Lexer，如 ("[", "]") 或 ("{var:", "}")</summary>
    public static FluxLexer<float, FloatOp> CreateVarLexer(string prefix, string suffix)
    {
        return new FluxLexer<float, FloatOp>(new LexerConfig<float, FloatOp>
        {
            LiteralPattern = @"\d+(\.\d+)?f?",
            LiteralParser  = s => float.Parse(s.TrimEnd('f')),
            LiteralOper    = FloatOp.Const,
            Operators =
            {
                new("+", FloatOp.Add), new("-", FloatOp.Sub),
                new("*", FloatOp.Mul), new("/", FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", FloatOp.LParen, FloatOp.RParen),
            },
            VariablePatterns =
            {
                new(prefix, suffix),
            },
        });
    }
}
