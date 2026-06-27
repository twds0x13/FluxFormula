# 多语言协作

FluxFormula 文档站当前维护简体中文（`zh-CN`，源语言）和英文（`en`）两个 locale。本文说明非中英母语者如何参与项目、如何请求新语言支持、以及如何借助机器翻译降低语言门槛。

## 当前语言状态

| 语言 | 文档覆盖 | 源语言 | Issue 模板 | 讨论区支持 |
|------|:---:|:---:|:---:|:---:|
| 简体中文 | 完整 | 是 | 中文 | 是 |
| English | 完整（1:1 对译） | 否 | English | 是 |
| 其他语言 | 无 | 否 | 否 | 依情况 |

技术术语表见[翻译指南](/translation-guide#约定)。

## 不懂中文和英文如何参与

项目的主要沟通语言为简体中文和英文。如果对这两种语言都不熟悉：

1. 在 [GitHub Discussions](https://github.com/twds0x13/FluxFormula/discussions) 使用任意语言发帖。机器翻译回复完全可接受——维护者不会评判语言质量。
2. Bug 报告：使用英文 [Bug Report](https://github.com/twds0x13/FluxFormula/issues/new?template=bug_report.yml) 模板，描述部分可混合使用母语。代码和复现步骤本身是通用语言。
3. 代码贡献：PR 描述推荐英文或中文，但代码本身是主要沟通媒介。变量名、类型名、注释已在源码中使用英文。

## 请求新语言支持

新增 locale 有三个前提条件：

1. 至少有维护者或社区成员愿意承担初始翻译和持续同步
2. 该语言有足够规模的潜在用户群
3. 文档站基础设施（VitePress i18n）支持该 locale

流程：

1. 在 [Discussions](https://github.com/twds0x13/FluxFormula/discussions) 的 Ideas 分类下发帖，标题格式：`[i18n] <语言名> locale request`
2. 说明翻译意愿（自译或请求社区协助）和预期维护模式
3. 维护者评估后，如果条件满足，会创建对应 locale 目录结构并提供初始翻译模板

## 机器翻译作为协作桥梁

机器翻译在以下场景中是有效工具：

- **阅读文档**：使用浏览器内置翻译或 DeepL/Google Translate 翻译文档站页面。VitePress 生成的静态 HTML 兼容主流翻译引擎。
- **撰写 Issue**：先用母语写好内容，再用机器翻译转为英文后粘贴。在正文末尾标注 `(machine-translated from <语言名>)`，方便维护者在理解偏差时回溯原文。
- **翻译贡献**：机器翻译可以作为初稿，但人工校对后的版本才能合入文档站。纯机翻页面不满足[翻译指南](/translation-guide)的准确性要求。

## 通用协作入口

无论使用何种语言，以下入口不依赖自然语言能力：

| 路径 | 语言要求 |
|------|:---:|
| Bug 报告（代码 + 复现步骤） | 低 |
| 单元测试贡献 | 低 |
| Benchmark 补充 | 低 |
| API 文档校对（源码注释与文档一致性） | 中 |
| 翻译校对 | 高 |

## 相关问题

- [翻译指南](/translation-guide)：中文 ↔ 英文翻译的约定与流程
- [贡献指南](https://github.com/twds0x13/FluxFormula/blob/main/CONTRIBUTING.md)：Bug 报告、PR 提交、开发环境搭建
- [GitHub Discussions](https://github.com/twds0x13/FluxFormula/discussions)：提问和讨论
