# API 总览

## 类型关系图

```mermaid
graph TD
    Source["string 表达式"] -->|"Lex()"| Lexer["FluxLexer<br/>词法分析"]
    Lexer -->|"LexResult"| Assembler["FluxAssembler<br/>编译入口"]
    Token["FluxToken<br/>词法单元"] -->|"Compile()"| Assembler
    Assembler -->|"产出"| Formula["FluxFormula<br/>不可变字节码"]
    Assembler -->|"Instantiate()"| Instance["FluxInstance<br/>流式执行器"]
    Instance -->|"Set()/Run()"| Result["TData"]
    Formula -->|"Connect()"| Formula2["FluxFormula"]
    Formula -->|"ToBytes/FromBytes"| Bytes["byte[]"]
    Formula -->|"CreateAsset"| Asset["FluxAsset<br/>ScriptableObject"]

    subgraph 执行路径
        Instance -->|"JIT"| JITDelegate["CompiledFunc<br/>委托"]
        Instance -->|"解释器"| Evaluator["FluxEvaluator<br/>stackalloc 指针循环"]
    end

    subgraph 编译器内部
        Assembler --> Compiler["FluxCompiler<br/>调车场算法"]
        Compiler --> Instruction["Instruction[]<br/>8字节字节码"]
    end

    subgraph 用户定义
        Def["IFluxJITDefinition<br/>运算符语义"] -.-> Assembler
        Def -.-> Evaluator
        Def -.-> JITDelegate
    end
```

## Public 类型

| 类型 | 泛型 | 定位 |
|------|:--:|------|
| [FluxAssembler](./flux-assembler) | `<TData, TOper, TDef>` | 主入口：编译与实例化 |
| [FluxFormula](./flux-formula) | `<TData, TOper>` | 不可变字节码容器 |
| [FluxInstance](./flux-instance) | `<TData, TOper, TDef>` | ref struct 流式执行器 |
| [IFluxDefinition](./idefinition) | `<TData, TOper>` | 运算符定义接口（解释器路径） |
| [IFluxJITDefinition](./idefinition) | `<TData, TOper>` | 运算符定义接口（含 JIT 路径） |
| [Instruction](./instruction) | — | 8 字节指令结构体 |
| [FluxToken](./flux-token) | `<TData, TOper>` | 词法 Token |
| `LexerConfig<TData, TOper>` | `<TData, TOper>` | Lexer 配置（运算符/括号/变量规则） |
| `FluxLexer<TData, TOper>` | `<TData, TOper>` | 手写 Span 词法器 |
| `LexResult<TData, TOper>` | `<TData, TOper>` | Lexer 产出：Token 数组 + 变量名 |
| `OperatorRule<TOper>` | `<TOper>` | 运算符符号到枚举的映射 |
| `BracketRule<TOper>` | `<TOper>` | 括号符号对到枚举的映射 |
| `VariablePatternRule` | — | 变量前缀/后缀模式定义 |
| `OpPair<TOper>` | `<TOper>` | 括号配对描述 |
| `FluxAsset` | — | ScriptableObject 资产容器 |
| `FormulaLibrary<TData, TOper, TDef>` | `<TData, TOper, TDef>` | 资产创建与加载（需 FLUX_ADDRESSABLES） |
| `FluxFormulaRef<TData, TOper, TDef>` | `<TData, TOper, TDef>` | AssetReference 类型安全包装（需 FLUX_ADDRESSABLES） |
| `VariableSlot` | — | 变量名到槽位索引的映射 |

### 内部类型

以下类型非 Public API，仅列示用途：

- `FluxPlatform` — JIT 降级状态控制
- `FluxEvaluator<TData, TOper, TDef>` — 解释器执行引擎
- `FluxCompiler<TData, TOper, TDef>` — 调车场算法实现
- `FluxJITCompiler<TData, TOper, TDef>` — LINQ Expression Tree 编译
- `FluxInjector<TData>` — 数据注入器

## 命名空间

- **`FluxFormula.Core`** — 所有公共类型与内部运行时类型
- **`FluxFormula.Compiler`** — `FluxCompiler` 与 `FluxJITCompiler`（内部）
- **`FluxFormula.Editor`** — `FluxAssetEditor`、`FluxAssetInspector`、Dump 扩展（Editor-only）

## 泛型约束

```
TData  : unmanaged               (float, int, 自定义 blittable struct)
TOper  : unmanaged, Enum         (必须 enum X : byte)
TDef   : unmanaged, IFluxJITDefinition<TData, TOper>
```
