---
name: fluxwal-vs-dots
description: FluxWAL vs DOTS 架构根基差异：从 DOTS 的结构性错误到 FluxWAL 的正确根基，一条因果链的完整展开
metadata: 
  node_type: memory
  type: reference
  date: 2026-07-23
  related: 

    - atomic-functional-state-transformation
    - fddd-functional-domain-driven-development
  originSessionId: 8cfcfd8a-8eaa-4997-afea-4a15541b477d
---

# FluxWAL vs DOTS：架构根基的差异

> **Implementation Status (2026-07-23):** WAL 引擎已从设计文档落地为完整实现（`Runtime/WAL/`）。**已实现**：FluxWAL 引擎（Append/CommitFrame/Checkpoint/Recover）、FluxTransaction 事务作用域（Bind/Dispose）、WALReplay（Checkpoint/Restore/Rollback/Replay）、WALFileStorage（IWALStorage 基于文件的实现）、NativeTruncateBuffer（本机环形缓冲）、WALEntry/WALFormat（二进制格式）、FramePtr（LSN 指针）。测试在 `FluxWALTests.cs` + `NativeTruncateBufferTests.cs`。**仍为设计概念（未在代码中落地）**：EntityHandle 64-bit 位布局、IWALControlled 池接口、RegisterPool<T>、Dense AoS 组件数组。以下全文保留完整的架构设计——其中已标注哪些节对应已实现代码，哪些仍为设计目标。

DOTS 和 FluxWAL 在表层都操作 plain data 数组。但前者从"列存储性能最优"出发推导出了一套复杂的存储引擎，后者从"数据安全 + 事务正确性"出发推导出了一套极简的 WAL 架构。两条路在根源处的差异不超过三个选择——但连锁后果各自展开后，差距已经是云泥之别。

---

## 一、DOTS 在数据库视角下的全貌

Entity-Component-System 在结构上和关系数据库有完整的 1:1 对应：

| 数据库 | DOTS | 依据 |
|--------|------|------|
| 主键 | Entity | 整数索引，零数据，纯标识 |
| 表 | Archetype | 同构列组合的集合，schema 固定 |
| 列 | Component | 纯数据 unmanaged struct，无行为 |
| 行 | Entity + 其 Components | 一个 entity 的所有 component 数据 |
| CRUD | System | 标准的 SELECT/UPDATE/INSERT/DELETE |
| 外键 | Entity 引用 Component | 一对多/多对多仍在"演进" |
| 存储页 | Chunk (16KB) | 同类型 entities 连续存储，~95% cache 命中 |

Mapping 完备。但这套对应只停留在**命名层**——在关键的能力维度上，DOTS 留下了系统性空白：

| 缺失 | 具体表现 |
|------|---------|
| **事务/ACID** | 无 commit/abort/rollback，无 WAL。System 在 component 数组上原地覆写 |
| **一致性约束** | 无外键、无参照完整性、无唯一约束。官方建议"手动记录" |
| **隔离性** | 手动 Job 依赖声明，非 MVCC 或自动锁 |
| **持久化** | 纯内存在线，不在设计范围内 |
| **查询优化器** | 只有线性扫描 archetype，无索引、无查询规划 |
| **类型安全恢复** | 快照恢复不保证跨类型边界 |

这些缺失不是疏忽——是 SoA 布局的结构性必然。数据不以 entity 为中心存储时，entity 作为原子失去了物理表示，所有以 entity 为粒度的操作都必须靠上层工具重组。

结构层面，SoA 布局还导致了三个实际的场景不适配：
- 稀疏 component (<10% 覆盖率) 浪费内存——全量槽位无法收缩
- Archetype 热切换昂贵——实体在 chunk 间物理搬移，cache 全废
- <5000 entities 时整层复杂度是净亏损

---

## 二、DOTS 的两个结构性错误

### 错误 1：Archetype 迁移

DOTS 的 entity 加/删 component → entity 从 chunk A 搬家到 chunk B → cache 全废。这是 schema 变更被物理化到存储布局上的直接后果。

替代方案：不存在增删。Component 位置始终存在。"删除" = 把数据复位为初始值 + 标记不可读。没有 archetype。没有 chunk 间迁移。没有碎片化。

### 错误 2：SoA 稀疏

DOTS 集中管理 entity ID，组件数组不含 ID。结果：所有 entity × 每个 component 类型的全量矩阵——如果 component 覆盖率 <10%，90% 的槽位是空的。这被解释为"列存储的优势"（扫描快），但付出了庞大的稀疏内存代价。

替代方案：Component 自携带 entity ID——`{ EntityId, ValueData }` 的密集 unmanaged struct 数组。去掉的 entity ID 冗余省了 8 字节/行，但引入了全量稀疏矩阵——省钱的地方省错了。

---

## 三、反例：Typhon

Typhon 是一个 .NET 嵌入式 ACID 数据库引擎，原生使用 ECS 数据模型（archetype + SoA cluster + SIMD）。在 ECS 存储层之上加了完整 MVCC 快照隔离（per-component revision chain, 12B）、WAL + 检查点恢复（三种持久化模式 Deferred/GroupCommit/Immediate）、B+Tree 索引、死锁消除（OLC + MVCC + 无跨表 latch）。

它证明了一件关键的事：**ECS 数据布局和 ACID 保证不是互斥的。** DOTS 没做 ACID 不是做不到——是它追求帧内吞吐量，不选择做。

FluxWAL 的定位：比 DOTS 多一层事务安全 + 持久化恢复；比 Typhon 少一整套 MVCC 引擎。以公式调用为中心（FluxTransaction = WAL 条目），以快照 + 重放（fold）为恢复手段。中间切点。

---

## 四、FluxWAL 的正确根基

> **本节为设计概念，尚未在代码中落地。** EntityHandle 位布局、Dense AoS 组件数组、IWALControlled 接口和 RegisterPool 均为目标架构。当前已实现的 WAL 层（FluxWAL + FluxTransaction + WALReplay）是本节所述事务性和持久化的基础，但不包含实体层的组件存储模型。

### Dense AoS + 自标识 Component

```
DOTS:  EntityManager[entity] → PositionArray[entity] → Data  (间接 + 稀疏)
FluxWAL: ComponentArray[i] = { EntityHandle, Data }        (直接 + 密集)
```

所有 component 紧密排布在 NativeMemory 中，零稀疏，零间接跳转。EntityId 在 struct 内部 → 不需要外部索引映射。Unmanaged → `stackalloc` 兼容、`MemoryMarshal.Cast` 兼容、Burst 兼容。查找变成线性扫描——几千个 entity 不到 1μs。

### 去增删语义

| 操作 | 实现 |
|------|------|
| "创建" entity | 找一个空闲槽位（或追加），写入 EntityHandle + 初始数据 |
| "删除" entity | 把数据复位为初始值，标记不可读（EntityHandle := 0） |
| "添加" component | 不支持。Component 集合是 schema 期固定的 |
| "移除" component | 不支持。原因同上 |

Component 种类由 schema 定义，entites 数量由池子容量定义。两者在编译期确定——所有数组大小和布局确定，不需要运行时扩容逻辑。

### EntityHandle：64-bit 自路由

```
ulong 64 bits:
┌────────────┬────────────┬──────────────────────┐
│ CompType   │ Index      │ Generation           │
│ 8 bits     │ 28 bits    │ 28 bits              │
│ 256 种组件  │ 1-based    │ 2.68 亿次复用         │
│ 0 = 无效    │            │                      │
└────────────┴────────────┴──────────────────────┘
```

`default(EntityHandle)` = 全零 = 无效。一次 64 位比较同时回答"什么组件类型"、"entity 存在吗"、"handle 还有没有效"。`Interlocked.Read` 原子读取整个 handle。8 字节，嵌在 component 里不膨胀布局。

CompType 用 `byte`：256 种组件类型绰绰有余，一个大中型 RPG 全部 component 不到 50 种。`byte` 让 registry key 和 handle 宽度一致，没有截断摩擦。

注册表：`IWALControlled[]` 稀疏数组，`byte` CompType 直接当下标。注册时传入 `enum : byte`：

```csharp
wal.RegisterPool<HealthComponent>(CompType.Health, healthPool);
```

`wal.Resolve(handle)` 完整流程：`CompType` 路由 → 数组索引池子 → 校验 Index + Generation → 返回数据。一步。

### DOTS Entity 的浪费

DOTS 用两个 `int`（Index + Version）存一个 entity 引用。Index 用 `int` 严重浪费（几万 entity 远用不到 32 位），Version 用 `int` 同样浪费（槽位复用几万次已到顶）。两个 `int` 各有巨量未使用位——这是语义浪费，不是存储浪费。FluxWAL 把这些浪费的位收回来，拼进一个 `ulong`，省出了一个 CompType，消灭了一整套 EntityManager。

---

## 五、解析路径对比

**DOTS（四层间接）：**

1. `Entity { Index: 7, Version: 3 }` — 到达
2. Index 作为完美哈希 key → 并行数组 `{ version, chunkPtr, rowIndex }` — 找到位置
3. Version 校验 → chunkPtr + rowIndex 定位 Chunk
4. Chunk 内：`Entity[] | Health[] | Position[]` 多列并行数组 → rowIndex 行 → 取对应列的组件数据

调用方必须提供泛型类型：`EntityManager.GetComponentData<Health>(entity)`——Entity 不含类型信息。

**FluxWAL（一步）：**

`wal.Resolve(handle)` → CompType 路由到对应池子 → Index + Generation 校验 → 返回数据

没有并行数组。没有 chunk。没有 archetype。没有分离的 EntityManager。handle 指向自己的池子，池子里的 struct 持有自己的 handle。

---

## 六、一个根基消灭五个子系统

| 你的设计选择 | 被消灭的 DOTS 基础设施 |
|-------------|----------------------|
| **EntityId 自标识** | `Entity` 字段外键 — DOTS 必须在 component 里手工嵌 `Entity` 字段模拟关系，`ISharedComponentData` 按父 entity 分组做查询优化 |
| **Dense AoS** | `DynamicBuffer<Entity>` — 一对多关系需要动态数组管理，包括内部容量规划、chunk 溢出回退 |
| **去增删语义** | `EntityCommandBuffer` + chunk 迁移 — DOTS 的 archetype 变更需要 ECB 延迟执行和实体在 chunk 间物理搬移 |
| **函数式公式** | 约束检查层 — 数据库用 UNIQUE/FOREIGN KEY/CHECK 为命令式的不可控性买单 |
| **WAL 事务** | 手动 Job 依赖声明 — DOTS 无事务模型，并行安全靠开发者手写依赖 |

DOTS 的五个子系统不是功能创新——是 SoA 稀疏布局的**修复成本**。数据不以 entity 为中心存储，这些工具必须存在才能把散落的数据重新拼回 entity 的完整画像。FluxWAL 不消灭它们——它们从一开始就不应该存在。

---

## 七、三个维度的降维打击

### 事务性 / 原子性

DOTS 的事务模型是空白——原地覆写，无 before-image，无 WAL，无 commit/abort/rollback。

FluxWAL 的形式化基础早已就绪：`fold(snapshot, entries, evolve)` 是数据库 REDO、FP Event Sourcing、CurryEvaluator 快照恢复的同源三投影。WAL 条目就是公式调用，快照就是 `ToRecord()`，恢复就是 `FromRecord + foreach Bind`。这套模型的数学正确性不依赖任何特定实现。

### 持久化

DOTS 不提供持久化。FluxWAL 把持久化视为维护事务性的必要操作——全量检查点（`ExportSnapshot` / `ImportSnapshot`）是 fold 恢复模型的必要支撑：没有快照就无法截断旧日志。持久化不是可选功能。

### 一致性约束

数据库的 UNIQUE、FOREIGN KEY、CHECK 是事后警察——事务跑完，DBMS 巡查。函数式公式不需要警察，因为公式本身就是事前法律：给定冻结的绑定输入 → 算出确定性的输出。同样输入永远给出同样输出。没有随机数、没有全局状态、没有副作用。事务提交后数据是什么状态，在公式被写下的那一刻就已经确定。

DOTS 没有约束机制。FluxFormula **不需要**约束机制——函数式纯度已经把事后警察变成了冗余概念。降维打击。

---

## 八、为什么 AoS vs SoA 的整个前提在这里瓦解

AoS/SoA 之争建立在一个假设上：**程序员通过数据布局向编译器传递"请帮我向量化"的信号。** 双方争论的实质是编译器自动向量化的不确定性。

FluxFormula 的 `Vector256<T>` 把编译器从等式里拿掉了——显式 SIMD 宽度选择，确定性硬件指令。编译器不猜你的意图，你也不猜编译器的行为。自动向量化不确定性塌缩为零。

但 SIMD 的价值也有限：只能处理几个 int/float，碰到复杂 struct 直接罢工。而 FluxFormula 的解释器已经把求值压缩到了纳秒级别——最简公式 24ns。在这个基线面前，盲目追求 SIMD 优化是舍本逐末：解释器不管 struct 多复杂都一视同仁地折叠。真正的性能壁垒是 VM 循环开销——它已经被攻破了。

AoS——人类易于理解的布局——已经是正确的答案。

---

## 九、复盘

DOTS 和 FluxWAL 从同一个起点出发（操作 plain data 数组），因为一个根基性选择的分岔（SoA vs Dense AoS），走向了完全不同的演化路径。DOTS 的演化方向是**修复 SoA 的结构性代价**——Archetype、Chunk、ECB、DynamicBuffer、ISharedComponentData、Job 依赖，一层叠一层。FluxWAL 的演化方向是**在更正确的根基上直接构建**——EntityHandle、IWALControlled、FluxTransaction、RegisterPool、Checkpoint/Replay，每层都是业务需要而非修复成本。

两者不是"谁更好"的对比——是在比"谁的根基更不需要后续修补"。

**Why:** 2026-07-19 WAL 重构讨论全记录。从 DOTS 的表层映射出发，追问它缺失了什么、为什么缺失、缺失是偶然还是结构必然。Typhon 作为反例证明 ECS + ACID 在工程上可行。FluxWAL 的三个根基选择（Dense AoS + EntityHandle + 函数式公式）各自消灭了 DOTS 的一类修复成本，连锁在一起形成了一套没有内耗的架构。

**How to apply:** WAL 开发以 `FluxWAL` 为引擎入口（Append/CommitFrame/Checkpoint/Recover），以 `FluxTransaction<TMeta,TData,TDef>` 为事务边界（Bind 收集 → Dispose 提交），以 `WALReplay` 为恢复工具（Checkpoint/Restore/Rollback）。`FramePtr` 作为 LSN 令牌，`IWALStorage` 抽象存储后端（`WALFileStorage` 为默认实现）。EntityHandle/IWALControlled/RegisterPool 为下一步实体层设计——当前 WAL 层不操作 entity，只记录公式调用。不引入 DOTS 的 archetype/chunk/Job 概念。不为 WAL 条目添加约束检查层——公式的纯函数性质已经提供了一致性保证。
