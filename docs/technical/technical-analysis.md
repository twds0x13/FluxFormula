# FluxFormula 技术分析

> 生成日期: 2026-06-19 | 最后更新: 2026-06-25 | 文档基于版本: v3.0.0（已全面更新类型签名）
>
> 本文档对 FluxFormula 的每个源文件进行逐行级技术要点统计，标注潜在问题、隐式约定和可优化点。不修改源码，仅做分析与记录。

### 三层泛型约束 (所有核心类型通用)

| 参数 | 约束 | 含义 |
|------|------|------|
| `TData` | `unmanaged` | 数据单元类型 (float, int, 自定义 blittable struct) |
| `TDef` | `unmanaged, IFluxJITDefinition<TData>` | 操作符语义定义, 值类型以消除虚调用开销 |

v3.0.0 移除了 `TOper` 泛型参数：操作符枚举变为定义体 `private` 细节，所有操作符相关方法使用 `byte`。`IFluxJITDefinition<TData>` 替代了此前的 `IFluxJITDefinition<TData, TOper>`。

---

## 2. 逐文件技术要点

### 2.1 FluxToken.cs

**定位**：词法层，用户构建的最基本单元。

**技术要点**:
- v3.0.0：`FluxToken.Oper` 现在直接是 `byte`，不再需要 `*(byte*)&oper` 转换。
- Token 的 `Data` 字段仅对 Immediate 类型有效, 对操作符类 Token 其值为 `default`，这是一个隐式约定，代码中无运行时校验。

**潜在问题**:
- Token.Data 对非 Immediate Token 无意义, 但无任何防御性检查或文档提示。用户可能误填。

---

### 2.2 FluxEvaluator.cs (含所有枚举/结构/接口)

**定位**：运行时核心文件，承载了所有公共类型定义和解释器执行引擎。

#### 2.2.1 枚举

| 枚举 | 底层类型 | 值 | 说明 |
|------|----------|-----|------|
| `FluxType` | `byte` | Modifier=0, Formula=1 | 区分完整公式和修饰片段 |
| `OpType` | `byte` | Immediate=0, Instruction=1, Return=2 | 字节码指令分类 |
| `Associativity` | — | Left, Right | 运算符结合方向 |
| `Pair` | `byte` | None=0, Left=1, Right=2 | 括号配对角色 |

**技术要点**:
- `OpType` 的 `Immediate` 意为"立即数加载"：将一个 TData 值从指令流中加载到寄存器。这与传统 CPU 的"立即数"概念一致。
- `FluxType` 在 v3.0.0 改为 `internal`。Modifier 语义现由 `FluxModifier<TData, TDef>` 类型承担：该类型没有 `Instantiate()` 方法，编译期阻止独立求值。
- `TokenContext` 枚举在编译器/词法器中使用, 区分符号的上下文语义。

#### 2.2.2 FluxPlatform (v1.3.0 新增)

```csharp
internal static class FluxPlatform
{
    internal const int MaxRegisters = 255;
    private static volatile bool _jitDisabled;

    public static bool IsJitDisabled => _jitDisabled;
    public static void DisableJit() => _jitDisabled = true;
}
```

**技术要点**:
- `volatile bool` 保证多线程可见性，防止在异步/Job System 场景下 JIT 降级状态不一致。
- `DisableJit()` 仅在 `FluxAssembler.Instantiate()` 中 `Expression.Compile()` 失败时由框架调用, 用户不直接操作。
- `MaxRegisters = 255` 为 byte 索引上限, R0(错误) 和 R1(总线) 保留, 剩余 253 个通用寄存器。

#### 2.2.3 OpPair（v3.0.0: 非泛型）

```csharp
struct OpPair {
    Pair PairRole;        // 括号角色
    byte TargetLeft;      // 匹配的目标左括号 opcode
    bool EmitOnMatch;     // 匹配时是否发射指令
    byte EmitOpCode;      // 发射什么指令
}
```

**技术要点**:
- 微型 DSL 引擎: 将语法括号映射为语义指令。
- 典型用例: 函数调用 `sin(x)`：`sin` 是 Left-pair，右括号匹配时 `EmitOnMatch=true` 触发发射 `EmitOpCode=SinOp`。
- v3.0.0: `OpPair` 非泛型化：`TargetLeft` 和 `EmitOpCode` 均为 `byte`，消除了 Enum 装箱。

#### 2.2.4 Instruction (8 字节显式布局)

```
Offset:  0        1        2     3     4     5     6     7
Field:   OpCode   Dest     Arg0  Arg1  Arg2  Arg3  Arg4  Arg5
         └────────────── Raw (long) ──────────────────────┘
```

**技术要点**:
- `Size = 8` 保证, `Marshal.SizeOf<Instruction>()` 可用。
- 最大操作数数量 = 6 (Arg0-Arg5), 这限制了单个操作的 arity 上限。
- `Dest` 和 `Arg0` 可以是同一寄存器 (如 R1 = R1 + R2, Dest=1, Arg0=1, Arg1=2)。
- `Raw` 与 `OpCode` 共用 offset 0，读 `Raw` 得到整个 8 字节的 long 视图。`Raw` 仅用于调试 dump 和 `ToBytes()` 序列化。

**设计约束**:
- arity 上限为 6, 与 8 字节 struct 尺寸取平衡: 6 个操作数 + OpCode + Dest = 8 字节。
- Dest 可被设为 0 (R0 错误寄存器)，这是有意设计。若用户定义的操作符写入 R0，会触发短路返回，用于自定义错误处理。

#### 2.2.5 IFluxDefinition\<TData\>（v3.0.0）

```csharp
interface IFluxDefinition<TData> {
    byte GetReturnOp();                           // 哪个 opcode 是 Return
    int GetArity(byte op);                        // 操作数数量
    OpType GetKind(byte op);                      // Immediate/Instruction/Return
    int GetPrecedence(byte op);                   // 优先级 (越大越优先)
    OpPair GetPair(byte op);                      // 括号配对信息
    Associativity GetAssociativity(byte op);      // Left/Right 结合
    byte ResolveToken(byte oper, TokenContext ctx); // Token 消歧
    TData Compute(byte op, Instruction inst, ReadOnlySpan<TData> registers); // 执行
    string GetOperatorName(byte op);              // 操作码显示名称 (DIM)
}
```

**技术要点**:
- v3.0.0: 移除 `TOper` 泛型参数。所有方法使用 `byte`。操作符枚举变为定义体 `private` 细节。
- `Compute` 接收整个 `Instruction` 而不仅是 opcode, 允许访问 Dest 和 Arg0-Arg5 字段。
- `registers` 是长度为 256 的 `Span<TData>`，解释器保证至少这么大。
- `ResolveToken` 在 v1.2.0 加入, 负责根据上下文将同一符号映射为不同操作符（如 `-` → 一元取负 vs 二元减法）。
- 寄存器约定:
  - R0: 错误寄存器。任何非 default 返回值触发短路。
  - R1: 总线/默认结果寄存器。
  - R2-R254: 通用寄存器。

#### 2.2.6 IFluxJITDefinition\<TData\>（v3.0.0）

```csharp
interface IFluxJITDefinition<TData> : IFluxDefinition<TData> {
    Expression GetExpression(byte op, Instruction inst, ParameterExpression[] registers);
}
```

**技术要点**:
- `registers` 是 `ParameterExpression[256]`, 索引对应寄存器号。
- 返回值是纯计算 `Expression`，调用方负责包装赋值和 R0 错误检查。
- 与 `Compute()` 必须语义一致: 同一输入产生同一输出。

#### 2.2.7 FluxEvaluator (解释器核心)

```csharp
internal unsafe ref struct FluxEvaluator<TData, TDef> {
    TData Compute(ReadOnlySpan<Instruction> raw);
    static bool IsDefault(TData* ptr);
}
```

**技术要点**:

**(a) 寄存器内存对齐**
```csharp
byte* rawPtr = stackalloc byte[sizeof(TData) * byte.MaxValue + 63];
long addr = (long)rawPtr;
TData* regsPtr = (TData*)((addr + 63) & ~63);  // 64 字节对齐
```
- 分配 256 个 TData 寄存器 + 63 字节填充。
- 对齐到 64 字节边界, 暗示 SIMD 优化意图 (AVX cache line), 但当前未使用 SIMD 指令。
- 在栈上分配 (`stackalloc`), 零 GC。

**(b) 立即数读取**
```csharp
TData* pData = (TData*)(pBase + ip + 1);  // +1 跳过 Instruction 头
regsPtr[inst->Dest] = *pData;
ip += (sizeof(TData) + 7) / 8;             // 跳过的 Instruction 数
```
- 数据紧跟在 Instruction 头后面, 在同一个 `Instruction[]` 缓冲中。
- `(sizeof(TData) + 7) / 8` 计算数据占几个 Instruction 槽位。TData ≤ 8 字节 = 1 槽, ≤ 16 字节 = 2 槽。
- `ip` 循环变量递增的是"指令数"而非"字节数"，存在混合单位，理解成本较高。

**(c) 执行与短路**
```csharp
regsPtr[inst->Dest] = _definition.Compute(operByte, *inst, registers);
if (!IsDefault(&regsPtr[0]))           // R0 非默认 → 短路返回
    return regsPtr[0];
```
- 每条指令后检查 R0, 一旦非 default 立即返回错误值。
- `IsDefault()` 使用 `ReadOnlySpan<byte>.SequenceEqual()` 逐字节比较，对大于 8 字节的结构体可能较慢，但保证了通用性。

**(d) 指令指针无界检查**
- 循环 `for (int ip = 0; ip < raw.Length; ip++)` 中 Immediate 路径 `ip += dataSlots` 可能跳过多个指令。无越界检查，完全依赖编译器正确计算了缓冲长度。

**潜在问题**:
- `ip` 混合单位 (指令数 vs 字节偏移量)，理解成本高，但属内部实现，对用户透明。
- 无 `raw.Length` 越界保护，信任编译器输出。若编译器出现 bug 导致缓冲过短，会触发内存访问异常而非友好报错。
- `IsDefault` 对大 struct 的逐字节比较通用但非最优, 若 TData 为大型 blittable struct 可考虑特化路径。

---

### 2.3 FluxLexer.cs (v1.1.0 新增)

**定位**: 字符串到 Token 流的词法分析器。

**技术要点**:

**(a) 架构**: 配置驱动的手写 `ReadOnlySpan<char>` 扫描器。零正则, 零第三方依赖, 零分配（除产出数组外）。

**(b) 配置模型**:
```csharp
public class LexerConfig<TData>
{
    public byte LiteralOper;                              // 数字字面量对应操作码
    public Func<string, TData> LiteralParser;             // 字面量→TData 解析函数
    public List<OperatorRule> Operators;                  // 运算符符号列表
    public List<BracketRule> Brackets;                    // 括号对列表
    public List<byte> ImplicitOperators;                  // 隐式乘法操作码
    public List<VariablePatternRule> VariablePatterns;    // 变量模式
}
```

**(c) 运算符匹配策略**: 构造函数中对运算符按符号长度降序排序, 长符号优先匹配。例如 `**` 在 `*` 之前尝试, 避免截断误判。

**(d) 隐式运算符插入**: Lexer 在两轮扫描中检测并置（juxtaposition），如 `2(atk)` 或 `(a)(b)`，在中间插入隐式运算符。若配置了多个隐式运算符且检测到并置，抛出 `FormatException` 报告歧义。

**(e) 变量模式**: 通过 `VariablePatternRule` 的 Prefix/Suffix 定义变量语法。例如 `["[", "]"]` 匹配 `[atk]`, `["{var:", "}"]` 匹配 `{var:damage}`。变量名与 Token 位置并行记录在 `LexResult.VarNames` 中。

**潜在问题**:
- 若运算符存在前缀关系（如 `-` 和 `--`）, 排序逻辑保证长符号优先, 但用户需注意不要定义歧义规则。
- 隐式运算符插入在第二轮扫描中做 O(n) 遍历, n 为 Token 数量, 对极长公式有线性开销。
- 字面量解析委托 `LiteralParser` 可能抛出异常（如 `float.Parse` 失败）, Lexer 不做 catch, 异常直接传播给调用方。

---

### 2.4 FluxCompiler.cs

**定位**: 调车场算法编译器, 将中缀 Token 流转为后缀字节码。

**技术要点**:

**(a) 寄存器分配**
```csharp
byte nextReg = 2;  // R0=Error, R1=Bus, 从 2 开始分配
```
- 简单递增分配, 无寄存器重用。最长公式受限于 253 个变量 (255 - R0 - R1)。
- 寄存器用尽时抛出 `"Out of registers."`。该错误信息不含上下文（当前公式长度/寄存器用量）。

**(b) EmitOp — 隐式 R1 注入**
```csharp
while (regTop + 1 < arity)
{
    for (int i = regTop; i >= 0; i--)
        regStack[i + 1] = regStack[i];
    regStack[0] = 1;  // R1
    regTop++;
}
```
- 当操作数 arity > 可用寄存器栈深度时, 自动注入 R1 填补缺口。
- O(n²) 位移操作: 每次缺一个操作数就整体移动一次栈。实际 n ≤ 64, arity ≤ 6, 影响可忽略。

**(c) 括号匹配与 EmitOnMatch**
- 支持"函数调用"语义: `sin(x)` 中匹配时发射 `SinOp` 指令。
- `OpPair.EmitOnMatch` + `EmitOpCode` 机制灵活, 但需 `GetPair()` 正确配置才能生效。

**(d) 优先级与结合性**
```csharp
bool shouldPop = (topPrec > currPrec)
              || (topPrec == currPrec && assoc == Associativity.Left);
```
- 标准调车场规则: 高优先级先出栈; 同优先级左结合先出栈。

**(e) 终止指令**
```csharp
ret->Dest = regTop >= 0 ? regStack[0] : (byte)1;  // 默认返回 R1
```
- 寄存器栈有值时返回栈底 (第一个结果), 否则返回 R1。
- 多值表达式只有栈底被返回，其余值丢弃。

**潜在问题**:
- 寄存器永不重用，长公式可能提前耗尽 253 个寄存器。
- `"Out of registers."` 错误信息不含上下文, 调试困难。

---

### 2.5 FluxFormula.cs

**定位**: 编译后的不可变字节码容器。

**技术要点**:

**(a) TOper 类型校验（v3.0.0: 已移除）**
- v3.0.0 移除了 `TOper` 泛型参数。`FluxToken.Oper` 现在直接是 `byte`，永远是 1 字节，无需运行时校验。此前的 `sizeof(TOper) != 1` 静态构造函数检查已删除。

**(b) Connect() 拼接 — 含空公式保护**
```csharp
if (this.Count == 0) return next;
if (next.Count == 0) return this;
```
- v1.3.0 加入的卫语句, 保护空公式拼接边界, 避免 `newCount = -1 + next.Count` 导致负数数组长度崩溃。
- 注意: `Connect()` 仍产生一次 `new Instruction[newCount]` 堆分配, 这是编译期唯一的 GC 分配点。

**(c) Raw() / ToBytes() / FromBytes()**
- `Raw()`: 返回 `ReadOnlySpan<Instruction>`, 仅暴露 `Count` 长度的有效区域。
- `ToBytes()`: 序列化为紧凑字节数组, 零额外分配（memcpy 写入）。
- `FromBytes()`: 从字节数组反序列化, 无需重新编译。典型场景: 公式热更新。

**潜在问题**:
- `Connect()` 不重映射寄存器号, 两个公式使用相同寄存器时拼接后发生覆写。当前由调用方负责保证寄存器一致性, 无框架级冲突检测。

---

### 2.6 FluxAssembler.cs

**定位**：主入口点，编排编译→实例化的全流程。

**技术要点**:

**(a) JIT 自动降级 (v1.3.0 新增)**
```csharp
if (jit && !FluxPlatform.IsJitDisabled)
{
    try
    {
        var func = FluxJITCompiler<TData, TOper, TDef>.Compile(...);
        return new FluxInstance(..., func, true);
    }
    catch (Exception ex) when (
        ex is PlatformNotSupportedException
        || ex is NotSupportedException
        || ex is InvalidOperationException)
    {
        FluxPlatform.DisableJit();
    }
}
// fall through to interpreter
```
- 在 `Instantiate()` 中封装 try-catch, 捕获 IL2CPP/AOT 平台调用 `Expression.Compile()` 失败的三类异常。
- 失败后调用 `FluxPlatform.DisableJit()`, 后续同进程内所有 `Instantiate(jit: true)` 直接跳过 JIT 尝试。
- 首次 JIT 失败降级后不影响后续调用性能（跳过 try-catch 进入直接走解释器分支）。

**(b) 缓冲大小预估**
```csharp
int dataSlots = (sizeof(TData) + 7) / 8;
var buffer = new Instruction[tokens.Length * (1 + dataSlots) + 1];
```
- 每 token 预留 1 个指令头 + dataSlots 个数据槽, 加上 1 个末尾 Return。
- 纯操作符 token 的数据槽被浪费，这是保守估计，安全但非紧凑。

**(c) FluxType 判定**
```csharp
if (kind == OpType.Instruction) {
    if (pairInfo.PairRole != Pair.Left) {
        type = FluxType.Modifier;
    }
}
```
- 首 Token 是 Instruction 且非左括号 → Modifier（缺少源的片段）。
- 首 Token 是 Immediate → Formula。

**(d) CreateInjector — 两次扫描**
- 第一遍: 统计 Immediate 指令数量（分配 offsets 数组）。
- 第二遍: 记录每个 Immediate 的数据偏移量。
- 避免使用 `List<int>`（产生 GC）, 用裸数组。时间换空间的设计选择。

**潜在问题**:
- `buffer.ToArray()` 复制整个公式缓冲。对于大公式（数千条指令）这是显著的分配。若公式缓存后多次 `Instantiate()`, 每次都会复制一次。JIT 路径可考虑避免此复制。
- `CreateInjector()` 两次扫描可优化为单次（先分配预估大小的数组, 不够再扩容）, 但当前方案保证了精确的数组大小。

---

### 2.7 FluxInjector.cs

**定位**：数据注入器，将用户参数写入字节码缓冲。

**技术要点**:

**(a) 两种模式**
```csharp
// JIT 模式: offsets = null, 线性索引
offset = paramIndex * _slotsPerData;

// 解释器模式: offsets 指向 Immediate 数据在公式缓冲中的位置
offset = _offsets[paramIndex];
```

**(b) Set() 的指针写入**
```csharp
fixed (Instruction* pBase = _buffer) {
    *(TData*)(pBase + offset) = value;  // 指针重解释, 零拷贝写入
}
```
- `pBase + offset` 中 offset 的单位是"Instruction 个数"而非字节。`Instruction*` 算术自动处理乘法。

**(c) Set() 的命名变量注入**
- 内联二分查找在 `VariableSlots` 中定位变量名, O(log n) 复杂度。
- 同名变量所有出现位置同时更新。

**确认修复 (v1.3.0)**:
- `ToString()` 现已正确返回 `"FluxInjector<{typeof(TData).Name}>"`, `FluxBinder` 遗留命名已修正。

**潜在问题**:
- `Set()` 在 JIT 模式下的越界检查检查了 `offset + _slotsPerData > _buffer.Length`, 但 `offset + _slotsPerData` 可能 int 溢出，对极大索引值缺少保护。实际使用中索引值很小，风险极低。

---

### 2.8 FluxInstance.cs

**定位**: 流式 API, 用户直接交互的 ref struct。

**技术要点**:

**(a) ref struct 设计**
```csharp
public ref struct FluxInstance<TData, TDef>
```
- v3.0.0: 移除 `TOper` 泛型参数。允许 `Set()` 修改内部状态 (injector)。栈分配, 不可装箱, 不可作为类字段。

**(b) Run() 的分发**
```csharp
if (_isJit)
    return _jitFunc(_injector.GetBuffer());    // JIT 委托
else
    return new FluxEvaluator<TData, TDef>(_definition).Compute(_formula.Raw());  // 解释器
```
- JIT: 传入注入后的数据缓冲 (紧凑 payload 数组)。
- 解释器: 每次 new FluxEvaluator (`ref struct`, 栈分配), 传入完整公式缓冲。

**(c) Modifier 保护（v3.0.0: 编译期保证）**
- v3.0.0: `FluxModifier<TData, TDef>` 没有 `Instantiate()` 方法，任何尝试独立求值 Modifier 的代码**编译不过**。运行时的 `InvalidOperationException` 检查已转为 `Debug.Assert` 防御性保留。

---

### 2.9 FluxJITCompiler.cs

**定位**: JIT 编译器, 将字节码转为 LINQ Expression Tree 然后编译为委托。

**技术要点**:

**(a) 数据载荷提取**
- JIT 路径分离"指令"和"数据"：指令成为 Expression Tree，数据成为 `Instruction[]` 参数。
- `payload` 的 Instruction 槽中存放的实际上是 TData 的二进制表示。

**(b) 立即数加载 (SafeCast)**
- 每次 Immediate 生成一个 `GetData<TData>(buffer, index)` 调用, 内部通过 `fixed` 指针从 `dataBuffer[index]` 读出 TData。
- 每个 SafeCast 需要一次 `fixed` 语句，运行时多次指针 pin。

**(c) 错误短路**
```csharp
Expression.IfThen(
    Expression.NotEqual(regs[0], defaultTDataExpr),
    Expression.Return(returnTarget, regs[0])
);
```
- 与解释器同构，每个 Instruction 后检查 R0。

**(d) 返回逻辑**
```csharp
Expression.Condition(
    Expression.NotEqual(regs[0], defaultTDataExpr),
    regs[0],           // R0 非默认 → 错误
    regs[inst.Dest]    // 否则返回 Return 指令的目标寄存器
);
```
- JIT 的 Return 处理比解释器更精确：明确使用 `inst.Dest` 而非回退到 `regs[1]`。

**(e) Expression.Compile()**
- `Expression.Lambda<CompiledFunc>(block, bufferParam).Compile()`。
- 在 .NET 中, `Compile()` 内部也做解释→JIT 转换, 有一定开销。
- 编译后的委托是强类型的 `Func<Instruction[], TData>`。
- AOT 平台 (IL2CPP, iOS, WebGL) 不支持 `Expression.Compile()`, 框架在 `FluxAssembler.Instantiate()` 中捕获异常并自动降级。

**潜在问题**:
- 生成的 Expression Tree 未缓存，每次 `Compile()` 重新构建 + 编译。对频繁编译的场景有开销，但公式缓存模式（编译一次，多次实例化）可规避。
- `SafeCast` 每数据槽调用一次；多次 `fixed` 可能影响性能，但对于大多数公式（几十个变量）影响可忽略。

---

### 2.10 编辑器扩展

**FluxFormulaExtensions.Dump()**:
- 对每个指令输出三种格式: 可读摘要、带标签二进制、64 位原始二进制。
- 使用共享 `StringBuilder` 减少分配。

**InstructionExtensions.ToBinary()**:
- 格式: `Op:XXXXXXXX | De:XXXXXXXX | R0:XXXXXXXX R1:XXXXXXXX ... R5:XXXXXXXX | Raw:XXXXXXXXXXXXXXXX`。
- 始终显示全部 8 个字段，即使 arity < 6 也会显示无意义的 Arg 值。

**FluxAssetEditor.cs**:
- 泛型 EditorWindow, 零反射。通过 `FluxEditorRegistry` 注册类型-编辑器映射。
- 支持公式编译、即时求值、变量调试。

---

## 3. 当前状态评估

### 3.1 已解决的历史问题

以下问题在版本演进中已确认修复:

| # | 原问题 | 解决方案 | 版本 |
|---|--------|----------|------|
| 1 | JIT 在 IL2CPP/AOT 平台崩溃 | `FluxPlatform` + `Instantiate()` try-catch 自动降级 | v1.3.0 |
| 2 | `Connect()` 无 Count=0 保护 | 卫语句: `if (this.Count == 0) return next; if (next.Count == 0) return this;` | v1.3.0 |
| 3 | TOper 底层类型无校验 | `TOper` 泛型参数移除，`FluxToken.Oper` 直接为 `byte`，无需校验 | v3.0.0 |
| 4 | 无真实测试 | 332 个单元测试（97.9% 行覆盖），覆盖编译/解释器/JIT/Connect/Lexer/持久化/缓存/blob/VFF | v1.5+ |
| 5 | 无 IFluxDefinition 示例 | `MathDef` 完整示例 + `TestDefinition.cs` | v1.5+ |
| 6 | 寄存器模型无文档 | VitePress 双语文档站完整覆盖核心概念/API/内部原理/pipeline | v1.5+ |
| 7 | FluxBinder 残留 | `ToString()` 已更新为 `"FluxInjector<{TData}>"` | v1.3.0 |
| 8 | AOT 兼容性未声明 | 文档/FAQ 明确标注平台支持矩阵 | v1.5+ |
| 9 | README 未实现 | 中英文双语 README + 徽章体系完成 | v1.5+ |

### 3.2 版本演进新增能力

| 能力 | 说明 | 引入版本 |
|------|------|---------|
| 编译缓存 | DualHash64 → FormulaCache → Delegate 缓存 | v1.5.0 |
| Blob 构建管线 | FluxBlobBuilder 扫描 FluxAsset → 拼接 blob → 生成 C# 偏移表 | v1.5.0 |
| VFF 虚拟公式 | "VFF\0" 格式持久化公式引用 + 参数覆写，DLL 式符号解析 | v1.5.0 |
| VFF 编码器 + IFluxFileFormatter | VffFormat.ToBytes() 编码 + FromBytes() 独立解码 + 文件持久化接口 | v2.0.0 |
| 格式集中化 | FormulaFormat/BinaryFormat 消除散落 helper，格式定义单一来源 | v1.5.0 |
| MaxRegister 按需分配 | 公式头部存储编译期分析的最高寄存器号 | v1.5.0 |
| 全局配置 | FluxConfig 替代硬编码常量 | v1.5.0 |
| Per-link JIT 链式求值 | Connect 产物按 link 分别 JIT，SetIndex(0, prevResult) 串联 | v1.5.0 |
| **TOper 移除** | `FluxFormula<TData, TDef>` 替代 `<TData, TOper>`，操作符枚举变为定义体 private 细节，4 运行时异常 → 编译错误 | **v3.0.0** |
| **Formula/Modifier 类型分裂** | `FluxModifier<TData, TDef>` 独立 struct，Connect 编译期类型安全 | **v3.0.0** |
| **IFluxFileFormatter** | Core 内置文件持久化接口 + `FileFluxFileFormatter` 实现 | v2.0.0 |
| **Pipeline 文档** | 8 篇管线文档中英双语（lexer/compiler/instruction/evaluator/jit/platform/overview/injector） | v3.0.0 |

### 3.3 当前待改进项

| # | 类别 | 问题 | 位置 |
|---|------|------|------|
| 1 | 性能 | `CreateInjector()` 两次扫描可优化为单次 | `FluxAssembler.cs` |
| 2 | 性能 | 解释器 `IsDefault()` 对大 struct 逐字节比较可特化 | `FluxEvaluator.cs` |
| 3 | 健壮性 | 寄存器永不重用, 253 个寄存器在超长公式中可能耗尽 | `FluxCompiler.cs` |
| 4 | 健壮性 | `"Out of registers."` 错误信息不含上下文 | `FluxCompiler.cs` |
| 5 | 健壮性 | `Set()` JIT 模式下 `offset + _slotsPerData` 可能 int 溢出（风险极低） | `FluxInjector.cs` |
| 6 | 文档 | 部分内部算法仍缺源代码级注释（调车场、寄存器分配、R1 注入） | `FluxCompiler.cs` |

---

## 4. 架构评价

### 优点
- **泛型设计**: v3.0.0 简化为 TData/TDef 两层参数：操作符枚举变为定义体 `private` 细节，所有操作符方法使用 `byte`。编译时类型安全，零虚调用开销。
- **零 GC 达成度**: ref struct + stackalloc + unsafe + unmanaged 约束覆盖了从 Instantiate 到 Run 的完整热路径。
- **双后端策略**: 解释器零编译开销、全平台兼容; JIT 编译后接近原生性能。自动降级让用户在 IL2CPP 平台无需感知切换。
- **紧凑字节码**: 8 字节定长指令, 适合缓存、序列化和跨平台传输。
- **OpPair 系统**: 将语法映射到语义的机制灵活, 可模拟函数调用等复杂语法。
- **Lexer 设计**: 配置驱动的 Span 扫描器, 零依赖零分配, 长符号优先匹配策略正确。

### 架构约束
- 寄存器不复用决定了公式变量数上限为 253, 对超过此规模的公式需拆分处理。
- `Connect()` 不重映射寄存器号, 多公式拼接需由调用方保证寄存器一致性。
- JIT 路径依赖 `Expression.Compile()`, 在 AOT 平台完全依赖解释器；但这是 .NET 生态的客观限制，框架已做最大程度的自动处理。
