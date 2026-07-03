using System;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using FluxFormula.Core;

// ============================================================
// 测试用操作符定义：单精度浮点四则运算 + 取负 + 多元函数 + 三元
// ============================================================

public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, Select, Lerp, Sum6, Question, Colon, Comma, LParen, RParen, Return,
}

public readonly struct FloatMathDef : IFluxExprDefinition<float>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetReturnOp() => (byte)FloatOp.Return;

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
        FloatOp.Const => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPrecedence(byte op) { var o = (FloatOp)op; return o switch {
        FloatOp.Add => 1, FloatOp.Sub => 1, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 3,
        FloatOp.Select => 4, FloatOp.Lerp => 4, FloatOp.Sum6 => 4,
        FloatOp.Question => -100,
        _ => 0,
    }; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpPair GetPair(byte op) { var o = (FloatOp)op; return o switch {
        FloatOp.LParen => new OpPair { PairRole = Pair.Left },
        FloatOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)FloatOp.LParen,
        },
        FloatOp.Question => new OpPair
        {
            PairRole    = Pair.None,
            EmitOnMatch = true,
            EmitOpCode  = (byte)FloatOp.Select,
        },
        FloatOp.Colon => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft = (byte)FloatOp.Question,
            IsSeparator = true,
        },
        FloatOp.Comma => new OpPair
        {
            PairRole    = Pair.Right,
            TargetLeft = (byte)FloatOp.LParen,
            IsSeparator = true,
        },
        _ => new OpPair { PairRole = Pair.None },}; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Associativity GetAssociativity(byte op) { var o = (FloatOp)op; return o switch {
        FloatOp.Neg => Associativity.Right,
        _           => Associativity.Left,
    }; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Compute(byte op, Instruction inst, Span<float> regs)
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

    float IFluxDefinition<float>.Compute(byte op, Instruction inst, IntPtr rp, int _)
    {
        unsafe
        {
            float* r = (float*)rp;
            return (FloatOp)op switch
            {
                FloatOp.Add    => r[inst.Arg0] + r[inst.Arg1],
                FloatOp.Sub    => r[inst.Arg0] - r[inst.Arg1],
                FloatOp.Mul    => r[inst.Arg0] * r[inst.Arg1],
                FloatOp.Div    => Math.Abs(r[inst.Arg1]) < float.Epsilon
                                   ? float.NaN : r[inst.Arg0] / r[inst.Arg1],
                FloatOp.Neg    => -r[inst.Arg0],
                FloatOp.Select => r[inst.Arg0] != 0f ? r[inst.Arg1] : r[inst.Arg2],
                FloatOp.Lerp   => r[inst.Arg0] + (r[inst.Arg1] - r[inst.Arg0]) * r[inst.Arg2],
                FloatOp.Sum6   => r[inst.Arg0] + r[inst.Arg1] + r[inst.Arg2]
                                + r[inst.Arg3] + r[inst.Arg4] + r[inst.Arg5],
                _ => 0f,
            };
        }
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
    public byte ResolveToken(byte oper, TokenContext context)
    {
        if ((FloatOp)oper == FloatOp.Sub && context == TokenContext.OperandExpected)
            return (byte)FloatOp.Neg;
        return 0;
    }

    public string GetOperatorName(byte op) => ((FloatOp)op).ToString();
}

// ============================================================
// 共享测试辅助方法
// ============================================================

public static class TestHelper
{
    public static readonly FloatMathDef Def = default;

    public static FluxToken<float> C(float v) =>
        new() { Oper = (byte)FloatOp.Const, Data = v };

    public static FluxToken<float> Op(FloatOp o) =>
        new() { Oper = (byte)o };

    public static float Eval(ReadOnlySpan<FluxToken<float>> tokens, bool jit = false)
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        return runner.Build(tokens, jit).Run();
    }

    public static float EvalFormula(FluxFormula<float, FloatMathDef> formula, bool jit = false)
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        return runner.Instantiate(formula, jit).Run();
    }

    public static float EvalFormula(FluxChain<float, FloatMathDef> chain, bool jit = false)
    {
        var runner = new FluxAssembler<float, FloatMathDef>(Def);
        return runner.Instantiate(chain, jit).Run();
    }

    public static FluxLexer<float> CreateMathLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper    = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
        });
    }

    /// <summary>创建启用隐式乘法的 Lexer（2(3), (a)(b)）</summary>
    public static FluxLexer<float> CreateImplicitMulLexer()
    {
        var lexer = new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper    = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            ImplicitOperators = { (byte)FloatOp.Mul },
        });
        return lexer;
    }

    /// <summary>创建支持函数调用 + 三元 ?: 语法的 Lexer</summary>
    public static FluxLexer<float> CreateFuncLexer()
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper    = (byte)FloatOp.Const,
            Operators =
            {
                new("select", (byte)FloatOp.Select, "(", ")"),
                new("lerp", (byte)FloatOp.Lerp, "(", ")"),
                new("?", (byte)FloatOp.Question),
                new(":", (byte)FloatOp.Colon),
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
                new(",", (byte)FloatOp.Comma),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
        });
    }

    /// <summary>创建支持变量模式的 Lexer，如 ("[", "]") 或 ("{var:", "}")</summary>
    public static FluxLexer<float> CreateVarLexer(string prefix, string suffix)
    {
        return new FluxLexer<float>(new LexerConfig<float>
        {
            LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
            LiteralOper    = (byte)FloatOp.Const,
            Operators =
            {
                new("+", (byte)FloatOp.Add), new("-", (byte)FloatOp.Sub),
                new("*", (byte)FloatOp.Mul), new("/", (byte)FloatOp.Div),
            },
            Brackets =
            {
                new("(", ")", (byte)FloatOp.LParen, (byte)FloatOp.RParen),
            },
            VariablePatterns =
            {
                new(prefix, suffix),
            },
        });
    }
}
