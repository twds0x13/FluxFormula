# 迁移指南

本文档记录 FluxFormula 各主版本之间的 breaking changes 及迁移步骤。

当前最新版本为 2.0.0。

---

## 从 1.5 迁移到 1.7

无 breaking changes。

### 新增

- **编译缓存管线**：`DualHash64` + `FormulaCache`。编译和 JIT 委托自动缓存，零用户介入。
- **Blob 构建管线**：`FluxBlobBuilder` 扫描所有 `FluxAsset` → 拼接 blob → 生成 C# 偏移表。Play Build 自动触发。
- **VFF 虚拟公式**：`VffFormat` 实现持久化公式引用 + 参数覆写，DLL 式符号解析。
- **FluxConfig 全局配置**：`FluxConfig` 替代硬编码常量。Unity 端通过 `FluxConfigAsset` ScriptableObject 注入。
- **MaxRegister 按需分配**：公式头部存储编译期最高寄存器号，运行时按需栈分配。
- **Per-link JIT 链式求值**：JIT 路径不再强制 ToAtomic——每 link 独立编译 delegate，`SetIndex(0, prevResult)` 串联。
- **FluxFormula.Addressables.UniTask**：UniTask 异步加载扩展包。

---

## 从 1.x 迁移到 2.0

### 概述

2.0 将公式链的内部表示从公开 API 表面移到实现深处，统一了对外接口形态，并收紧了 `Connect` 的调用约束。

影响范围：直接使用 `ChainLink`、`IsChained`、`ChainLength` 的代码需调整。使用 `Connect` 串联公式的代码需在右侧传入 Modifier。

### Breaking Changes

| 变更 | 1.x 行为 | 2.0 行为 | 迁移方式 |
|------|----------|----------|----------|
| `IsChained` | `public bool` | `internal` | 移除外部引用。消费者不再感知链式/原子双形态 |
| `ChainLength` | `public int` | `internal` | 同上 |
| `GetChainLinks()` | `public` | `internal` | 同上 |
| `ChainLink` 结构体 | 可公开引用 | `internal struct` | 同上 |
| `Connect(next)` | 接受任意 `FluxFormula` | 要求 `next` 为 Modifier | 右侧公式先调用 `.ToMultiplier()` 再传入 |

### 行为变更（非 API 签名）

- `Raw()` / `ToBytes()`：链式公式自动合并为原子公式后返回，不再返回空/损坏数据。
- `Connect` 语义澄清：左侧公式的 R1 输出流入右侧 Modifier 的 Bus(R1) 寄存器。传入非 Modifier 不再静默覆盖首操作数，改为显式抛出异常。

### 版本兼容

| FluxFormula | Unity |
|-------------|-------|
| 2.0 | 2021.3+ |

### 新增

- `Connect` 的 Modifier 语法更安全：`formula.Connect(modifier)` 明确表达"前者输出注入后者"的意图
- 所有链路追踪 API 内部化后，公开 API 表面缩小 6 个方法，降低认知负担

---

## 从 1.2 迁移到 1.3

无 breaking changes。

## 从 1.1 迁移到 1.2

无 breaking changes。
