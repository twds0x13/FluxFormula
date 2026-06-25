# FormulaCache

Formula compilation artifact cache: an open-addressing hashmap mapping `DualHash64 → (pointer + length)`. Zero dynamic allocation over the singleton lifetime — all arrays are allocated once at construction.

## Signature

```csharp
public unsafe class FormulaCache : IFluxCacheProvider
```

## Design Highlights

| Feature | Description |
|------|------|
| **Open addressing + linear probing** | No linked-list pointers, zero GC pressure |
| **Tombstone markers** | Mark deleted slots as Tombstone (-3); insertions reuse tombstones, preventing eviction from breaking probe chains |
| **Ring eviction** | Overwrites the oldest entry when full (ring write head `_ringHead`), rather than returning an error |
| **Split hash key storage** | Keys stored as two independent `ulong[]` arrays (xxHash64 + FNV-1a 64), avoiding 16-byte alignment overhead |
| **Value semantics** | `length ≥ 0` = bytecode pointer `(byte*, length)`; `DelegateSlot (-2)` = GCHandle for JIT delegate |
| **Tombstone compaction** | Automatically triggers a full-table Compact (rehash live entries) when tombstones exceed `Capacity / 4` |
| **Single-threaded design** | No locking. Designed for Unity main thread usage. Add external locking for multi-threaded scenarios |

## Constants

| Constant | Value | Description |
|------|------|------|
| `Empty` | `-1` | Empty slot — never written to |
| `Tombstone` | `-3` | Tombstone — previously held a value that was evicted |
| `DelegateSlot` | `-2` | JIT delegate cache slot marker |

## Static Members

### Instance

```csharp
public static FormulaCache Instance { get; }
```

Global singleton cache instance. Lazily initialized on first access (`Capacity` taken from `FluxConfig.Current.FormulaCacheCapacity`, default 2048). Replaces the removed `ConnectCache`.

### HitCount / MissCount

```csharp
public static long HitCount { get; }
public static long MissCount { get; }
```

Cache hit/miss counters (incremented only for the singleton instance). For diagnostics.

### Reset

```csharp
public static void Reset()
```

Resets the singleton cache: creates a fresh `FormulaCache` instance, zeros all counters. All old cache entries (blob bytecode pointers, JIT delegates) are discarded.

## Properties

| Property | Type | Description |
|------|------|------|
| `Count` | `int` | Number of live entries (excluding tombstones) |
| `TombstoneCount` | `int` | Current tombstone count (for diagnostics) |
| `Capacity` | `int` | Slot count (read from `FluxConfig` at construction) |

## Methods

### TryGet

```csharp
public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
```

Looks up cached bytecode by `DualHash64`. On success, `ptr` points to the bytecode start address in pinned memory, `length` is the byte count. Returns `false` on miss.

Internally calls `FindSlot(key)` for open-addressing lookup: linear probing from `HashToSlot(xxHash)`. Tombstones do not break the probe chain; `Empty` slots terminate it (tombstones only occupy space without interrupting, ensuring entries inserted before a tombstone was created remain reachable).

### Put

```csharp
public void Put(DualHash64 key, IntPtr ptr, int length)
```

Writes a bytecode pointer into the cache. If the key already exists, updates in place. Otherwise prefers empty slots, then reuses tombstones. Falls back to ring eviction (`EvictAndWrite`) when the table is full.

```csharp
// Typical usage: writing from blob to cache
unsafe
{
    fixed (byte* p = bytecode)
        FormulaCache.Instance.Put(hash, (IntPtr)p, bytecode.Length);
}
```

### TryGetDelegate

```csharp
public bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
```

Looks up a cached JIT delegate (`GCHandle` pointer). Only matches entries where `_valueLengths[slot] == DelegateSlot`. On hit, `gcHandle` is the result of `GCHandle.ToIntPtr()`; the caller retrieves the delegate via `GCHandle.FromIntPtr(gcHandle).Target`.

### PutDelegate

```csharp
public void PutDelegate(DualHash64 key, IntPtr gcHandle)
```

Writes a JIT delegate's `GCHandle` into the cache. If the key already exists and the old value is a delegate, frees the old `GCHandle` before writing the new one. Other behavior matches `Put` (prefer empty slots/tombstones; ring eviction when full).

## Diagnostics

### DumpSlot

```csharp
internal string DumpSlot(int slot)
```

Returns a status string for the given slot (debug only), formatted as:
- `[N] Empty`
- `[N] Tombstone`
- `[N] Key={xxHash}{fnvHash} Len={length}`
- `[N] Unknown({state})`

## Internals

### Insertion Flow (FindInsertSlot)

```
HashToSlot(xxHash64) → linear probe:
  - Empty found → return first tombstone if any, otherwise this empty slot
  - Tombstone → record position, continue probing
  - Live entry → continue probing
  - Full table → return -1, triggering EvictAndWrite
```

### Ring Eviction (EvictAndWrite)

Overwrites the `_ringHead` slot: if the original entry was live, marks it as a tombstone (`_tombstoneCount++`); if it was a delegate, additionally frees the `GCHandle`. After writing the new value, advances `_ringHead` by one. Triggers full-table compaction when tombstones exceed `Capacity / 4`.

### Full-Table Compaction (Compact)

Collects all live entries (`_valueLengths[i] >= 0` or `== DelegateSlot`), clears the entire table, and rebuilds all probe chains. Tombstone counter is reset to zero. Time complexity O(n), triggered only when tombstone density exceeds 25%.

## Usage Example

```csharp
// — Write —
byte[] bytecode = formula.ToBytes();
var hash = DualHash64.Compute(bytecode);
unsafe
{
    fixed (byte* p = bytecode)
        FormulaCache.Instance.Put(hash, (IntPtr)p, bytecode.Length);
}

// — Read —
if (FormulaCache.Instance.TryGet(hash, out IntPtr ptr, out int len))
{
    var span = new ReadOnlySpan<byte>((void*)ptr, len);
    var loaded = FluxFormula<float, MathDef>.FromBytes(span);
}

// — JIT delegate caching —
var compiledFunc = ...; // generated by FluxJITCompiler
var gcHandle = GCHandle.Alloc(compiledFunc);
FormulaCache.Instance.PutDelegate(hash, GCHandle.ToIntPtr(gcHandle));

if (FormulaCache.Instance.TryGetDelegate(hash, out IntPtr handlePtr))
{
    var func = (Func<float[], float>)GCHandle.FromIntPtr(handlePtr).Target;
    float result = func(inputs);
}

// — Diagnostics —
Console.WriteLine($"Hit: {FormulaCache.HitCount}, Miss: {FormulaCache.MissCount}");
Console.WriteLine($"Slots: {FormulaCache.Instance.Count}/{FormulaCache.Instance.Capacity}");
Console.WriteLine($"Tombstones: {FormulaCache.Instance.TombstoneCount}");

// — Reset —
FormulaCache.Reset();
```

## See Also

- [VffFormat](./vff-format) — VFF resolution depends on FormulaCache for referenced formula lookup
- [FluxFormula](./flux-formula) — Formula compilation and cache keys
- [FluxConfig](./flux-config) — `FormulaCacheCapacity` configuration
- [IFluxCacheProvider](./iflux-cache-provider) — Replaceable cache backend interface
- [DualHash64](./dualhash64) — Cache key
