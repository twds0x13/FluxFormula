# IFluxCacheProvider

编译缓存后端接口。定义字节码和 JIT delegate 的写入 / 读取四组操作。

## 签名

```csharp
public interface IFluxCacheProvider
```

## 设计约束

- 所有方法使用 `IntPtr` 而非 `byte*`：实现者不需要 `unsafe` 上下文
- Delegate 通过 `GCHandle` 转 `IntPtr` 存储，调用方负责创建和释放
- 缓存的生命周期管理（指针有效期、GCHandle 存活期）由实现者完全负责

## 方法

### TryGet

```csharp
bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
```

按双重哈希查找缓存的字节码。命中时 `ptr` 指向字节码起始地址，`length` 为字节数。未命中返回 `false`。

### Put

```csharp
void Put(DualHash64 key, IntPtr ptr, int length)
```

将字节码写入缓存。若同一 key 已存在则原地更新值。

### TryGetDelegate

```csharp
bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
```

查找缓存的 JIT delegate。命中时 `gcHandle` 为 `GCHandle.ToIntPtr()` 的结果，调用方通过 `GCHandle.FromIntPtr(gcHandle).Target` 取回 delegate。

### PutDelegate

```csharp
void PutDelegate(DualHash64 key, IntPtr gcHandle)
```

将 JIT delegate 写入缓存。调用方先 `GCHandle.Alloc(func)`，再 `GCHandle.ToIntPtr(handle)` 传入。

## 内置实现

`FormulaCache` 是默认实现：2048 槽开放寻址哈希表，单例生命周期零分配。

## 自定义实现示例

```csharp
public class DiskCache : IFluxCacheProvider
{
    public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
    {
        // 从磁盘读取...
    }

    public void Put(DualHash64 key, IntPtr ptr, int length)
    {
        // 写入磁盘...
    }

    public bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle)
    {
        // delegate 不适合持久化，返回 false
        gcHandle = IntPtr.Zero;
        return false;
    }

    public void PutDelegate(DualHash64 key, IntPtr gcHandle)
    {
        // 不持久化 delegate，空操作
    }
}
```

## 参见

- [FormulaCache](./formula-cache) — 默认实现
- [DualHash64](./dualhash64) — 缓存键
