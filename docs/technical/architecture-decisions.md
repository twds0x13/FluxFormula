# 架构决策记录

本文记录 FluxFormula 核心架构中已做出的关键决策及其理由。每个决策含背景、选项、选择和后果。

## ADR-1: 双重哈希（DualHash64）

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
编译缓存需要一种内容寻址方案（content-addressed storage）。单哈希（如 SHA256）安全但慢；单非密码学哈希（如 xxHash64）快但存在结构性碰撞风险。

### 选项
1. SHA256：安全，但每条公式重算开销大
2. xxHash64 only：快，但约 2³² 生日碰撞空间
3. xxHash64 + FNV-1a 64 组合：两个正交的非密码学哈希

### 选择
**选项 3**。两个哈希的内部结构独立（xxHash 用乘法混洗+旋转，FNV 用 XOR+质数乘法），攻击者需同时击破两者。组合后碰撞难度远超非密码学哈希的单独上限。

### 后果
- 128-bit key 带来 16 字节存储开销（相对于 64-bit），可接受
- `Combine()` 提供 O(1) 链 key 计算，无需重新扫描字节序列
- 不是密码学安全：恶意行为者若同时控制 blob 和偏移表无法防御。安全性依赖偏移表编译进 assembly

---

## ADR-2: 哈希存偏移表而非 Blob

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
公式字节码经过编译后生成 `.ff` 缓存文件，需要存储其哈希以在运行时验证完整性。

### 选项
1. 哈希嵌入 blob 文件内（每条公式前 16 字节头）
2. 哈希存储在 Source Generator 输出的偏移表中（编译进 assembly）

### 选择
**选项 2**。Blob 内存储哈希等同于"自己验证自己"：攻击者改 blob 时可同步改哈希。偏移表编译为 IL，篡改需反编译并重编译程序集。

### 后果
- 偏移表体积小（每条公式 24 字节：offset + length + dualHash），编译器转为 data section 常量
- 运行时验证：偏移表取期望哈希 → blob 取字节 → Compute → 比对
- Source Generator 必须在 Player Build 的 C# 编译前运行

---

## ADR-3: Connect 延迟物化（ChainLink）

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
Connect 最初做全量字节码拼接（`Array.Copy` + 新 `Instruction[]` 分配）。每次 Connect 都分配合并产物，N 次 Connect 产生 N 个中间分配的合并缓冲。

### 选项
1. Connect 始终合并字节码（原行为）
2. Connect 只追加元数据引用（ChainLink），物化推迟到求值时

### 选择
**选项 2**。等效于 LINQ 的延迟求值。`Connect` 只在 `ChainLink[]` 末尾追加引用切片，物理合并推迟到 `Run()`（短链 per-link）或 `ToAtomic()`（长链/JIT）。Threshold（8 links）从 Connect 移至 `Instantiate`，职责分离。

### 后果
- Connect 零分配（只追加 ChainLink 数组元素）
- 代码路径增加（chain vs atomic 双表示），但由 `Instantiate` 统一决策
- Per-link 解释器求值（`RunChainInterpreter`）和 ToAtomic 合并求值通过解释器中段 Return 语义达成一致

---

## ADR-4: Delegate 缓存（GCHandle 方案）

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
JIT 编译（`Expression.Compile()`）开销大，同一公式多次实例化不应重编译。

### 选项
1. 不缓存：每次 Instantiate 重编译
2. 公式持有 WeakReference 到自身 delegate
3. 全局 FormulaCache 用 GCHandle 存 delegate

### 选择
**选项 3**。`GCHandle.Alloc(func)` → `GCHandle.ToIntPtr()` 存入 FormulaCache 的 `IntPtr` 槽位。与字节码指针共用存储空间（通过 `DelegateSlot = -2` 状态标记区分）。

### 后果
- 同一公式无论实例化多少次，只编译一次
- Delegate 的 GCHandle 生命周期由 FormulaCache 管理（驱逐/Compact 时释放）
- IL2CPP 上 Expression.Compile 不可用，自动降级为解释器，缓存不适用

---

## ADR-5: Formula ↔ Modifier 互转

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
Formula 和 Modifier 是同一种字节码的两种视图：Formula 有完整操作数，Modifier 的第一操作数从 R1 总线读入。

### 选项
1. 只通过 Connect 隐式转换
2. 提供显式 `ToMultiplier()` / `ToFormula()` API

### 选择
**选项 2**。`ToMultiplier()` 在字节码级移除第一 Immediate 指令并将 dest 寄存器重命名为 R1。`ToFormula(varName)` 插入命名变量替代 R1。Connect 不自动调用 `ToMultiplier`，语义选择留给调用方。

### 后果
- 用户显式控制链语义：`Connect(A, B.ToMultiplier())` = B 消费 A 的输出；`Connect(A, B)` = B 独立运行
- `ToMultiplier`/`ToFormula` 是字节码变换操作，分配新 `Instruction[]`
- 内部变量前缀 `CHAIN_LINK_INTERNAL_` 保留，用户不得使用

---

## ADR-6: 解释器中段 Return 语义

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
ToAtomic 合并链字节码时，中间 link 的 Return 指令不能简单保留（会导致解释器提前退出），也不能简单丢弃（会导致 R1 输出寄存器未设置）。

### 选项
1. 丢弃中间 Return + 插入显式寄存器拷贝指令（需修改指令集）
2. 丢弃中间 Return + 接受 ToAtomic 和 per-link 路径结果不一致
3. 修改解释器：中间 Return 不退出，而是复制 Dest → R1 并继续

### 选择
**选项 3**。当 Return 指令后还有更多指令（`ip + 1 < raw.Length`），解释器将 `regsPtr[Dest]` 复制到 `regsPtr[1]`（R1 总线）并继续执行。正常字节码中 Return 总是最后一条指令，不改变现有行为。

### 后果
- ToAtomic 可以保留所有中间 Return，不再丢弃任何指令
- 链式 per-link 求值和 ToAtomic 合并求值对所有链类型产出一致结果
- 解释器增加一条分支判断，开销可忽略

---

## ADR-7: 阈值合并决策集中在 Instantiate

**日期**: 2026-06-21
**状态**: 已采纳

### 背景
Connect 最初内置了阈值判断（>8 links → 合并），将"何时合并"的决策与"如何连接"耦合。

### 选项
1. 阈值判断在 Connect 内（原行为）
2. Connect 始终产链，阈值判断在 Instantiate

### 选择
**选项 2**。Connect 只负责描述链关系（组装 ChainLink[]）。合并策略（是否调用 ToAtomic）由 Instantiate 根据路径（JIT/解释器）和链长决定。

### 后果
- Connect 代码简化（`Connect` → `GetLinks` + `ChainConnect`，3 方法变为 2）
- 合并策略统一在一处（Instantiate），便于后续调优
- 解释器：短链 per-link，长链合并。JIT：per-link delegate 串联（SetIndex(0, prevResult) 注入 R1）

---

## 已采纳（v1.7.0）

| 议题 | 说明 |
|------|------|
| Per-link JIT 求值 | `FluxAssembler.InstantiateJitChain()` 为每 link 独立编译 delegate，`FluxInstance.RunJitChain()` 通过 `SetIndex(0, prevResult)` 串联。消除 JIT 路径的 ToAtomic 强制合并。 |
| MaxRegister 按需分配 | 公式头部第 14 字节存储编译期最高寄存器号。`FluxEvaluator` 和 `FluxExprCompiler` 按需 stackalloc 和创建 ParameterExpression，替代全量 255。 |
| FormulaFormat / BinaryFormat 集中化 | 格式定义和字节级 I/O 各集中为单一源文件，消除此前 9+ 个散落 helper。 |
| FluxConfig 全局配置 | 替代硬编码常量（缓存容量、合并阈值、缓冲区大小）。Unity 端通过 `FluxConfigAsset` ScriptableObject 自动注入。 |

## 待决策

| 议题 | 说明 |
|------|------|
| ChainLink 存储格式 | 当前为 `Instruction[]` 引用。可考虑 `byte[]` 副本以消除 GC 边界问题（低优先级）。 |
| Connect 自动 `ToMultiplier` | 当前 Connect 不自动转换。`Connect(A, B)` = B 独立，`Connect(A, B.ToMultiplier())` = B 消费 A。是否需要默认语义？ |
