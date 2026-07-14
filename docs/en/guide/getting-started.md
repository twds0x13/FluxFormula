# Getting Started

First time using FluxFormula? This page walks you through a complete formula pipeline from scratch: define operators, implement compute logic, configure the lexer, and run a variable-injectable evaluation chain.

Build a complete floating-point arithmetic formula from scratch.

## Step 1: Define the Operator Enum

The operator enum is now a `private` implementation detail of the definition — the framework only sees `byte`:

```csharp
enum MathOp : byte
{
    Const,     // Immediate value (operand)
    Add,       // +
    Sub,       // -
    Mul,       // *
    Div,       // /
    Neg,       // Unary negation
    LParen,    // (
    RParen,    // )
    Return = 255,  // Termination instruction
}
```

## Step 2: Implement the Definition

Implement `IFluxExprDefinition<float>` (single generic param). All operator-related methods receive/return `byte`:

```csharp
readonly struct MathDef : IFluxExprDefinition<float>
{
    public byte GetReturnOp() => (byte)MathOp.Return;

    public int GetArity(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 2, MathOp.Sub => 2, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((MathOp)op) switch
    {
        MathOp.Const  => OpType.Immediate,
        MathOp.Return => OpType.Return,
        _             => OpType.Instruction,
    };

    public int GetPrecedence(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => 1, MathOp.Sub => 1, MathOp.Mul => 2,
        MathOp.Div => 2, MathOp.Neg => 3, _ => 0,
    };

    public Associativity GetAssociativity(byte op) => ((MathOp)op) switch
    {
        MathOp.Neg => Associativity.Right,
        _          => Associativity.Left,
    };

    public OperandPosition GetFirstPosition(byte op) => ((MathOp)op) switch
    {
        MathOp.Add => OperandPosition.Left,
        MathOp.Sub => OperandPosition.Left,
        MathOp.Mul => OperandPosition.Left,
        MathOp.Div => OperandPosition.Left,
        _          => OperandPosition.Right,
    };

    public OpPair GetPair(byte op) => ((MathOp)op) switch
    {
        MathOp.LParen => new OpPair { PairRole = Pair.Left },
        MathOp.RParen => new OpPair
        {
            PairRole   = Pair.Right,
            TargetLeft = (byte)MathOp.LParen,
        },
        _ => new OpPair { PairRole = Pair.None },
    };

    // Token disambiguation: '-' is unary negation when an operand is expected
    public byte ResolveToken(byte oper, TokenContext ctx)
    {
        if (oper == (byte)MathOp.Sub && ctx == TokenContext.OperandExpected)
            return (byte)MathOp.Neg;
        return oper;
    }

    public string GetOperatorName(byte op) => ((MathOp)op).ToString();

    // Interpreter path
    public float Compute(byte op, Instruction inst, Span<float> regs)
    {
        return ((MathOp)op) switch
        {
            MathOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            MathOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            MathOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            MathOp.Div => Math.Abs(regs[inst.Arg1]) < float.Epsilon
                ? float.NaN
                : regs[inst.Arg0] / regs[inst.Arg1],
            MathOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT path
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        var zero = Expression.Constant(0f);
        var nan  = Expression.Constant(float.NaN);
        return ((MathOp)op) switch
        {
            MathOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            MathOp.Div => Expression.Condition(
                Expression.Equal(regs[inst.Arg1], zero),
                nan,
                Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])),
            MathOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
}
```

::: tip LiteralTemplate (v5.0+)
For custom TData structs, use `[Template]` to auto-generate literal scanners at compile time instead of writing a `LiteralScanner` delegate. The built-in `float` type works directly via `CreateDefaultNumberScanner`. See the [Literal Scanner guide](/en/guide/literal-scanner) for details.
:::

## Step 3: Configure the Lexer and Parse

```csharp
var config = new LexerConfig<float>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets =
    {
        new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),  // Variables wrapped in [ ]: [atk], [def]
    },
    ImplicitOperators = { (byte)MathOp.Mul },  // 2[atk] → 2*[atk]
};

var lexer = new FluxLexer<float>(config);
var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
// lexResult.Tokens   → FluxToken[]
// lexResult.VarNames → Variable name array (atk, bonus)
```

## Step 4: Compile and Execute

```csharp
var def    = new MathDef();
var runner = new FluxAssembler<float, MathDef>(def);

var formula = runner.Compile(lexResult);
float result = runner.Instantiate(formula)
    .Set("atk", 150f)
    .Set("bonus", 25f)
    .Run();
// result = 3.25
```

## Separating Compilation from Execution

Formulas are cacheable and reusable. Compile once, instantiate and run many times:

```csharp
// Compile (cacheable)
var formula = runner.Compile(lexResult);

// Instantiate (lightweight, can be created repeatedly)
var inst = runner.Instantiate(formula, jit: false);
float r = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

## Direct Token Construction (Without Lexer)

```csharp
var lexResult = lexer.Lex("1 + 2 * 3");
var formula   = runner.Compile(lexResult);
float result  = runner.Instantiate(formula, jit: true).Run();
// result = 7.0
```

## Key Changes from v3.0.0 to v5.1.0

| v2.x | v3.0.0 |
|------|--------|
| `public enum FloatOp : byte` | `enum MathOp : byte` (private) |
| `IFluxExprDefinition<float, FloatOp>` | `IFluxExprDefinition<float>` |
| `GetPrecedence(FloatOp op)` | `GetPrecedence(byte op)` |
| `ResolveToken(FloatOp op, ...)` | `ResolveToken(byte oper, ...)` |
| `OpPair<FloatOp>` | `OpPair` (non-generic) |
| `FluxToken<float, FloatOp>` | `FluxToken<float>` |
| `FluxAssembler<float, FloatOp, FloatMathDef>` | `FluxAssembler<float, MathDef>` |

| v3.0.0 | v5.1.0 |
|--------|--------|
| `LiteralScanner` delegate required | `[Template]` generated scanner (delegate now optional) |
| Manual scan logic | Compile-time source generator producing `Scan_Xxx` methods |

## Next Steps

- [Core Concepts](/en/guide/core-concepts) — Full Token → Formula → Instance pipeline
- [Writing a Definition](/en/guide/writing-a-definition) — `IFluxExprDefinition` methods in detail
- [Full Example](/en/examples/float-math) — Copy-paste ready MathDef implementation
