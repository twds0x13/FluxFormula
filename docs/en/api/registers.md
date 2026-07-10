# Registers

Semantic constants for the register model. The single source of truth for all register index references in the project.

## Signature

```csharp
public static class Registers
```

## Constants

| Constant | Type | Value | Description |
|----------|------|-------|-------------|
| `Error` | `byte` | `0` | R0: Error sentinel register. When `Compute()` writes a non-default value to this register, a short-circuit return is triggered |
| `Bus` | `byte` | `1` | R1: Output bus register. The formula's final result is written to this register; chained link values also pass through R1 |

## Design Notes

| Feature | Description |
|---------|-------------|
| R0 Sentinel | The VM checks R0 after every instruction. Once non-default, evaluation terminates immediately and returns the error value |
| R1 Bus | All formula and modifier results land on R1. `Connect()` feeds the previous formula's R1 output as the next modifier's input |
| R2–R254 General | 253 general-purpose registers for compile-time intermediate result allocation. Managed by the `FluxCompiler` internal allocator |

## Usage

```csharp
// Check for errors when using FluxInstance.Run
float result = instance.Run();
// If Compute() returns a non-default value, result inherits it and the VM exits early

// Quick constant reference (most code never needs this directly)
byte errReg = Registers.Error;   // 0
byte busReg = Registers.Bus;     // 1
```

## See Also

- [Instruction](./instruction) — Instruction layout, where the `Dest` field references register indices
- [FluxEvaluator](/technical/pipeline/evaluator) — R0 checking in the interpreter execution loop
- [FluxChain](./flux-chain) — Chain formulas pass values through the R1 bus
