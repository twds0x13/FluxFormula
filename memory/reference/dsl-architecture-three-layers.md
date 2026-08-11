---
name: dsl-architecture-three-layers
description: FluxFormula 作为嵌入式 DSL 框架的三层架构——高维向量、抽象运算符、极限优化。竞品最多穿透一层。
metadata: 
  node_type: memory
  type: reference
  date: 2026-06-26
  related: []
  originSessionId: dbd39381-c506-493a-939e-6cfcbdbe749a
---

## 三层架构

### 第一层：高维向量

传统方案把一个 Damage 拆成若干个独立的 float——`amount`/`element`/`critMult`——然后在公式里手动拼回去。公式看不到这些 float 之间的关联。

FluxFormula 的做法是让整个 struct 作为不可分割的原子进入字节码。在寄存器机里，一个 `stackalloc TData[256]` 槽位装载的是完整的 struct 状态。**维度的增加不增加寄存器槽位，不增加指令条数，不增加 VM 循环开销。**

### 第二层：根据高维向量设计抽象运算符

这步是区分"通用 VM"和"领域 DSL 框架"的关键。

通用 VM 在 float 上定义了 `+ - * /`，换到 Damage 上还是 `+ - * /`，语义靠调用方自己维护。

FluxFormula 的 `Compute()` switch case 由用户定义：你可以在 Sub 里注入克制，在 Mul 里传播元素，在 Add 里叠加 buff。操作符的语义空间和类型的维度完全对齐——Damage 有 Element 字段，Sub 就用它；Damage 没有暴击字段，Mul 就不管它。

字节码和定义的这种可分离性是架构的自然结果——但**不可滥用**。`FluxFormula<TData, TDef>` 的 TDef 在类型签名中，`FromBytes<TData, TDef>` 必须在编译期声明定义体类型。拿 MathDef 编译的 .ff 文件注入到 GameDef 上下文中是**编译错误**——类型系统在加载阶段就拦截了跨定义注入。v3.0.0 之前的 `FluxType` + `TOper` 枚举留下了这个注入面（`(MathOper)opCode` 强制转换静默接受任意 byte 值），v3.0.0 已关闭。

### 第三层：架构允许的极限优化

"高维向量 + 抽象运算符"在学术界不稀奇，但落到游戏引擎里通常是巨大的运行时开销：虚函数表、堆分配、GC 压力。

FluxFormula 的约束保证了这条路不走偏：

| 约束 | 效果 |
|------|------|
| `TData : unmanaged` | 栈分配，零 GC，Burst 兼容 |
| Instruction 8 字节定长 | 缓存友好，SIMD 友好 |
| 寄存器机 256 槽 | 编译期确定栈大小，无运行时扩容 |
| JIT + 解释器双后端 | 可选 2ns 级 delegate 调用 |

**无论 TData 多复杂，字节码的密度、VM 的执行模型、缓存的行为模式都是恒定的。** 复杂度被压缩进了 `Compute()` 的 switch case 里，而这个 switch 在 Burst 里被编译为跳转表，在 JIT 里被内联——最终落到指令集上的，和手写 C# 没有本质区别。

## 一句话

> 把领域复杂度封装为高维向量，用抽象运算符定义其交互规则，再将整条 DSL 塞进一个不需要为复杂度付出额外运行时代价的执行模型里。

## 竞品的层穿透

| 项目 | 第一层（高维向量） | 第二层（抽象运算符） | 第三层（极限优化） |
|------|:---:|:---:|:---:|
| Jackson Dunstan | ❌ 仅 double | ❌ 硬编码 | ✅ Burst |
| BLAST | ❌ `float[16]` 栈 | ❌ C-like 关键字 | ✅ Burst |
| expr-solver | ❌ 仅 f64/Decimal | ❌ 内置函数 | ❌ |
| **FluxFormula** | **✅** | **✅** | **✅** |

三条线全穿的只有 FluxFormula。
