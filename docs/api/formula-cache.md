# FormulaCache

公式编译产物缓存：`DualHash64 → (指针 + 长度)` 的开放寻址哈希表。单例生命周期内零动态分配，所有数组在构造时一次性分配。

## 签名

```csharp
public unsafe class FormulaCache : IFluxCacheProvider
```

## 设计要点

| 特性 | 说明 |
|------|------|
| **开放寻址 + 线性探测** | 无链表指针，无 GC 压力 |
| **墓碑标记** | 删除时留墓碑（Tombstone = -3），插入时复用，避免驱逐破坏探测链 |
| **环形驱逐** | 缓存满时覆盖最老条目（环形写入头 `_ringHead`），而非返回错误 |
| **双哈希键存储** | 键存储为两个独立 `ulong[]`（xxHash64 + FNV-1a 64），避免 16 字节对齐损失 |
| **值语义区分** | `length ≥ 0` = 字节码指针 `(byte*, length)`；`DelegateSlot (-2)` = JIT delegate 的 GCHandle |
| **墓碑压缩** | 墓碑超过 `Capacity / 4` 时自动全表 Compact（rehash 存活条目） |
| **单线程设计** | 无锁。Unity 主线程单线程使用。多线程场景外层加锁 |

## 常量

| 常量 | 值 | 说明 |
|------|------|------|
| `Empty` | `-1` | 空槽位，从未写入过 |
| `Tombstone` | `-3` | 墓碑，曾经有值但已被驱逐 |
| `DelegateSlot` | `-2` | JIT delegate 缓存槽位标记 |

## 静态成员

### Instance

```csharp
public static FormulaCache Instance { get; }
```

全局单例缓存实例。首次访问时延迟初始化（`Capacity` 取自 `FluxConfig.Current.FormulaCacheCapacity`，默认 2048）。替代已移除的 `ConnectCache`。

### HitCount / MissCount

```csharp
public static long HitCount { get; }
public static long MissCount { get; }
```

缓存命中/未命中计数（仅单例实例增量）。诊断用。

### Reset

```csharp
public static void Reset()
```

重置单例缓存：创建全新的 `FormulaCache` 实例，清零所有计数器。所有旧缓存条目（blob 字节码指针、JIT delegate）均被丢弃。

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Count` | `int` | 当前存活条目数（不含墓碑） |
| `TombstoneCount` | `int` | 当前墓碑数（诊断用） |
| `Capacity` | `int` | 槽位数（构造时从 `FluxConfig` 读取） |

## 方法

### TryGet

```csharp
public bool TryGet(DualHash64 key, out IntPtr ptr, out int length)
```

按 `DualHash64` 查找缓存的字节码。返回 `true` 时 `ptr` 指向 pinned 内存中的字节码起始地址，`length` 为字节数。未命中返回 `false`。

内部调用 `FindSlot(key)` 进行开放寻址查找：从 `HashToSlot(xxHash)` 开始线性探测，墓碑不阻断探测链，`Empty` 槽位终止探测（`Tombstone` 仅占位不中断，确保 Tombstone 发生前的插入仍能被探测到）。

### Put

```csharp
public void Put(DualHash64 key, IntPtr ptr, int length)
```

将字节码指针写入缓存。若 key 已存在则原地更新值。否则优先占用空槽位，其次复用墓碑。缓存满时走环形驱逐路径（`EvictAndWrite`）。

```csharp
// 典型调用：从 blob 写入缓存
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

查找缓存的 JIT delegate（`GCHandle` 指针）。仅匹配 `_valueLengths[slot] == DelegateSlot` 的条目。命中时 `gcHandle` 为 `GCHandle.ToIntPtr()` 的返回值，调用方通过 `GCHandle.FromIntPtr(gcHandle).Target` 获取 delegate。

### PutDelegate

```csharp
public void PutDelegate(DualHash64 key, IntPtr gcHandle)
```

将 JIT delegate 的 `GCHandle` 写入缓存。若 key 已存在且旧值为 delegate，先释放旧的 `GCHandle` 再写入新值。其他行为与 `Put` 一致（优先空槽位/墓碑，满时环形驱逐）。

## 诊断方法

### DumpSlot

```csharp
internal string DumpSlot(int slot)
```

返回指定槽位的状态字符串（仅调试用），格式：
- `[N] Empty`
- `[N] Tombstone`
- `[N] Key={xxHash}{fnvHash} Len={length}`
- `[N] Unknown({state})`

## 内部机制

### 插入流程（FindInsertSlot）

```
HashToSlot(xxHash64) → 线性探测：
  - Empty 出现 → 有墓碑返回墓碑，无墓碑返回此空位
  - Tombstone → 记录第一个墓碑位置，继续探测
  - 存活条目 → 继续探测
  - 全表满 → 返回 -1，触发 EvictAndWrite
```

### 环形驱逐（EvictAndWrite）

覆盖 `_ringHead` 槽位：若原条目为存活条目则标记为墓碑（`_tombstoneCount++`），若为 delegate 则额外释放 `GCHandle`。写入新值后 `_ringHead` 前移一位。墓碑超过 `Capacity / 4` 时触发全表压缩。

### 全表压缩（Compact）

收集所有存活条目（`_valueLengths[i] >= 0` 或 `== DelegateSlot`），清空全表，重建所有条目的探测链。墓碑计数器清零。时间复杂度 O(n)，仅在墓碑密度超过 25% 时触发。

## 使用示例

```csharp
// — 写入 —
byte[] bytecode = formula.ToBytes();
var hash = DualHash64.Compute(bytecode);
unsafe
{
    fixed (byte* p = bytecode)
        FormulaCache.Instance.Put(hash, (IntPtr)p, bytecode.Length);
}

// — 读取 —
if (FormulaCache.Instance.TryGet(hash, out IntPtr ptr, out int len))
{
    var span = new ReadOnlySpan<byte>((void*)ptr, len);
    var loaded = FluxFormula<float, MathDef>.FromBytes(span);
}

// — JIT delegate 缓存 —
var compiledFunc = ...; // 由 FluxExprCompiler 生成
var gcHandle = GCHandle.Alloc(compiledFunc);
FormulaCache.Instance.PutDelegate(hash, GCHandle.ToIntPtr(gcHandle));

if (FormulaCache.Instance.TryGetDelegate(hash, out IntPtr handlePtr))
{
    var func = (Func<float[], float>)GCHandle.FromIntPtr(handlePtr).Target;
    float result = func(inputs);
}

// — 诊断 —
Console.WriteLine($"Hit: {FormulaCache.HitCount}, Miss: {FormulaCache.MissCount}");
Console.WriteLine($"Slots: {FormulaCache.Instance.Count}/{FormulaCache.Instance.Capacity}");
Console.WriteLine($"Tombstones: {FormulaCache.Instance.TombstoneCount}");

// — 重置 —
FormulaCache.Reset();
```

## 参见

- [VffFormat](./vff-format) — VFF resolve 依赖 FormulaCache 查找被引用公式
- [FluxFormula](./flux-formula) — 公式编译与缓存键
- [FluxConfig](./flux-config) — `FormulaCacheCapacity` 配置
- [IFluxCacheProvider](./iflux-cache-provider) — 可替换缓存后端接口
- [DualHash64](./dualhash64) — 缓存键
