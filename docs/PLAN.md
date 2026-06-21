# VitePress 文档站大纲

> 当前状态：基本完成。页面结构与大纲一致，内容已同步至 v1.7.0。

## 页面结构

```
docs/
├── index.md                      # 首页（v1.7.0：benchmark 数据 CI 自动同步）
├── guide/
│   ├── installation.md           # 安装说明
│   ├── getting-started.md        # 入门：Lexer 路径为主
│   ├── core-concepts.md          # 核心概念（含 TokenContext、ChainLink、缓存）
│   ├── writing-a-definition.md   # 自定义运算符（含 ResolveToken）
│   └── advanced.md               # 高级用法（含 Connect、Set、ToBytes/FromBytes、FluxConfig）
├── api/
│   ├── overview.md               # API 总览
│   ├── flux-assembler.md         # 入口类
│   ├── flux-formula.md           # 公式容器
│   ├── flux-instance.md          # 流式执行器
│   ├── idefinition.md            # 定义接口
│   ├── instruction.md            # 字节码指令
│   └── flux-token.md             # 词法 Token
├── examples/
│   ├── float-math.md             # FloatMathDef 完整示例
│   ├── damage-formula.md         # 游戏伤害公式
│   ├── error-handling.md         # R0 短路错误处理
│   ├── vector3.md                # Vector3 运算
│   └── chain-connect.md          # 链式 Connect
├── technical/
│   ├── internals.md              # 内部原理概览
│   ├── compile-cache.md          # 编译缓存管线
│   ├── chainlink-deep-dive.md    # ChainLink 深度解析
│   ├── architecture-decisions.md # 架构决策记录
│   ├── technical-analysis.md     # 源码技术分析（持续更新）
│   └── pipeline/
│       ├── overview.md           # 管线全景
│       └── injector.md           # 数据注入器
├── migration-guide.md            # 迁移指南
├── translation-guide.md          # 翻译状态追踪
├── faq.md                        # 常见问题
├── FEATURE-streaming-injection.md # Lexer 功能规划（历史档案）
└── PLAN.md                       # 本文件
```
