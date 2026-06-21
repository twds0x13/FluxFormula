# 迁移指南

本文档记录 FluxFormula 各主版本之间的 breaking changes 及迁移步骤。

当前最新版本为 1.7.0，尚无已知的 breaking changes。下文模板供后续版本迭代时使用。

---

## 从 1.5 迁移到 1.7

无 breaking changes。

### 新增

- **编译缓存管线**：`DualHash64` + `FormulaCache` + `ConnectCache`。编译和 JIT 委托自动缓存，零用户介入。
- **Blob 构建管线**：`FluxBlobBuilder` 扫描所有 `FluxAsset` → 拼接 blob → 生成 C# 偏移表。Play Build 自动触发。
- **VFF 虚拟公式**：`VffFormat` 实现持久化公式引用 + 参数覆写，DLL 式符号解析。
- **FluxConfig 全局配置**：`FluxConfig` 替代硬编码常量。Unity 端通过 `FluxConfigAsset` ScriptableObject 注入。
- **MaxRegister 按需分配**：公式头部存储编译期最高寄存器号，运行时按需栈分配。
- **Per-link JIT 链式求值**：JIT 路径不再强制 ToAtomic——每 link 独立编译 delegate，`SetIndex(0, prevResult)` 串联。
- **FluxFormula.Addressables.UniTask**：UniTask 异步加载扩展包。

---

## 从 1.x 迁移到 2.0

> 本节为模板。当 2.0 发布时填写实际内容。

### 概述

简述本次升级的影响范围和核心变更。

### Breaking Changes

| 变更 | 1.x 行为 | 2.0 行为 | 迁移方式 |
|------|----------|----------|----------|
| 示例 | | | |

### 弃用

列出已弃用但仍可用的 API，注明计划移除版本。

### 新增

列出取代旧行为或填补缺口的新 API。

---

## 从 1.2 迁移到 1.3

无 breaking changes。

## 从 1.1 迁移到 1.2

无 breaking changes。
