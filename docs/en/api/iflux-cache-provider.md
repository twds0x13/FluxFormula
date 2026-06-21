# IFluxCacheProvider

Compile cache backend interface. Defines four operations: write/read for bytecode and JIT delegates.

## Signature

```csharp
public interface IFluxCacheProvider
```

## Design Constraints

- All methods use `IntPtr` instead of `byte*` — implementers don't need `unsafe` context
- Delegates are stored as `IntPtr` via `GCHandle` — callers are responsible for creating and freeing
- Cache lifecycle management (pointer validity, GCHandle lifetime) is entirely the implementer's responsibility

## Methods

### TryGet

```csharp
bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
```

Looks up cached bytecode by dual hash. On hit, `ptr` points to the bytecode start address and `length` is the byte count. Returns `false` on miss.

### Put

```csharp
void Put(DualHash64 key, IntPtr ptr, int length)
```

Writes bytecode into the cache. Updates in place if the same key already exists.

### TryGetDelegate

```csharp
bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
```

Looks up a cached JIT delegate. On hit, `gcHandle` is the result of `GCHandle.ToIntPtr()`; the caller retrieves the delegate via `GCHandle.FromIntPtr(gcHandle).Target`.

### PutDelegate

```csharp
void PutDelegate(DualHash64 key, IntPtr gcHandle)
```

Writes a JIT delegate into the cache. Caller first does `GCHandle.Alloc(func)`, then passes `GCHandle.ToIntPtr(handle)`.

## Built-in Implementation

`FormulaCache` is the default — a 2048-slot open-addressing hashmap with zero allocation over the singleton lifetime.

## Custom Implementation Example

```csharp
public class DiskCache : IFluxCacheProvider
{
    public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
    {
        // Read from disk...
    }

    public void Put(DualHash64 key, IntPtr ptr, int length)
    {
        // Write to disk...
    }

    public bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
    {
        // Delegates aren't suitable for persistence
        gcHandle = IntPtr.Zero;
        return false;
    }

    public void PutDelegate(DualHash64 key, IntPtr gcHandle)
    {
        // Don't persist delegates — no-op
    }
}
```

## See Also

- [FormulaCache](./formula-cache) — Default implementation
- [DualHash64](./dualhash64) — Cache key
