# FluxAssembler

编译入口，将词法 Token 编译为字节码并实例化为执行器。

## 签名

```csharp
public readonly unsafe struct FluxAssembler<TData, TDef>
    where TData : unmanaged
    where TDef : unmanaged, IFluxExprDefinition<TData>
```

两个泛型参数：数据类型 `TData` + 定义体 `TDef`。v3.0.0 移除了 `TOper` 泛型参数：操作符枚举变为定义体的内部实现细节，框架层面仅见 `byte`。

## 构造

```csharp
public FluxAssembler(TDef definition)
```

传入运算符定义实例（值类型，无堆分配）。

## 方法

### Compile（Lexer 路径）

```csharp
public FluxFormula<TData, TDef> Compile(LexResult<TData> lexResult)
```

接受 `FluxLexer.Lex()` 返回的 `LexResult`，直接编译为字节码。LexResult 携带 Token 数组和变量名信息，Compile 将变量名写入 `FluxFormula.VariableSlots`。

### Instantiate

```csharp
public FluxInstance<TData, TDef> Instantiate(
    FluxFormula<TData, TDef> formula,
    bool jit = false)
```

激活已有的 Formula 为可执行 Instance。

- `jit: false`（默认）：使用解释器路径，栈分配寄存器
- `jit: true`：先尝试 JIT（Expression Tree 编译为委托），若平台不支持（AOT）则自动降级

## 公式类型判定

`Compile()` 检查首 Token 判定 `Formula` 或 `Modifier`（内部 `FluxType` 枚举，v3.0.0 改为 `internal`；外部通过 `FluxFormula` / `FluxModifier` 类型区分）：

> **v5.5+**: 若首 Token 来自具有 `Slots` 声明的 `OperatorRule`，优先使用 `Slots[0] < 0` 判定是否需左操作数；否则回退到 `IFluxDefinition.GetFirstPosition`。

| 首 Token | 产出的外部类型 |
|----------|---------------|
| Immediate（如 Const） | `FluxFormula<TData, TDef>` |
| 函数式前缀运算符（Slots[0] ≥ 0） | `FluxFormula<TData, TDef>` |
| 左括号（PairRole=Left） | `FluxFormula<TData, TDef>` |
| 中缀运算符（Slots[0] < 0 或 GetFirstPosition=Left） | `FluxModifier<TData, TDef>` |

## 参见

- [FluxFormula](./flux-formula) — 编译产出的字节码容器
- [FluxInstance](./flux-instance) — 流式执行器
- [IDefinition](./idefinition) — 自定义运算符定义接口
