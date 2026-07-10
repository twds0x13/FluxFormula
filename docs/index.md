---
layout: home

hero:
  name: "FluxFormula"
  text: Unity 公式编译与执行框架
  tagline: 自定义运算符集，中缀表达式编译为字节码，解释器或 JIT 双后端执行，零堆分配
  image:
    src: /logo.png
    alt: FluxFormula
  actions:
    - theme: brand
      text: 快速入门
      link: /guide/getting-started
    - theme: alt
      text: API 参考
      link: /api/overview

features:
  - title: 执行期零 GC
    details: ref struct、stackalloc 与 unsafe 指针操作，执行期零堆分配。编译期仅一次 Instruction[] 分配加字面量解析，后续执行走纯栈。
  - title: 双后端执行
    details: 解释器全平台兼容，JIT 基于 LINQ Expression Tree 编译为委托。AOT 平台自动降级，无需手动切换。
  - title: 自定义指令集
    details: 实现 IFluxExprDefinition 接口定义领域运算符，一次编写同时获得解释器与 JIT 两条执行路径。
  - title: 紧凑字节码
    details: Instruction 为 8 字节定长结构体，LayoutKind.Explicit 显式内存布局。256 虚拟寄存器，最大 arity 6。
  - title: 三态求值器
    details: 热路径解释器全速执行，柯里化求值器渐进式绑定支持分叉，单步调试器逐指令排查。三种模式共享同一寄存器机核心，覆盖调试到生产的全场景。
---

## 性能一览

BenchmarkDotNet on Intel Core Ultra 9 275HX，.NET 9，ShortRun：

| 阶段 | 简单表达式 | 复杂表达式 | 分配 |
|------|-----------|-----------|------|
| Lexer | ~103 ns | ~422 ns | 392–1080 B |
| Compile | ~34 ns | ~119 ns | 112–496 B |
| 解释器求值 | ~27 ns | ~42 ns | **0 B** |
| JIT 求值 | ~2 ns | ~4 ns | **0 B** |

编译为一次性开销，执行期零堆分配。JIT 比解释器快 15 倍。