# FluxAssembler

编译入口，将词法 Token 编译为字节码并实例化为执行器。

## 签名

```csharp
public readonly unsafe ref struct FluxAssembler<TData, TDef>
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

### Compile（Token 路径）

```csharp
public FluxFormula<TData, TDef> Compile(
    ReadOnlySpan<FluxToken<TData>> tokens,
    string[] varNames = null)
```

将中缀 Token 序列编译为字节码 `Formula`。内部执行调车场算法，分配 `Instruction[]` 缓冲并填充。Formula 可缓存复用。

### Instantiate

```csharp
public FluxInstance<TData, TDef> Instantiate(
    FluxFormula<TData, TDef> formula,
    bool jit = false)
```

激活已有的 Formula 为可执行 Instance。

- `jit: false`（默认）：使用解释器路径，栈分配寄存器
- `jit: true`：先尝试 JIT（Expression Tree 编译为委托），若平台不支持（AOT）则自动降级

### Build

```csharp
public FluxInstance<TData, TDef> Build(
    ReadOnlySpan<FluxToken<TData>> tokens,
    bool jit = false)
```

`Compile()` + `Instantiate()` 合并调用。适用于一次性公式，无需缓存。

```csharp
var runner = new FluxAssembler<float, MathDef>(def);
float r = runner.Build(tokens, jit: true).Run();
```

## 公式类型判定

`Compile()` 检查首 Token 判定 `Formula` 或 `Modifier`（内部 `FluxType` 枚举，v3.0.0 改为 `internal`；外部通过 `FluxFormula` / `FluxModifier` 类型区分）：

| 首 Token | 产出的外部类型 |
|----------|---------------|
| Immediate（如 Const） | `FluxFormula<TData, TDef>` |
| 一元前缀运算符（arity=1） | `FluxFormula<TData, TDef>` |
| 左括号（PairRole=Left） | `FluxFormula<TData, TDef>` |
| 二元运算符（arity≥2 且非括号） | `FluxModifier<TData, TDef>` |

## v3.0.0 变更

- `FluxAssembler<TData, TOper, TDef>` → `FluxAssembler<TData, TDef>`（三参数→两参数）
- `LexResult<TData, TOper>` → `LexResult<TData>`
- `FluxToken<TData, TOper>` → `FluxToken<TData>`（`Oper` 字段变为 `byte`）
- 编译期跨定义类型安全检查：`FluxFormula<float, MathDef>` 和 `FluxFormula<float, GameDef>` 为不同编译器类型，误连编译不过

## 参见

- [FluxFormula](./flux-formula) — 编译产出的字节码容器
- [FluxInstance](./flux-instance) — 流式执行器
- [IDefinition](./idefinition) — 自定义运算符定义接口
