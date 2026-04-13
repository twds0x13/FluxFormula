# Getting Started

Build a complete floating-point arithmetic formula from scratch.

## Step 1: Define the Operator Enum

```csharp
public enum FloatOp : byte
{
    Const,     // Immediate value (operand)
    Add,       // +
    Sub,       // -
    Mul,       // *
    Div,       // /
    Neg,       // Unary negation
    LParen,    // (
    RParen,    // )
    Return,    // Termination instruction
}
```

The enum underlying type must be `: byte`. The framework reads the first byte via `*(byte*)&oper` as the opcode. A non-byte underlying type throws an exception during type initialization.

## Step 2: Implement the Definition

```csharp
public readonly struct FloatMathDef : IFluxJITDefinition<float, FloatOp>
{
    public FloatOp GetReturnOp() => FloatOp.Return;

    public int GetArity(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Add => 2, FloatOp.Sub => 2, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 1, _ => 0,
    };

    public OpType GetKind(byte op) => ((FloatOp)op) switch
    {
        FloatOp.Const  => OpType.Immediate,
        FloatOp.Return => OpType.Return,
        _              => OpType.Instruction,
    };

    public int GetPrecedence(FloatOp op) => op switch
    {
        FloatOp.Add => 1, FloatOp.Sub => 1, FloatOp.Mul => 2,
        FloatOp.Div => 2, FloatOp.Neg => 3, _ => 0,
    };

    // Token disambiguation: '-' is unary negation when an operand is expected
    public FloatOp ResolveToken(FloatOp op, TokenContext ctx)
    {
        if (op == FloatOp.Sub && ctx == TokenContext.OperandExpected)
            return FloatOp.Neg;
        return op;
    }

    // Interpreter path
    public float Compute(byte op, Instruction inst, ReadOnlySpan<float> regs)
    {
        return ((FloatOp)op) switch
        {
            FloatOp.Add => regs[inst.Arg0] + regs[inst.Arg1],
            FloatOp.Mul => regs[inst.Arg0] * regs[inst.Arg1],
            FloatOp.Sub => regs[inst.Arg0] - regs[inst.Arg1],
            FloatOp.Div => regs[inst.Arg0] / regs[inst.Arg1],
            FloatOp.Neg => -regs[inst.Arg0],
            _ => 0f,
        };
    }

    // JIT path
    public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
    {
        return ((FloatOp)op) switch
        {
            FloatOp.Add => Expression.Add(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Mul => Expression.Multiply(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Sub => Expression.Subtract(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Div => Expression.Divide(regs[inst.Arg0], regs[inst.Arg1]),
            FloatOp.Neg => Expression.Negate(regs[inst.Arg0]),
            _ => Expression.Constant(0f),
        };
    }
    // Parenthesis and associativity configuration omitted; see "Writing a Definition"
}
```

## Step 3: Configure the Lexer and Parse

```csharp
var config = new LexerConfig<float, FloatOp>
{
    LiteralOper = FloatOp.Const,
    LiteralParser = s => float.Parse(s, CultureInfo.InvariantCulture),
    Operators =
    {
        new("+", FloatOp.Add),
        new("-", FloatOp.Sub),
        new("*", FloatOp.Mul),
        new("/", FloatOp.Div),
    },
    Brackets =
    {
        new("(", ")", FloatOp.LParen, FloatOp.RParen),
    },
    VariablePatterns =
    {
        new("[", "]"),  // Variables wrapped in [ ]: [atk], [def]
    },
    ImplicitOperators = { FloatOp.Mul },  // 2[atk] → 2*[atk]
};

var lexer = new FluxLexer<float, FloatOp>(config);
var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
// lexResult.Tokens   → FluxToken[]
// lexResult.VarNames → Variable name array (atk, bonus)
```

## Step 4: Compile and Execute

```csharp
var def    = new FloatMathDef();
var runner = new FluxAssembler<float, FloatOp, FloatMathDef>(def);

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

When string parsing is not needed, construct token arrays directly:

```csharp
var tokens = new FluxToken<float, FloatOp>[]
{
    new() { Oper = FloatOp.Const, Data = 1f },
    new() { Oper = FloatOp.Add },
    new() { Oper = FloatOp.Const, Data = 2f },
    new() { Oper = FloatOp.Mul },
    new() { Oper = FloatOp.Const, Data = 3f },
};
// Expression: 1 + 2 * 3

float result = runner.Build(tokens, jit: true).Run();
// result = 7.0
```

## Next Steps

- [Core Concepts](/en/guide/core-concepts) — Full Token → Formula → Instance pipeline
- [Writing a Definition](/en/guide/writing-a-definition) — `IFluxJITDefinition` methods in detail
- [Full Example](/en/examples/float-math) — Copy-paste ready FloatMathDef implementation
