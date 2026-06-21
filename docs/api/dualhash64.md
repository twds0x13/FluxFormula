# DualHash64

128 位双重哈希：xxHash64（高 64 位）|| FNV-1a 64（低 64 位）。两个内部结构正交的非密码学哈希组合，用于预防结构性碰撞注入。

## 定位

在 FluxFormula 中，此类型是公式字节码**完整性验证**的核心：偏移表存储每个公式的期望 `DualHash64`，Blob 仅提供原始字节码。加载时计算字节码的 `DualHash64` 与期望值比对。

## 签名

```csharp
public readonly struct DualHash64 : IEquatable<DualHash64>
```

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `XxHash64` | `ulong` | xxHash64 结果（高 64 位） |
| `FnvHash64` | `ulong` | FNV-1a 64 结果（低 64 位） |

## 构造

```csharp
public DualHash64(ulong xxHash64, ulong fnvHash64)
```

通常不直接调用——使用静态工厂方法 `Compute` / `ComputeSeeded` / `Combine`。

## 静态方法

### Compute

```csharp
public static DualHash64 Compute(ReadOnlySpan<byte> data)
```

对数据计算双重哈希。返回的 `DualHash64` 可用于与偏移表中的期望值做完整性比对。

### ComputeSeeded

```csharp
public static DualHash64 ComputeSeeded(ReadOnlySpan<byte> data, ulong xxhSeed)
```

带种子的 xxHash64 计算。种子不同则相同数据产生不同哈希。用于需要独立哈希空间的场景（如连接链路的递进 key 计算）。FNV 侧无种子概念，保持正交。

### Combine

```csharp
public static DualHash64 Combine(DualHash64 accumulated, DualHash64 next)
```

累进组合 hash——为 Connect 链路 key 计算设计。顺序敏感：`Combine(a, Combine(b, c)) ≠ Combine(b, Combine(a, c))`。O(1) 时间，不需要重新扫描字节码。

### Parse

```csharp
public static DualHash64 Parse(ReadOnlySpan<char> hex)
```

从 32 字符十六进制字符串解析。大写/小写兼容。前 16 字符 = xxHash64，后 16 字符 = FNV-1a 64。

## 实例方法

| 方法 | 说明 |
|------|------|
| `Equals(DualHash64)` | 两个分量同时相等 |
| `Equals(object)` | 装箱比较 |
| `GetHashCode()` | 两个 64-bit 折叠为单个 32-bit |
| `ToString()` | 32 字符十六进制：`{XxHash64:X16}{FnvHash64:X16}` |

## 运算符

```csharp
public static bool operator ==(DualHash64 left, DualHash64 right)
public static bool operator !=(DualHash64 left, DualHash64 right)
```

## 使用示例

```csharp
byte[] bytecode = formula.ToBytes();
var hash = DualHash64.Compute(bytecode);

// 缓存键
FormulaCache.Instance.Put(hash, ptr, length);

// Chain key 累进
var chainKey = hashA;
chainKey = DualHash64.Combine(chainKey, hashB);

// 字符串往返
string hex = hash.ToString();           // "A1B2...F3E4"
var parsed = DualHash64.Parse(hex);     // hash == parsed
```

## 参见

- [FormulaCache](./formula-cache) — 以 DualHash64 为键的缓存
- [FormulaFormat](./formula-format) — 字节码序列化格式
