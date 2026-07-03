# 常见问题

## "Modifier cannot run standalone" 是什么错误

编译了以二元运算符开头的 Token 序列（如 `+ 5`），它被判定为 Modifier（内部 `FluxType.Modifier`）。**v3.0.0 起此错误在编译期被阻止**：`FluxModifier<TData, TDef>` 没有 `Instantiate()` 方法，任何尝试独立求值 Modifier 的代码编译不过。

```csharp
// 编译错误：以 Add 开头 → 产出 FluxModifier，没有 Instantiate()
// var inst = runner.Compile(lexer.Lex("+ 5")).Instantiate(...);  // CS1061

// 正确：拼接到完整公式
var f1   = runner.Compile(lexer.Lex("10"));
var mod  = runner.Compile(lexer.Lex("+ 5"));
var combined = f1.Connect(mod); // 10 + 5（Connect 接受 FluxModifier）
```

## Connect 之后结果不正确

`Connect()` 不重映射寄存器号。如果两个公式使用了相同的寄存器，拼接后会发生覆写。安全用法：

- 连接空公式（`FluxFormula<TData, TDef>.Empty`）：零寄存器冲突
- 在单次 `Compile()` 中完成完整表达式编译，而非编译后拼接

如果需动态组合大量公式片段，建议在 Token 层面拼接后再一次性 `Compile()`。

## JIT 和解释器的选择

| 场景 | 推荐 |
|------|------|
| Unity Editor 开发调试 | JIT（编译后执行速度更快） |
| IL2CPP 平台（iOS/WebGL/主机） | 无需选择，JIT 自动降级到解释器 |
| 公式仅执行一两次 | 解释器（免编译开销） |
| 公式编译后反复运行数千次 | JIT |

默认使用 `jit: false`（解释器），确认公式复用频率高后再切换 JIT。

## iOS / WebGL 兼容性

解释器路径全平台可用。`Expression.Compile()` 在 IL2CPP 平台不受支持，框架检测到异常后自动设置 `FluxPlatform.DisableJit()`，后续所有 `Instantiate(jit: true)` 自动走解释器。无需编写平台判断代码。

## 如何调试出错的公式

**1. 查看字节码：**

```csharp
var formula = runner.Compile(lexer.Lex("1 + 2 * 3"));
#if UNITY_EDITOR
formula.Dump(); // 需要 using FluxFormula.Editor 扩展
#endif
```

**2. 对比 JIT 和解释器结果：**

```csharp
var lexResult = lexer.Lex("1 + 2 * 3");
float interp = runner.Instantiate(runner.Compile(lexResult), jit: false).Run();
float jit    = runner.Instantiate(runner.Compile(lexResult), jit: true).Run();
// 如果结果不一致 → IFluxDefinition.Compute 和 GetExpression 语义不同步
```

**3. 从简单公式逐步构建：**

```csharp
C(1f)                          // → 1
C(1f), Add, C(2f)              // → 3
C(1f), Add, C(2f), Mul, C(3f)  // → 7
```

## 操作符枚举为什么必须使用 `: byte`

> **v3.0.0**：`TOper` 泛型参数已移除。操作符枚举现在是定义体的内部实现细节，`FluxToken.Oper` 直接使用 `byte`。不再需要 `sizeof(TOper) != 1` 运行时检查：`byte` 永远是 1 字节。

```csharp
// v3.0.0：定义体内部枚举
enum MyOp : byte { Add, Sub, Mul }  // 仍然是 : byte，但不再是框架约束
```

## TData 能否使用自定义结构体

可以。只要满足 `unmanaged` 约束：

```csharp
public struct Vector3f { public float x, y, z; } // blittable，可用
public struct MyData  { public string Name; }     // 含引用类型，不可用
```

`sizeof(TData)` 越大，字节码中 Immediate 占用的 Instruction 槽位越多。每槽 8 字节，`Vector3f`（12 字节）= 2 槽。

## 最大公式长度

| 瓶颈 | 限制 |
|------|------|
| 寄存器数量 | 253 个通用寄存器（R2-R254），长公式可能耗尽 |
| 操作符栈 | 最大 64 层嵌套 |
| 单指令 arity | 最大 6（Instruction 只有 Arg0-Arg5） |
| 缓冲大小 | `int` 索引限制，理论上约 2^31 条指令 |

实际受限于公式中的变量数量（每个 Immediate 消耗一个寄存器），而非指令数。超过 200 个独立变量时，建议拆分为多个公式。

## ref struct 的设计原因

`ref struct` 只能存在于栈上，不可装箱（不会逃逸到堆）。配合 `stackalloc` 和 `unmanaged` 泛型，FluxFormula 的执行热路径确保零 GC 分配。

代价：`FluxInstance` 不可用于 lambda 捕获（如 `Assert.Throws(() => inst.Run())`），需改用 try-catch。

## 如何实现自定义错误处理

将错误值写入 R0 寄存器。`Compute()` 中返回非 default 值即可触发短路：

```csharp
public float Compute(byte op, Instruction inst, Span<float> regs)
{
    if (op == MyOp.Div && Math.Abs(regs[inst.Arg1]) < 1e-6f)
        return float.NaN; // 写入 R0，触发短路
    // ...
}
```

JIT 路径对应写法：

```csharp
public Expression GetExpression(byte op, Instruction inst, ParameterExpression[] regs)
{
    return Expression.Condition(
        Expression.Equal(regs[inst.Arg1], Expression.Constant(0f)),
        Expression.Constant(float.NaN),  // 触发短路
        Expression.Divide(regs[inst.Arg0], regs[inst.Arg1])
    );
}
```

每条指令执行后都会检查 R0，一旦非 default 立即终止并返回错误值。
