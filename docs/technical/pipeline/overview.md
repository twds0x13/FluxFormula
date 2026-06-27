# 管线全景

FluxFormula 的编译与执行管线分为四个阶段：**Lex（词法分析）→ Compile（编译）→ Instantiate（实例化）→ Run（执行）**。相邻阶段之间通过明确的类型边界传递状态，每层职责独立，上层不感知下层实现细节。

## 阶段总览

```
字符串 / Token[]
  │
  ├─ 1. Lex ────────────────────────────────────────────
  │   FluxLexer.Lex(string) → LexResult<TData, TDef>
  │   产出: FluxToken[] + 变量名列表
  │   分配: Token 数组 + 变量名字符串数组（一次性）
  │
  ├─ 2. Compile ────────────────────────────────────────
  │   FluxAssembler.Compile(LexResult) → FluxFormula<TData, TDef>
  │   内部: FluxCompiler (调车场算法) → Instruction[] 字节码
  │   产出: FluxFormula (不可变容器, 持有 Instruction[] 缓冲)
  │   分配: Instruction[] 缓冲（一次性）
  │
  ├─ 2b. Connect / VFF Encode ──────────────────────────
  │   FluxFormula.Connect() → FluxChain (ChainLink[])
  │   FluxChain.GetLinks() → VffFormat.ToBytes() → .vff 字节码
  │   产出: VFF 字节数组（可存为独立文件或嵌入 blob）
  │
  ├─ 3. Instantiate ────────────────────────────────────
  │   FluxAssembler.Instantiate(FluxFormula) → FluxInstance<TData, TDef, TDef>
  │   内部: 构建 FluxInjector + JIT 委托编译（IL 优先 → Expression 回退 → 解释器兜底）
  │   产出: FluxInstance (ref struct, 栈分配)
  │   分配: JIT 模式下编译委托（可缓存）, Injector 元数据（栈）
  │
  └─ 4. Run ────────────────────────────────────────────
      FluxInstance.Set(...).Run() → TData
      内部: 解释器或 JIT 委托执行字节码
      产出: 计算结果
      分配: 0 B（热路径零 GC）
```

## 阶段间类型边界

| 边界 | 输入类型 | 输出类型 | 不可变性 |
|------|---------|---------|---------|
| Lex → Compile | `string` | `LexResult<TData, TDef>` | LexResult 不可变 |
| Compile → Instantiate | `LexResult` / `FluxToken[]` | `FluxFormula<TData, TDef>` | FluxFormula 不可变 |
| Instantiate → Run | `FluxFormula` | `FluxInstance<TData, TDef, TDef>` | Instance 可修改 (Set) |
| Run → Result | - | `TData` | 值类型结果 |

关键设计：**编译产物（FluxFormula）是不可变的**，可被缓存在 FormulaCache 中并按 DualHash64 索引复用。实例化产物（FluxInstance）是轻量的 ref struct，栈分配，每次求值重新创建。

## 各阶段设计决策

### 1. Lex：手写 Span 扫描器

**为什么不用正则？**
- 正则表达式在 .NET 中产生内部状态机和捕获组分配，违背零 GC 目标
- 手写 `ReadOnlySpan<char>` 扫描器零分配（除产出数组），性能可控
- 运算符匹配需长符号优先（`**` 在 `*` 之前），正则的 alternation 不保证匹配顺序

**为什么分两轮扫描？**
- 第一轮：按符号切分 Token，记录位置
- 第二轮：检测 juxtaposition（如 `2(atk)`），插入隐式运算符
- 两轮分离保持每轮逻辑简单，O(2n) 仍是线性复杂度

### 2. Compile：调车场算法

**为什么选调车场而非递归下降？**
- 调车场天然处理优先级和结合性，无需手写递归层级
- 操作符优先级和结合性通过 `IFluxDefinition` 注入，算法本身与运算符无关
- 生成的 RPN（逆波兰）直接对应寄存器虚拟机的执行顺序

**为什么 Instruction[] 缓冲大小是保守估计？**
- `tokens.Length * (1 + dataSlots) + 1`：每个 token 最多产生一个指令头 + TData 槽位
- 纯操作符 token 的数据槽被浪费，但避免了动态扩容带来的 GC
- 最终通过 `buffer.AsSpan(0, actualCount).ToArray()` 裁剪

### 3. Instantiate：编译-执行分离

**为什么分离 Compile 和 Instantiate？**
- 同一公式编译一次，不同参数反复求值：缓存 FlxFormula，反复 Instantiate
- JIT 委托编译是昂贵操作，分离后委托可缓存在 JitCache 中
- Instantiate 返回 ref struct，生命周期受限于栈帧。分离避免了长生命周期的 Formula 被栈约束

**JIT 自动降级机制**
- JIT 委托编译包含两条路径：IL 发射（`FluxILCompiler`，Mono/CoreCLR 优先）和 Expression 树（`FluxExprCompiler`，全平台回退）
- IL2CPP / AOT 平台不支持 `Expression.Compile()` 和 `DynamicMethod`
- `CompileDelegate` 按 IL → Expression → 解释器三阶降级
- 首次失败后 `FluxPlatform.DisableJit()` 置位，同进程后续调用跳过 JIT

### 4. Run：双后端执行

**解释器路径**：
- `stackalloc` 分配 256 个 TData 寄存器（64 字节对齐）
- `fixed` 指针固定字节码缓冲
- 逐条指令循环，R0 非 default 时短路返回

**JIT 路径**：
- 委托已预编译（IL 发射或 Expression 树，从缓存获取）
- 传入注入后的 payload 数组
- 无循环、无分支预测失败，仅一次委托调用

**为什么解释器还需要存在？**
- AOT 平台（IL2CPP, iOS, WebGL）不支持 JIT 编译
- 解释器是通用兜底方案，零平台依赖
- 冷启动场景：首次 JIT 编译有延迟，解释器可立即执行

## 管线中的缓存介入点

```
Compile ──→ DualHash64.Compute(bytecode) ──→ FormulaCache.Put(hash, ptr, len)
                                                │
Instantiate ──→ FormulaCache.Instance.TryGetDelegate(hash) ──→ 命中 → 复用委托
                                                │
                                                └──→ 未命中 → JIT Compile → PutDelegate
```

缓存层在 Compile 和 Instantiate 阶段之间形成透明加速层。用户无需感知缓存的存在。`Instantiate(jit: true)` 在缓存命中时直接返回预编译委托，在未命中时执行完整 JIT 编译并写入缓存。

## 下一步

- [数据注入器](./injector.md)：Set/SetIndex 的内部机制
