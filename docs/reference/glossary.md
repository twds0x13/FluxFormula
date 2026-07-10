# 术语速查表

FluxFormula 文档中出现的核心术语，按分类组织的快速索引。每术语附带 1–2 句事实陈述，无主观修饰。

## 管线阶段

| 术语 | 说明 |
|------|------|
| 词法分析 (Lexer) | `FluxLexer<TData>` 将字符串表达式解析为 Token 流。手写 `ReadOnlySpan<char>` 扫描器，零 Regex，零分配 |
| 编译器 (Compiler) | `FluxAssembler.Compile()` 将 Token 流转换为字节码，产出 `FluxFormula<TData,TDef>` 或 `FluxModifier<TData,TDef>` |
| 解释器 (Interpreter) | `FluxEvaluator` 逐条执行字节码指令，全平台兼容（含 IL2CPP/AOT），ref struct 栈分配 |
| JIT | `FluxJITCompiler` 将字节码编译为 LINQ Expression Tree 委托，单次调用 2–4ns。不支持 JIT 的平台（WebGL/iOS）自动降级为解释器 |
| IL 编译器 | `FluxILCompiler` 直接发射 IL 指令到 `DynamicMethod`，生成轻量委托 |

## 数据结构

| 术语 | 说明 |
|------|------|
| Token (`FluxToken<TData>`) | 中缀表达式的最小原子构件。包含 `Oper`（byte 操作码）和 `Data`（数据值）。Immediate Token 携带具体值，Operator Token 的 Data 为 default |
| Instruction | 8 字节定长结构体，`LayoutKind.Explicit` 内存布局。内含 OpCode、Arg0/Arg1、2 字节 Raw 字段。TData 可跨多个 Instruction 槽位编码 |
| 立即数 (Immediate) | `OpType.Immediate` 类型的指令，携带编译期已知的常量数据。在字节码中直接内联存储，无需运行时查找 |
| Formula (`FluxFormula<TData,TDef>`) | 不可变字节码容器，代表可独立求值的完整公式。由 `Compile()` 生成，可缓存复用 |
| Modifier (`FluxModifier<TData,TDef>`) | 不可变字节码容器，代表缺少左操作数的半成品公式。通过 `Connect()` 串联到前一公式后方 |
| FluxChain | `Connect()` 的返回类型。内部持有 `ChainLink[]`，延迟至求值时刻拼接。链长超过 `MergeThreshold` 时自动合并为原子公式 |
| FluxInstance | 公式的运行时执行句柄。`Instantiate()` 返回 ref struct，通过 `Set()` 注入变量、`Run()` 获取结果 |

## 执行模型

| 术语 | 说明 |
|------|------|
| 虚拟机 (VM) | 基于寄存器的字节码执行引擎。不是栈式，操作数地址由指令中的寄存器索引直接指定 |
| 寄存器机 | 256 个虚拟寄存器的线性数组 (`stackalloc TData[256]`)。二元运算结果可复用左操作数寄存器，减少寄存器消耗 |
| 字节码 (Bytecode) | 编译器输出的 Instruction 序列。每条指令 8 字节定长，缓存友好。可序列化为 `.ff` 文件或嵌入 Blob |
| 短路返回 (Early Exit) | 当 `Compute()` 将非 default 值写入 R0（错误寄存器），求值器立即终止并返回该值，不再执行后续指令 |
| 总线寄存器 (R1) | 固定寄存器索引 1。所有公式的计算结果落在 R1，`Connect()` 通过 R1 将前一公式的输出传入下一公式 |
| 元数 (Arity) | 运算符的参数数量。二元运算符 arity=2（如 `+`、`*`），一元 arity=1（如 `-` 取负）。最大 arity=6 |
| stackalloc | C# 关键字，在栈上分配连续内存块。FluxFormula 用 `stackalloc TData[256]` 创建寄存器数组，零 GC |
| ref struct | C# 值类型约束，实例只能在栈上存在，不能被装箱或存到堆上。`FluxInstance` 是 ref struct，保证执行期零堆分配 |

## 运算符定义

| 术语 | 说明 |
|------|------|
| OpCode (操作码) | 运算符的 `byte` 标识。框架不依赖枚举，Definition 的各个方法接收 `byte` 并自行按需转换为私有枚举 |
| Precedence (优先级) | 运算符的结合优先级。`*` 优先级高于 `+`，因此在 `a + b * c` 中 `b * c` 先结合。实现为 `GetPrecedence(byte)` 返回 int |
| Associativity (结合性) | 同级运算符的求值方向。`Left` 表示从左到右（`a - b - c` = `(a - b) - c`），`Right` 表示从右到左 |
| OperandPosition | 运算符期望操作数的位置。`Left`（如 `+` 从左操作数取值）、`Right`（如 `Neg` 从右操作数取值）。`GetFirstPosition()` 声明首个操作数位置 |
| TokenContext | 编译阶段用于符号消歧的上下文标记。`OperandExpected`（期望操作数）vs `OperatorExpected`（期望运算符）。同一符号（如 `-`）在不同上下文中映射为不同操作码 |
| OpType | 指令的类型分类：`Instruction`（普通运算符）、`Immediate`（立即数）、`Return`（终止指令）、`Pair`（括号） |

## 缓存与持久化

| 术语 | 说明 |
|------|------|
| FormulaCache | 全局公式缓存，基于 `DualHash64` 做键。JIT 委托和字节码条目均缓存在此。已设置为弱引用，GC 压力下可回收 |
| DualHash64 | 128 位双哈希（XxHash64 + FnvHash64），对公式内容做内容寻址。同一公式出现在不同位置共享同一缓存条目 |
| Delegate 缓存 | 每个 `FluxFormula` 编译后的 JIT 委托缓存在 FormulaCache 中。首次 `Instantiate` 编译，后续直接命中 |
| Blob | 所有预编译公式的二进制集合（`.blob` 文件）。构建时由 `FluxBlobBuilder` 生成，运行时通过 `FluxBlob.Load()` 加载到 FormulaCache |
| VFF (Virtual Flux Formula) | `.vff` 文件持久化公式引用（DualHash64 指向 Blob 中的公式）+ 参数覆写。Blob 类比 DLL，VFF 类比 import table |
| FluxAsset | Unity 中的 `ScriptableObject` 公式资产文件。Editor 中可视化编辑公式文本，构建时纳入 Blob |

## 平台概念

| 术语 | 说明 |
|------|------|
| IL2CPP | Unity 的 AOT 编译后端，将 C# 转为 C++ 后编译为原生代码。不支持 `Expression.Compile()`，FluxFormula 自动降级为解释器 |
| AOT (Ahead-of-Time) | 编译方式的一种，在构建时生成机器码，运行时无法生成新代码。iOS 和 WebGL 属 AOT 平台 |
| Burst | Unity 的高性能编译器，可将 C# 子集编译为高度优化的原生代码。FluxFormula 的定义体满足 `unmanaged` 约束，Burst 兼容 |
| Addressables | Unity 的异步资源管理系统。`FluxFormula.Addressables` 包通过 `ValueTask<T>` 加载 Blob 和 VFF 文件 |
| Expression Tree (表达式树) | .NET 的代码即数据 API。JIT 路径将字节码转为 Expression Tree，再编译为委托。AOT 平台不可用 |
