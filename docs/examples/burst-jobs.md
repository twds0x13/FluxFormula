# 示例：Burst Jobs 求值

> **依赖包：** `com.twds0x13.fluxformula.burst`（3.3+）
> 此包依赖 `com.unity.burst` 和 `com.unity.collections`，UPM 安装时自动拉入。纯 .NET 项目（非 Unity）无需此包。

以下示例演示 `FluxBurstInstance<TData, TDef>` 的三种使用模式：同步求值、异步 Job 调度、多 Job 并发。

## 同步求值

调用 `Run()` 在当前线程同步执行。适合单次求值或 Editor 脚本。

```csharp
using FluxFormula.Core;
using FluxFormula.Burst;
using Unity.Collections;

var assembler = new FluxAssembler<float, MathDef>(new MathDef());
var formula = assembler.Compile(new FluxToken<float>[]
{
    new() { Oper = (byte)MathOp.Const, Data = 100f },
    new() { Oper = (byte)MathOp.Add },
    new() { Oper = (byte)MathOp.Const, Data = 50f },
});

using var job = assembler.CreateBurstInstance(formula);
float result = job.Run();           // 150
Debug.Log(result);
```

`Set` 和 `SetIndex` 在 `Run` 前注入变量值，对标 `FluxInstance` 的链式 API：

```csharp
var formula = assembler.Compile(lexer.Lex("atk * 2 + bonus"));
using var job = assembler.CreateBurstInstance(formula);

job.Set("atk", 80f).Set("bonus", 25f);
float damage = job.Run();          // 185
```

## 异步 Job 调度

调用 `Schedule()` 将求值提交到 Unity Job 系统，不阻塞主线程。

```csharp
using var job = assembler.CreateBurstInstance(formula)
    .Set("atk", 120f)
    .Set("def", 40f);

var handle = job.Schedule();
// 主线程继续其他工作
handle.Complete();
float damage = job.Result;         // 从 R1 总线读取结果
```

## 多 Job 并发

每个 `FluxBurstInstance` 持有独立的 `NativeArray<byte>` 和 `NativeArray<TData>`，可安全并发调度。

```csharp
var job1 = assembler.CreateBurstInstance(damageFormula)
    .Set("atk", 100f);
var job2 = assembler.CreateBurstInstance(healFormula)
    .Set("wis", 60f);

var h1 = job1.Schedule();
var h2 = job2.Schedule();
JobHandle.CompleteAll(h1, h2);

float dmg = job1.Result;  // 伤害结果
float heal = job2.Result; // 治疗结果

job1.Dispose();
job2.Dispose();
```

`Schedule()` 支持传入前置依赖，用于串联多个 Job：

```csharp
var handle1 = job1.Schedule();
var handle2 = job2.Schedule(handle1);  // job2 等待 job1 完成
handle2.Complete();
```

## 注意事项

- 使用完毕后必须调用 `Dispose()` 释放 `NativeArray`。推荐 `using` 声明。
- Job 内不能使用 JIT 路径。`FluxBurstEvaluator` 是纯解释器，由 Burst 编译到接近 JIT 水平。
- `Set(string)` 在托管堆上解析变量名。热路径中优先用 `SetIndex` 按槽位索引注入。
- Burst Inspector 可验证 `Execute` 方法零托管逃逸。

## 参见

- [FluxChain API](../api/flux-chain) — 链式公式专用 API
- [FluxAssembler](../api/flux-assembler) — 编译入口
- [Burst 官方文档](https://docs.unity3d.com/Packages/com.unity.burst@latest)
