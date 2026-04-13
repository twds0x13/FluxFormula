# 文档翻译指南

FluxFormula 文档站同时维护简体中文（`zh-CN`，默认）和英文（`en`）两个 locale。欢迎帮助改进翻译质量。

## 翻译范围

`docs/` 目录结构：

```
docs/
├── index.md                 ← 中文源文件
├── faq.md
├── guide/
├── api/
├── examples/
├── technical/
└── en/                      ← 英文翻译（与中文源文件一一对应）
    ├── index.md
    ├── faq.md
    ├── guide/
    ├── api/
    ├── examples/
    └── technical/
```

中文源文件为原文，`docs/en/` 下的同名文件为英文翻译。翻译工作是对照中文源文件逐段翻译为英文，更新到 `docs/en/` 对应位置。

## 约定

### 翻译原则

- 优先**信息准确**，其次表达流畅
- 技术术语保持统一：`立即数 → immediate`，`字节码 → bytecode`，`短路返回 → early exit`，`寄存器 → register`
- 代码块不翻译——代码块内的注释可以翻译，但变量名、类型名保持原样
- 链接路径需要加 `/en/` 前缀：中文源文件写 `/guide/getting-started`，英文翻译写 `/en/guide/getting-started`

### 风格

- 工程文档风格：陈述事实，不加感叹号、不加主观修饰语
- 不需要逐字直译——自然英文优先，但不要偏离中文原文的技术含义
- 参考已有翻译页面的语气和术语选择

## 流程

1. 在 [Issues](https://github.com/twds0x13/FluxFormula/issues) 中说明翻译意图（哪个文件或哪个章节），避免与其他人重复工作
2. Fork 仓库，在 `docs/en/` 下修改对应文件
3. 本地预览：

```bash
cd docs
npx vitepress dev
```

4. 提交 PR，描述修改了哪些页面
5. 中英文页面数量需要保持一致——新增中文页面时，同时提供英文翻译（至少包含框架性翻译，可以后续迭代）

## 已翻译页面

| 中文 | 英文 | 状态 |
|------|------|:---:|
| `index.md` | `en/index.md` | ✅ |
| `faq.md` | `en/faq.md` | ✅ |
| `guide/installation.md` | `en/guide/installation.md` | ✅ |
| `guide/getting-started.md` | `en/guide/getting-started.md` | ✅ |
| `guide/core-concepts.md` | `en/guide/core-concepts.md` | ✅ |
| `guide/writing-a-definition.md` | `en/guide/writing-a-definition.md` | ✅ |
| `guide/advanced.md` | `en/guide/advanced.md` | ✅ |
| `api/overview.md` | `en/api/overview.md` | ✅ |
| `api/flux-assembler.md` | `en/api/flux-assembler.md` | ✅ |
| `api/flux-formula.md` | `en/api/flux-formula.md` | ✅ |
| `api/flux-instance.md` | `en/api/flux-instance.md` | ✅ |
| `api/idefinition.md` | `en/api/idefinition.md` | ✅ |
| `api/instruction.md` | `en/api/instruction.md` | ✅ |
| `api/flux-token.md` | `en/api/flux-token.md` | ✅ |
| `examples/float-math.md` | `en/examples/float-math.md` | ✅ |
| `examples/damage-formula.md` | `en/examples/damage-formula.md` | ✅ |
| `examples/error-handling.md` | `en/examples/error-handling.md` | ✅ |
| `examples/vector3.md` | `en/examples/vector3.md` | ✅ |
| `technical/internals.md` | `en/technical/internals.md` | ✅ |

## 技术支持

翻译过程中遇到技术术语不确定的情况，可以：
- 参考已有英文翻译页面的术语使用
- 在 GitHub Discussions 中提问
