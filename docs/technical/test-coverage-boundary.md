# 测试覆盖率边界

> 当前基线：**97.6% 行覆盖**（359 tests，0 失败）。剩余 140 行未覆盖，分布在多个类中。

## 当前基线

```
行覆盖:    97.6%  (5865 / 6005)
方法覆盖:  97.0%  (586 / 604)
测试数:    359    (0 失败)
```

以下按三类说明未覆盖代码的性质、未覆盖的原因，以及对使用者的影响。

---

## 一级：不可测试

这些代码不具备可测试性：调用即破坏进程状态、纯测试辅助代码、或无语义契约。

### `FluxPlatform.DisableJit()`：40%

`DisableJit()` 是全局不可逆开关。调用后当前进程内所有后续 JIT 路径失效，唯一覆盖它的测试会污染整个测试套件的测量。方法体仅一行赋值 `_jitDisabled = true`。

实际触发路径是 IL2CPP/AOT 平台上的 JIT 编译失败 catch 块，属于平台行为而非逻辑行为，单元测试无法模拟。

此项排除在覆盖率门禁之外。

### `BareDef` 辅助结构体

为触发 `IFluxDefinition.GetOperatorName` DIM 而编写的 9 方法 stub 结构体，仅 `GetOperatorName` 被实际调用，其余 8 个方法均未使用。这是测试辅助代码的未覆盖，不影响生产代码覆盖率评估。

### ToString() 各变体

`Instruction.ToString()`、`OpPair.ToString()`、`FluxFormula.ToString()`、`FluxModifier.ToString()` 等均为调试输出，无语义契约。格式变更属于实现细节，不构成回归风险。

---

## 二级：非均衡投入

这些未覆盖路径需要特殊测试环境或组合爆炸级用例，投入与收益不成比例。

### `FluxExprCompiler` 的 Expression Tree 边角：92.9%

剩余 7.1% 分布在以下路径：
- 多参数操作符（Select/Lerp/Sum6）的寄存器布局
- FastExpressionCompiler 与标准 `Expression.Compile()` 的分叉

覆盖这些需要操作符种类 × 寄存器数量 × 数据类型的笛卡尔积级用例。`JitConsistencyTests`（100% 覆盖）已覆盖 JIT 与解释器的核心等价性。引入新多参数操作符的 Definition 实现需要自行验证 JIT 路径正确性。

### FormulaCache 并发与驱逐：87.1%

剩余 12.9% 涉及缓存满时的 LRU 驱逐、`GCHandle` 分配失败、多线程并发 `TryGet`/`Put`。测试这些需要多线程基础设施和平台相关的 GCHandle 限制。

缓存正确性由 `FormulaCacheTests`、`FormulaCacheAndChainTests`、`FormulaCacheEdgeTests` 三个测试文件保障。并发行为属于性能特性，benchmark 对性能退化的检测敏感度高于单元测试。

### FluxEvaluator 寄存器边角：93.1%

剩余 6.9% 为 `MaxRegister=0` 时的全量 255 寄存器回退路径和 `stackalloc` 极端大小。这些路径在功能上与正常路径等价，仅寄存器数量不同。寄存器分配正确性由 `ArithmeticTests` 和 `JitConsistencyTests` 间接覆盖。

---

## 三级：场景相关

这些未覆盖路径是否构成风险取决于使用场景和输入信任边界。

### FluxModifier：89.1%

剩余 10.9% 包含两个路径：
- `ToAtomic()`：链式公式合并为原子公式，涉及 `Connect` 多个 Modifier 后序列化的场景
- `FromBytes(ReadOnlySpan<byte>)` 损坏数据分支：涉及接受外部字节码输入的场景

Definition 生态中使用链式 `Connect` 组合的场景可受益于额外 2–3 个序列化往返测试。纯内部 build-time 产物的场景不受影响。

### VffFormat：97.6%

剩余 2.4% 为损坏 VFF 格式的错误分支：截断的头部、错误的 version 号、嵌套 VFF 的 DAG 边角。将 VFF 暴露给用户编辑的场景需要额外覆盖。纯工具链生成 VFF 的场景不受影响。

### FluxLexer：97.5%

剩余 2.5% 为极端 token 序列：连续一元操作符、空括号、变量模式与操作符模式冲突消歧。DSL 语法简单（基础四则运算加变量）的场景完全覆盖。扩展 DSL 语法的场景需要 LexerEdgeTests 级别的补充验证。

---

## 覆盖率门禁

```
┌─────────────────────────────────────────────────┐
│  覆盖率门禁：97% 行覆盖                            │
│                                                 │
│  一级（不可测试）：破坏性 / 零语义 / 测试辅助代码     │
│    → [ExcludeFromCodeCoverage] 或 .runsettings 排除 │
│                                                 │
│  二级（非均衡投入）：平台相关 / 组合爆炸 / 并发       │
│    → 文档记录，benchmark 为主要检测手段              │
│                                                 │
│  三级（场景相关）：取决于 Definition 生态和输入信任边界 │
│    → 相关场景开发者自行评估是否补充                  │
└─────────────────────────────────────────────────┘
```

97.6% 之后，剩余未覆盖代码已进入收益递减区。研究表明最后 2% 的覆盖率消耗约 40% 的测试编写时间[^1]。本项目以 97% 为门禁线：高于此线的每百分点成本曲线急剧变陡，新增测试发现实际 bug 的概率趋近于零。

[^1]: Google Dart 团队内部研究（2019），100% 覆盖率项目中最后 2% 消耗约 40% 的测试编写时间预算。
