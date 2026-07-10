using System;
using System.Globalization;
using System.IO;
using FluxFormula.Core;

// ── 1. Configure Lexer + Assembler ──
var config = new LexerConfig<float>
{
    LiteralOper   = (byte)MathOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s, CultureInfo.InvariantCulture)),
    Operators =
    {
        new("+", (byte)MathOp.Add),
        new("-", (byte)MathOp.Sub),
        new("*", (byte)MathOp.Mul),
        new("/", (byte)MathOp.Div),
    },
    Brackets = { new("(", ")", (byte)MathOp.LParen, (byte)MathOp.RParen) },
    VariablePatterns = { new("[", "]") },
};

var def    = default(MathDef);
var runner = new FluxAssembler<float, MathDef>(def);
var lexer  = new FluxLexer<float>(config);

// ── 2. Compile two independent formulas ──
var damage  = runner.Compile(lexer.Lex("[atk] * [mult]"));
var reducer = runner.Compile(lexer.Lex("[def] * 0.5"));

// ── 3. Cache bytecode via DualHash64 → FormulaCache ──
byte[] dBytes = damage.ToBytes(), rBytes = reducer.ToBytes();
var dHash = DualHash64.Compute(dBytes);
var rHash = DualHash64.Compute(rBytes);
FormulaCache.Instance.PutBytes(dHash, dBytes);
FormulaCache.Instance.PutBytes(rHash, rBytes);

// ── 4. Connect into a chain: (atk * mult) - def * 0.5 ──
var chain = damage.Connect(reducer.ToModifier());

// ── 5. Serialize to VFF ──
var links = chain.GetLinks().ToArray();
byte[] vffData = VffFormat.ToBytes<float>(links, Array.Empty<VffOverride<float>>());

// ── 6. Write to temp file (simulating persist + reload) ──
string path = Path.GetTempFileName();
File.WriteAllBytes(path, vffData);

// ── 7. Deserialize and evaluate ──
byte[] loaded = File.ReadAllBytes(path);
var result = VffFormat.FromBytes<float, MathDef>(loaded);

var instance = runner.Instantiate(result.Chain, jit: true);
instance.Set("atk", 100f).Set("mult", 2f).Set("def", 50f);
float value = instance.Run();
Console.WriteLine($"(100 * 2) - 50 * 0.5 = {value}");

File.Delete(path);

// ── 8. Parameter override: fix "mult" to 2.0 ──
int multSlot = -1;
foreach (var link in chain.GetLinks())
    foreach (var vs in link.VarSlots)
        if (vs.Name == "mult") { multSlot = vs.SlotIndex; break; }

var overrides = new[]
{
    new VffOverride<float>(multSlot, VffOverrideKind.Constant, 2f),
};

byte[] vffWithOverride = VffFormat.ToBytes<float>(
    chain.GetLinks().ToArray(), overrides);

string path2 = Path.GetTempFileName();
File.WriteAllBytes(path2, vffWithOverride);

byte[] loaded2 = File.ReadAllBytes(path2);
var result2 = VffFormat.FromBytes<float, MathDef>(loaded2);

var inst2 = runner.Instantiate(result2.Chain, jit: true);
inst2.Set("atk", 100f).Set("def", 30f);
float value2 = inst2.Run();
Console.WriteLine($"(100 * 2) - 30 * 0.5 = {value2}  (mult fixed to 2.0)");

File.Delete(path2);

// ── 9. Resolve via cache hash ──
var vffHash = DualHash64.Compute(vffData);
FormulaCache.Instance.PutBytes(vffHash, vffData);

var result3 = VffFormat.Resolve<float, MathDef>(vffHash);
var inst3 = runner.Instantiate(result3.Chain, jit: true);
inst3.Set("atk", 10f).Set("mult", 3f).Set("def", 4f);
float value3 = inst3.Run();
Console.WriteLine($"(10 * 3) - 4 * 0.5 = {value3}  (resolved via cache)");
