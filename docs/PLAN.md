# VitePress 文档站大纲

> 当前状态：基本完成。页面结构与大纲一致，内容已同步至 v1.3.2。

## 页面结构

```
docs/
├── index.md                      # 首页（v1.3.2：去 Emoji，工程化 tone）
├── guide/
│   ├── installation.md           # 安装说明
│   ├── getting-started.md        # 入门：Lexer 路径为主
│   ├── core-concepts.md          # 核心概念（含 TokenContext）
│   ├── writing-a-definition.md   # 自定义运算符（含 ResolveToken）
│   └── advanced.md               # 高级用法（含 Set/SetIndex/ToBytes）
├── api/
│   ├── overview.md               # API 总览（v1.3.2：含 Lexer/Asset 新类型）
│   ├── flux-assembler.md         # 入口类（含 Compile(LexResult) 重载）
│   ├── flux-formula.md           # 公式容器（含 ToBytes/FromBytes/VariableSlots）
│   ├── flux-instance.md          # 流式执行器（Set + SetIndex + GetBuffer）
│   ├── idefinition.md            # 定义接口（含 ResolveToken）
│   ├── instruction.md            # 字节码指令
│   └── flux-token.md             # 词法 Token
├── examples/
│   └── float-math.md             # FloatMathDef 完整示例
├── technical/
│   ├── internals.md              # 内部原理（从 technical-analysis.md 精简）
│   └── technical-analysis.md     # v1.0.0 历史分析（已添加过时警告）
├── FAQ.md                        # 常见问题
├── FEATURE-streaming-injection.md # Lexer 功能规划（A 已完成）
└── PLAN.md                       # 本文件
```
