# DualHash64

128-bit dual hash: xxHash64 (upper 64 bits) || FNV-1a 64 (lower 64 bits). Combines two structurally orthogonal non-cryptographic hashes to prevent structural collision injection.

## Purpose

In FluxFormula, this type is the core of formula bytecode **integrity verification**: the offset table stores the expected `DualHash64` for each formula; the blob provides only raw bytecode. At load time, the bytecode's `DualHash64` is computed and compared against the expected value.

## Signature

```csharp
public readonly struct DualHash64 : IEquatable<DualHash64>
```

## Fields

| Field | Type | Description |
|------|------|------|
| `XxHash64` | `ulong` | xxHash64 result (upper 64 bits) |
| `FnvHash64` | `ulong` | FNV-1a 64 result (lower 64 bits) |

## Construction

```csharp
public DualHash64(ulong xxHash64, ulong fnvHash64)
```

Typically not called directly — use the static factory methods `Compute`, `ComputeSeeded`, or `Combine`.

## Static Methods

### Compute

```csharp
public static DualHash64 Compute(ReadOnlySpan<byte> data)
```

Computes the dual hash over data. The returned `DualHash64` can be used for integrity comparison against the expected value in the offset table.

### ComputeSeeded

```csharp
public static DualHash64 ComputeSeeded(ReadOnlySpan<byte> data, ulong xxhSeed)
```

Seeded xxHash64 computation. Different seeds produce different hashes for the same data. Useful for independent hash spaces (e.g., progressive key computation for connect chains). The FNV side has no seed concept and remains orthogonal.

### Combine

```csharp
public static DualHash64 Combine(DualHash64 accumulated, DualHash64 next)
```

Progressive hash combination — designed for Connect chain key computation. Order-sensitive: `Combine(a, Combine(b, c)) ≠ Combine(b, Combine(a, c))`. O(1) time; does not require re-scanning bytecode.

### Parse

```csharp
public static DualHash64 Parse(ReadOnlySpan<char> hex)
```

Parses from a 32-character hexadecimal string. Case-insensitive. First 16 chars = xxHash64, last 16 chars = FNV-1a 64.

## Instance Methods

| Method | Description |
|------|------|
| `Equals(DualHash64)` | Both components equal |
| `Equals(object)` | Boxed comparison |
| `GetHashCode()` | Folds two 64-bit hashes into a single 32-bit |
| `ToString()` | 32-char hex: `{XxHash64:X16}{FnvHash64:X16}` |

## Operators

```csharp
public static bool operator ==(DualHash64 left, DualHash64 right)
public static bool operator !=(DualHash64 left, DualHash64 right)
```

## Usage

```csharp
byte[] bytecode = formula.ToBytes();
var hash = DualHash64.Compute(bytecode);

// Cache key
FormulaCache.Instance.Put(hash, ptr, length);

// Chain key progression
var chainKey = hashA;
chainKey = DualHash64.Combine(chainKey, hashB);

// String round-trip
string hex = hash.ToString();           // "A1B2...F3E4"
var parsed = DualHash64.Parse(hex);     // hash == parsed
```

## See Also

- [FormulaCache](./formula-cache) — Cache keyed by DualHash64
- [FormulaFormat](./formula-format) — Bytecode serialization format
