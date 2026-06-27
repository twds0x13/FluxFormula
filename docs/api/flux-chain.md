# FluxChain

不可直接求值的链式公式。由 `FluxFormula.Connect()` 或 `FluxModifier.Connect()` 产生，存储多次串联的多段字节码序列。需通过 `ToAtomic()` 转为 `FluxFormula` 后求值，或直接传入 `FluxAssembler.Instantiate(FluxChain)` 逐 link 执行。

## 签名

```csharp
public readonly struct FluxChain<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

## 获取方式

`FluxChain` 不由用户直接构造。通过以下 API 获取：

```csharp
// FluxFormula.Connect(FluxModifier) → FluxChain
FluxChain<float, MathDef> chain = formula.Connect(modifier);

// FluxModifier.Connect(FluxModifier) → FluxChain
FluxChain<float, MathDef> modChain = modifier1.Connect(modifier2);

// VffFormat 解析结果
var result = VffFormat.Resolve<float, MathDef>(hash);
FluxChain<float, MathDef> vffChain = result.Chain;
```

## 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Length` | `int` | 链中的链接数。空链为 0 |
| `Empty` | `FluxChain<TData, TDef>`（静态） | 空链（Length=0），Connect 的单位元 |

## 方法

### Connect

```csharp
public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
```

在当前链末尾追加一个 Modifier。返回新 `FluxChain`（原链不变）。

- `next` 为空（Count=0）时返回自身
- 当前链为空时返回单 link 的 `FluxChain`
- 可连续追加：`chain.Connect(m1).Connect(m2).Connect(m3)`

### ToAtomic

```csharp
public FluxFormula<TData, TDef> ToAtomic()
```

将所有 link 的字节码完整拼接为单个 `Instruction[]`，返回原子 `FluxFormula`。合并后可直接 `Instantiate` + `Run`。

- 单次堆分配（`new Instruction[totalCount]`）
- 合并后的字节码还原为独立可求值公式
- `FluxAssembler.Instantiate(FluxChain)` 对长链（>8）自动调用此方法

### GetLinks

```csharp
public ReadOnlySpan<ChainLink> GetLinks()
```

获取链式链接的只读视图。返回 `ChainLink` 结构体的 span，零拷贝。

```csharp
var links = chain.GetLinks();
foreach (var link in links)
    Console.WriteLine($"Key={link.Key}, Instructions={link.InstructionCount}");
```

### GetByteHash

```csharp
public DualHash64 GetByteHash()
```

计算链式公式的组合哈希。用于缓存查找：与 `ToAtomic().GetByteHash()` 结果不同（链式哈希反映 link 组合，原子哈希反映合并后字节码）。

## 求值路径

| 路径 | 行为 |
|------|------|
| `Instantiate(chain, jit: false)` | 短链（≤8）逐 link 解释器求值，通过 R1 总线串联；长链自动调 `ToAtomic` 后单次求值 |
| `Instantiate(chain, jit: true)` | 逐 link JIT delegate 串联，各 link 独立缓存命中 |
| `Instantiate(chain.ToAtomic(), ...)` | 合并后作为普通原子公式求值 |

## 与 FluxFormula 的区分

| 特性 | `FluxFormula` | `FluxChain` |
|------|:---:|:---:|
| 可直接求值 | 是 | 否（需 `Instantiate(FluxChain)` 或 `ToAtomic()`） |
| `Raw()` | O(1)，零分配 | 无此方法 |
| `ToBytes()` | O(1) | 无此方法 |
| 内部表示 | `Instruction[]`（原子字节码） | `ChainLink[]`（引用切片） |
| 产生方式 | `Compile()`、`FromBytes()`、`ToAtomic()` | `Connect()`、VFF 解析 |
| 链式操作 | `Connect()` 返回 `FluxChain` | `Connect()` 返回 `FluxChain` |

## 参见

- [FluxFormula / FluxModifier](./flux-formula) — 原子公式与修饰符
- [FluxAssembler](./flux-assembler) — `Instantiate(FluxChain)` 重载
- [VffFormat](./vff-format) — VFF 解析产出 `FluxChain`
- [ChainLink 深度解析](../technical/chainlink-deep-dive) — 逐 link JIT 缓存原理
