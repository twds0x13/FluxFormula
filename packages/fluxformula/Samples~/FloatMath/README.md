# FloatMath

FluxFormula 入门示例：演示浮点四则运算公式的完整生命周期——Lexer 词法分析 → Compile 编译 → Instantiate 实例化 → Set 变量绑定 → Run 求值。

## 包含内容

- **FloatMathDefinition.cs** — 操作符定义（`IFluxExprDefinition<float>`），含 Const / Add / Sub / Mul / Div / Neg
- **FloatMathSample.cs** — MonoBehaviour 示例，右键 ContextMenu 执行

## 使用方式

1. 安装 `com.twds0x13.fluxformula` 后，在 Unity Package Manager 中导入此 Sample
2. 将 `FloatMathSample` 挂到任意 GameObject
3. 右键组件标题 → **Evaluate (Interpreter)** 或 **Evaluate (JIT)**
4. 修改 Inspector 中的 expression / atk / critDmg / defense 参数后重新执行

## 关键要点

- 解释器全平台兼容（含 IL2CPP/AOT），~27ns 一次求值
- JIT 更快（~2ns）但不支持 IL2CPP，不可用时自动降级
- 变量通过 `[varName]` 语法绑定，运行时 `Set("varName", value)` 赋值
- `FluxInstance` 是 ref struct，使用 `using` 确保及时释放
