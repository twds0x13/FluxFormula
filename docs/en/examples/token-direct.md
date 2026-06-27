# Example: Direct Token Construction

Build `FluxToken<TData>[]` arrays manually, bypassing the lexer. Useful for code-generated formulas, unit tests, and toolchain scenarios that require precise control over token sequences.

The `MathDef` used below is defined in [Float Arithmetic](/en/examples/float-math), with opcodes `Const=0, Add=1, Sub=2, Mul=3, Div=4, Return=255`.

## Infix Expression → Token Array

`v1 * 2 + v2` as a token sequence:

```csharp
using FluxFormula.Core;

// Literals → Oper=Const, Data=value; Operators → Oper=byte, Data=default
var tokens = new FluxToken<float>[]
{
    new() { Oper = (byte)MathOp.Const, Data = 0f },    // [v1] — placeholder, injected at runtime via Set("v1", ...)
    new() { Oper = (byte)MathOp.Mul,   Data = default },
    new() { Oper = (byte)MathOp.Const, Data = 2f },     // immediate 2
    new() { Oper = (byte)MathOp.Add,   Data = default },
    new() { Oper = (byte)MathOp.Const, Data = 0f },     // [v2] — placeholder
    new() { Oper = (byte)MathOp.Return,Data = default }, // Return
};
```

## Compile and Evaluate

`FluxAssembler.Compile(ReadOnlySpan<FluxToken<TData>>)` accepts a token array directly, bypassing the lexer:

```csharp
var assembler = new FluxAssembler<float, MathDef>(new MathDef());

// Tokens → Instruction[] (one-time compile allocation)
var formula = assembler.Compile(tokens);

// Instantiate → inject variables → evaluate
float result = assembler.Instantiate(formula, jit: true)
    .Set("v1", 100f)
    .Set("v2", 50f)
    .Run();
// result = (100 * 2) + 50 = 250
```

## Variable Placeholder Rules

The `Data` field of `Const` tokens is ignored during compilation: the compiler treats all `Const` tokens as immediate slots, assigning `SlotIndex` in order of appearance. Variable names (via the `VarNames` parameter) establish name-to-slot mappings at compile time.

Without variable names, use `SetIndex` to inject by position:

```csharp
var formula = assembler.Compile(tokens); // no varNames
float result = assembler.Instantiate(formula)
    .SetIndex(0, 100f)  // first Const → Slot 0
    .SetIndex(2, 50f)   // third Const → Slot 2 (second is literal 2, not settable)
    .Run();
```

With variable names, use `Set(name, value)`:

```csharp
var formula = assembler.Compile(tokens, new[] { "v1", "v2" });
float result = assembler.Instantiate(formula)
    .Set("v1", 100f)
    .Set("v2", 50f)
    .Run();
```

## Automatic Type Inference

`FluxAssembler.Compile()` automatically determines whether the result is a `FluxFormula` or should be treated as a `FluxModifier`: first token is `Const` → Formula; first token is a binary operator (e.g., `Add`) → Modifier. When constructing tokens manually, ensure the first token matches the intended formula type.

## Notes

- The number and order of `Const` tokens must match the injection positions used by `Set` / `SetIndex` at runtime
- A `Return` token is required — the compiler does not append one automatically
- Semantically identical to the lexer path: compiled `Instruction[]` arrays work with JIT/interpreter, serialization, and caching as usual
