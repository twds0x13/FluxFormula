# 迁移指南

本文档记录 FluxFormula 各主版本之间的 breaking changes 及迁移步骤。

当前最新版本为 5.1.x。

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

## 从 3.x 迁移到 4.0

### 概述

4.0 将 `IFluxDefinition<TData>.Compute()` 签名从 `ReadOnlySpan<TData>` 改为 `Span<TData>`，允许在 Compute 实现中原地修改寄存器值。同时 `FluxExprCompiler` 和 `IFluxExprDefinition` 从旧名 `FluxJITCompiler`/`IFluxJITDefinition` 重命名而来。

### Breaking Changes

| 变更 | 说明 | 迁移 |
|------|------|------|
| `Compute(byte, Instruction, ReadOnlySpan<TData>)` → `Span<TData>` | 寄存器 span 变为可写 | 多数实现无需修改: `Span<TData>` 与基于 index 的读取完全兼容 |
| `FluxJITCompiler` → `FluxExprCompiler` | 类重命名 | 替换所有类型引用 |
| `IFluxJITDefinition` → `IFluxExprDefinition` | 接口重命名 | 替换所有接口实现 |

### 新增

- `Span<TData>` 寄存器参数支持原地修改，适用于需要在 Compute 中累积状态的场景

---

## 从 4.x 迁移到 5.1

### 概述

5.0 移除 `LiteralParser` 和 `LiteralPattern`，引入 source generator 驱动的字面量模板系统。`[LiteralTemplate]` attribute 使编译器自动生成零分配 span 扫描器，`LexerConfig.LiteralScanner` 委托变为可选。

### Breaking Changes

| 变更 | 4.x 行为 | 5.1 行为 | 迁移 |
|------|----------|----------|------|
| `LexerConfig.LiteralParser` | 存在 | 移除 | 使用 `LexerConfig.LiteralScanner` 委托，或为 TData struct 添加 `[LiteralTemplate]` |
| `LexerConfig.LiteralPattern` | 存在 | 移除 | 由 `[LiteralTemplate]` 的模板字符串替代 |
| `LexerConfig.LiteralScanner` | 必设字段 | 可选（有 `[LiteralTemplate]` 时自动生成） | 现有手写委托继续有效，无需修改 |

### 新增

- `[LiteralTemplate("<float X> <float Y>")]` — 在 struct 上标记模板，source generator 编译期生成扫描代码
- `[ExternalLiteralTemplate(typeof(T), "...")]` — 为第三方无法修改源码的类型注册模板
- `[LiteralTypeAlias("Alias", "float")]` — 自定义类型别名，纯语法糖
- `LiteralTemplateRegistry` — 12 种内置类型的 `Scan_Xxx` 零分配解析方法 (float, double, int, uint, long, ulong, short, ushort, byte, sbyte, bool, char)
- 编译器诊断: FLX001 (模板语法错误), FLX002 (循环依赖), FLX003 (readonly struct 不可用), FLX004 (引用未注册类型)
- `CompactToXml` / `XmlTemplateParser` — 内部模板解析管线
- `CodeEmitter` — AST → C# 源码发射器

### 运行时优先级

`FluxLexer<TData>` 构造时按以下顺序选择扫描器:
1. 生成的 `LiteralScanners.TryGetScanner<TData>()` (有 `[LiteralTemplate]` 时命中)
2. `config.LiteralScanner` 手动委托 (回退)
3. 两者都无则抛出 `ArgumentException`

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
