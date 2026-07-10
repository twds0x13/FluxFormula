# Example: VFF Persistence and Parameter Overrides

This example demonstrates the full lifecycle of VFF (Virtual FluxFormula): compile formulas, build a chained reference, serialize to `.vff` byte array, deserialize from bytes, and evaluate. Parameter overrides allow fixing some variables to constants in the VFF while leaving others injectable at evaluation time.

The `MathDef` used below is defined in [Float Arithmetic](/en/examples/float-math).

## Compile and Cache

Compile two formulas and insert them into `FormulaCache` so VFF deserialization can look up referenced bytecodes by hash:

```csharp
using System;
using System.Globalization;
using System.IO;
using FluxFormula.Core;

var config = new LexerConfig<float>
{
    LiteralOper = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(
        s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add), new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul), new("/", (byte)MathOp.Div),
    },
    Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var lexer     = new FluxLexer<float>(config);
var def       = default(MathDef);
var assembler = new FluxAssembler<float, MathDef>(def);

// Compile two independent formulas
var damage   = assembler.Compile(lexer.Lex("[atk] * [mult]"));    // Formula
var reducer  = assembler.Compile(lexer.Lex("[def] * 0.5"));       // Formula

// Compute hashes and insert into cache
byte[] dBytes = damage.ToBytes(), rBytes = reducer.ToBytes();
var dHash = DualHash64.Compute(dBytes);
var rHash = DualHash64.Compute(rBytes);
FormulaCache.Instance.PutBytes(dHash, dBytes);
FormulaCache.Instance.PutBytes(rHash, rBytes);
```

## Build a Chained Reference

`Connect()` chains two formulas into a `FluxChain`. The output of the first link is passed to the second via the R1 bus:

```csharp
var chain = damage.Connect(reducer.ToModifier());
// Semantics: (atk * mult) - def * 0.5
```

`chain.GetLinks()` returns a `ChainLink[]` ready for `VffFormat.ToBytes()`.

## Serialize to VFF

Pass an empty array when no parameter overrides are needed:

```csharp
var links = chain.GetLinks().ToArray();
byte[] vffData = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());

// Write to a temp file (simulating persist + reload)
string path = Path.GetTempFileName();
File.WriteAllBytes(path, vffData);
```

## Deserialize from Bytes and Evaluate

Load the VFF byte array, deserialize into a chained formula, and evaluate:

```csharp
byte[] loaded = File.ReadAllBytes(path);
var result = VffFormat.FromBytes<float, MathDef>(loaded);

var instance = assembler.Instantiate(result, jit: true);
instance.Set("atk", 100f).Set("mult", 2f).Set("def", 50f);
float value = instance.Run(); // (100 * 2) - 50 * 0.5 = 175

File.Delete(path);
```

`FromBytes` internally looks up referenced formula bytecodes via `FormulaCache`. Ensure all dependency formulas are in the cache before calling it.

## Parameter Overrides

VFF allows fixing variables to specific values at serialization time. The override type is specified by `VffOverrideKind`:

| Type | Meaning |
|------|--------|
| `Inject` | Injected by the caller at evaluation time (default behavior) |
| `Constant` | Hardcoded in the VFF; cannot be overridden at evaluation time |

The `GlobalSlot` identifies a variable's position in the merged pipeline's immediate table. Look it up via `ChainLink.VarSlots`:

```csharp
// Find the global slot for "mult"
int multSlot = -1;
foreach (var link in chain.GetLinks())
    foreach (var vs in link.VarSlots)
        if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }

// Fix "mult" to 2.0; keep "atk" and "def" as runtime-injectable
var overrides = new[]
{
    new VffOverride<float>(multSlot, VffOverrideKind.Constant, 2f),
};

byte[] vffWithOverride = VffFormat.ToBytes<float>(
    chain.GetLinks().ToArray(), overrides);
```

After deserialization, "mult" is hardcoded. The caller only injects the remaining variables:

```csharp
string path2 = Path.GetTempFileName();
File.WriteAllBytes(path2, vffWithOverride);

byte[] loaded2 = File.ReadAllBytes(path2);
var result2 = VffFormat.FromBytes<float, MathDef>(loaded2);

var inst2 = assembler.Instantiate(result2, jit: true)
    .Set("atk", 100f).Set("def", 30f);
float value2 = inst2.Run(); // (100 * 2) - 30 * 0.5 = 185
// "mult" cannot be overridden via Set() — Instantiate auto-applied the VFF Constant override

File.Delete(path2);
```

## Resolve via Cache Hash

If the VFF bytes have already been inserted into the cache via `PutBytes`, use `Resolve()` to look up by hash instead of managing the byte array manually:

```csharp
var vffHash = DualHash64.Compute(vffData);
FormulaCache.Instance.PutBytes(vffHash, vffData);

// Later: resolve from hash alone
var result3 = VffFormat.Resolve<float, MathDef>(vffHash);
```

## Notes

- `FormulaCache` must contain bytecodes for all referenced formulas before calling `FromBytes` or `Resolve`. Missing entries throw `InvalidOperationException`.
- Circular references (VFF referencing itself, directly or indirectly) are detected and rejected during resolution.
- `FluxType` is an internal enum. Use `FluxChain.GetLinks()` to obtain `ChainLink` instances — manual construction is unnecessary.
- The VFF version is currently 1. Future version upgrades will be validated against the `Version` field by `FromBytes`.
