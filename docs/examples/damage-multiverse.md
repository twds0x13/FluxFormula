# 示例：暴击多世界线模拟（柯里化分叉）

暴击判定在传统做法中是每次模拟全部变量重新注入：1000 次模拟 = 3000 次 `Set`。Curry 的分叉能力改变了这一点：绑定共同变量一次，剩余的那个变量每次 fork 一份新实例独立求值。

此示例定义了一个含 `select` 三元运算符的伤害公式，使用 `FluxCurryEvaluator` 绑定攻击力和暴击伤害后，通过自定义 `.Multiverse()` 扩展方法对"是否暴击"做多世界线模拟并取算数平均。

## 伤害公式

```csharp
[atk] * ([isCrit] ? 1 + [critDmg] : 1)
```

- 暴击时：`atk * (1 + critDmg)`
- 非暴击时：`atk * 1`

`select` 同时支持函数语法 `select(a, b, c)` 和三元语法 `a ? b : c`。

## 定义体

`DamageDef` 实现 `IFluxExprDefinition<float>`，包含标准四则运算和三元 `Select`。`Select` 的 `GetArity` 返回 3（条件 + 真值 + 假值），`Compute` 中为三元表达式 `regs[Arg0] != 0f ? regs[Arg1] : regs[Arg2]`。

完整代码见 `examples/DamageMultiverse/DamageDef.cs`。

## PCG64 可回溯随机数

`Pcg64(ulong seed)` 提供确定性的随机数序列——相同种子永远产出相同序列。`NextFloat()` 返回 `[0, 1)` 区间的浮点数。

## Curry 绑定

```csharp
var formula = runner.Compile(lexer.Lex(
    "[atk] * ([isCrit] ? 1 + [critDmg] : 1)"));

var baseState = FluxCurryEvaluator<float, DamageDef>.Create(def, formula)
    .Bind("atk", 100f)
    .Bind("critDmg", 0.5f);
// baseState: atk=100, critDmg=0.5, isCrit 未绑定 (2/3)
```

## Multiverse 扩展方法

`.Multiverse()` 是 `FluxCurryEvaluator<float, TDef>` 的扩展方法，提供三种重载：

### 简单阈值

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000, critRate: 0.3f, rng);
// 每次迭代 rng.NextFloat() < 0.3 → 暴击
```

### 委托判定

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000, rng =>
{
    counter++;
    return counter % 3 == 0; // 每第三次暴击
}, rng);
```

### 外部 FluxFormula 判定

```csharp
float avg = baseState.Multiverse("isCrit", count: 10000,
    judgeAssembler, judgeFormula, rng);
// 每次迭代注入随机数到判定公式，结果 > 0.5 算暴击
```

所有重载共享同一实现模式：缓存当前 curry → 循环 fork → 绑 isCrit → `ForceComplete()` → 取 Result → 收集统计量。

除算术平均值外，Multiverse 同时输出 Max、Min、Mid（中位数），供后处理公式使用。

## 后处理与 TrySet

模拟结果可通过后处理公式进一步加工。后处理公式由配置决定（`[Max] - [Avg]` 衡量波动性、`[Avg] * (1 + [Max] / [Min])` 计入极值比），注入代码无需预先知道公式签名：

```csharp
// 模拟阶段
var stats = baseState.MultiverseStats("isCrit", 10000, 0.3f, rng);

// 后处理：TrySet 注入全部统计量，公式决定用哪些
float final = postProcessRunner.Instantiate(postProcessFormula)
    .TrySet("Avg", stats.Avg)
    .TrySet("Max", stats.Max)
    .TrySet("Min", stats.Min)
    .TrySet("Mid", stats.Mid)
    .Run();
```

`TrySet` 变量不存在时静默跳过，因此后处理公式可以只声明 `[Avg]` 或 `[Max] - [Min]`，注入代码保持不变。

## 与传统做法的对比

**传统做法**（每次全量 Set）：
```csharp
for (int i = 0; i < 10000; i++)
{
    bool crit = rng.NextFloat() < 0.3f;
    float dmg = runner.Instantiate(formula, jit: true)
        .Set("atk", 100f).Set("critDmg", 0.5f).Set("isCrit", crit ? 1f : 0f)
        .Run();
    sum += dmg;
}
```

**Curry + Multiverse**：
```csharp
var baseState = FluxCurryEvaluator<float, DamageDef>.Create(def, formula)
    .Bind("atk", 100f).Bind("critDmg", 0.5f);
float avg = baseState.Multiverse("isCrit", 10000, 0.3f, rng);
```

每次迭代只需绑 1 个变量而非 3 个。`baseState` 在全部模拟中保持不变，可被多次复用。

## 注意事项

- Multiverse 结果**不经过** `.Result`——函数直接返回 `float` 平均值
- 原始 curry 实例在 Multiverse 执行后**不受影响**（`readonly struct` + 每 fork 新实例）
- PCG64 的可回溯性意味着"30% 暴击率"两次运行结果完全一致
