# 迁移指南

本文档记录 FluxFormula 各主版本之间的 breaking changes 及迁移步骤。

当前最新版本为 3.2.x。

---

## 从 3.0 迁移到 3.2

### 概述

3.2 将链式公式表示从 `FluxFormula`/`FluxModifier` 中提取为独立 `FluxChain<TData, TDef>` 类型。`FluxFormula` 永远是原子公式，`FluxChain` 永远是链式公式：两个形态由类型系统而非运行时布尔字段区分。

### Breaking Changes

| 变更 | 说明 | 迁移 |
|------|------|------|
| `Connect()` 返回 `FluxChain` | `FluxFormula.Connect()` 和 `FluxModifier.Connect()` 现在返回 `FluxChain<TData, TDef>` 而非 `FluxFormula`/`FluxModifier` | 将接收 `Connect` 返回值的变量类型从 `var`/`FluxFormula` 改为 `FluxChain`，或显式调 `.ToAtomic()` |
| `IsChained` 移除 | `FluxFormula` 和 `FluxModifier` 不再有 `IsChained` 属性 | 删除所有 `if (formula.IsChained)` 分支：不再需要 |
| `ChainLength` → `FluxChain.Length` | 链长属性移至 `FluxChain` | `chain.ChainLength` → `chain.Length` |
| `GetChainLinks()` → `FluxChain.GetLinks()` | 链结构访问移至 `FluxChain` | `formula.GetChainLinks()` → `chain.GetLinks()` |
| `ToAtomic()` 移至 `FluxChain` | `FluxFormula.ToAtomic()` 移除；`FluxChain.ToAtomic()` 返回 `FluxFormula` | `chain.ToAtomic()` 显式合并，一次分配 |
| `VffResolveResult.Formula` → `.Chain` | VFF 解析结果字段重命名 | `result.Formula` → `result.Chain`（`FluxChain` 可直接传入 `Instantiate`） |

### 新增

- `FluxChain<TData, TDef>` — 独立链式公式类型，`Instantiate(FluxChain)` 逐 link 求值
- `FluxChain.GetLinks()` — 零拷贝 span 访问链结构
- `FluxChain.Connect(FluxModifier)` — 链式追加，返回新 `FluxChain`
- `FluxAssembler.Instantiate(FluxChain, bool)` — 链式公式实例化重载

### 消除的隐式分配

`FluxFormula.Raw()` 和 `FluxFormula.ToBytes()` 对链式公式的隐式 `ToAtomic()` 分配已消除：原子公式永远 O(1)。

---

## 从 1.5 迁移到 1.7

无 breaking changes。

### 新增

- **编译缓存管线**：`DualHash64` + `FormulaCache`。编译和 JIT 委托自动缓存，零用户介入。
- **Blob 构建管线**：`FluxBlobBuilder` 扫描所有 `FluxAsset` → 拼接 blob → 生成 C# 偏移表。Play Build 自动触发。
- **VFF 虚拟公式**：`VffFormat` 实现持久化公式引用 + 参数覆写，DLL 式符号解析。
- **FluxConfig 全局配置**：`FluxConfig` 替代硬编码常量。Unity 端通过 `FluxConfigAsset` ScriptableObject 注入。
- **MaxRegister 按需分配**：公式头部存储编译期最高寄存器号，运行时按需栈分配。
- **Per-link JIT 链式求值**：JIT 路径不再强制 ToAtomic：每 link 独立编译 delegate，`SetIndex(0, prevResult)` 串联。
- **FluxFormula.Addressables.UniTask**：UniTask 异步加载扩展包。

---

## 从 1.x 迁移到 2.0

### 概述

2.0 将公式链的内部表示从公开 API 表面移到实现深处，统一了对外接口形态，并收紧了 `Connect` 的调用约束。

影响范围：直接使用 `ChainLink`、`IsChained`、`ChainLength` 的代码需调整。使用 `Connect` 串联公式的代码需在右侧传入 Modifier。

### Breaking Changes

| 变更 | 1.x 行为 | 2.0 行为 | 迁移方式 |
|------|----------|----------|----------|
| `Connect(next)` | 接受任意 `FluxFormula` | 要求 `next` 为 Modifier | 右侧公式先调用 `.ToMultiplier()` 再传入 |
| `ChainLink` / `IsChained` / `ChainLength` / `GetChainLinks()` | `public`（1.x）→ `internal`（2.0 初期） | `public`（2.0 最终） | 高级用户可通过 `GetChainLinks()` 访问链结构，配合 `VffFormat.ToBytes()` 持久化 VFF |

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

## 从 2.x 迁移到 3.0

### 概述

3.0 完成两个方向的重构：(1) `TOper` 泛型参数移除，`TDef` 进入 `FluxFormula` 签名；(2) Formula/Modifier 类型分裂，`FluxModifier<TData, TDef>` 作为独立公开类型。

影响范围：所有 Definition 实现、`Connect` 调用点、Modifier 变量声明。

### Breaking Changes

| 变更 | 2.x 行为 | 3.0 行为 | 迁移方式 |
|------|----------|----------|----------|
| `FluxFormula<TData, TOper>` | 两个泛型参数 | `FluxFormula<TData, TDef>` — `TDef` 替代 `TOper` | 替换所有泛型参数 |
| `ToMultiplier()` | 返回 `FluxFormula` | `ToModifier()` 返回 `FluxModifier<TData, TDef>` | 重命名调用 |
| `Connect(FluxFormula)` | 接受 `FluxFormula`，运行时检查 Modifier | `Connect(FluxModifier)` — 类型系统保证 | 传入前调用 `.ToModifier()` |
| `FluxType` 枚举 | `public` | `internal` — 不再暴露 | 移除所有 `FluxType` 断言 |
| `FluxFormula.Type` | `public` | `internal` — 类型身份由 struct 保证 | 移除 `.Type` 访问 |
| `FluxModifier` | 不存在 | 新增独立 struct，无 `Instantiate()`/`Run()` | Modifier 变量类型改为 `FluxModifier<TData, TDef>` |
| `Modifier.Run()` | 抛 `InvalidOperationException` | 编译错误：`FluxModifier` 无此方法 | 通过 `ToFormula()` 转换后求值 |
| `sizeof(TOper) != 1` 检查 | 运行时异常 | 消除：`byte` 始终 1 字节 | 无需迁移 |

### 新增

- `FluxModifier<TData, TDef>` — 类型安全的 Modifier，编译期防止非法调用
- `FluxFormula.Connect(FluxModifier)` — 类型系统保证 RHS 为 Modifier
- `FluxModifier.Connect(FluxModifier)` — Modifier 间串联
- `FluxModifier.ToFormula(string)` — 转为可求值 Formula
- `FluxModifier.FromBytes()` — 从字节码反序列化 Modifier

### 消除的运行时异常

| 原异常 | 消除方式 |
|--------|---------|
| `Connect 要求 Modifier` | 签名只接受 `FluxModifier` |
| `Modifier cannot run standalone` | `FluxModifier` 无 `Instantiate()` |
| 跨 Definition Connect 无检查 | `FluxFormula<TData, TDef>` 类型级区分 |
| `sizeof(TOper) != 1` | TOper 消失，byte 永远是 1 字节 |

### 版本兼容

| FluxFormula | Unity |
|-------------|-------|
| 3.0 | 2021.3+ |

---

## 从 1.3 迁移到 2.0

无 breaking changes。

## 从 1.1 迁移到 1.2

无 breaking changes。
