# 平台适配：JIT 检测、IL 可用性与降级

`FluxPlatform` 是全局平台能力检测与 JIT 降级开关。它的核心设计问题：**如何在运行时检测 JIT 和 IL 发射是否可用，并在不可用时逐级自动降级，同时避免反复尝试的浪费？**

## 双标志系统

```csharp
internal static class FluxPlatform
{
    private static volatile bool _jitDisabled;

    /// <summary>Expression.Compile() 是否不可用（已检测到降级）</summary>
    public static bool IsJitDisabled => _jitDisabled;

    /// <summary>DynamicMethod / ILGenerator 是否可用</summary>
    public static bool IsIlSupported =>
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    public static void DisableJit() => _jitDisabled = true;
}
```

两个标志控制不同的编译路径：

| 标志 | 含义 | 检测方式 | 控制范围 |
|------|------|---------|---------|
| `IsIlSupported` | DynamicMethod 可用 | `RuntimeFeature.IsDynamicCodeSupported` | IL 发射路径 |
| `IsJitDisabled` | Expression.Compile() 已失效 | 运行时异常触发 `DisableJit()` | 表达式树路径 |

`IsIlSupported` **预先检测**（进程启动时结果已确定，无运行时开销），`IsJitDisabled` **运行时标记**（首次 JIT 失败后置位）。

## IL 可用性检测

`RuntimeFeature.IsDynamicCodeSupported` 是 .NET 运行时提供的静态属性：

- **Mono / CoreCLR**：返回 `true`，`DynamicMethod` 和 `ILGenerator` 可用
- **IL2CPP / NativeAOT**：返回 `false`，任何 `DynamicMethod` 构造均抛出 `PlatformNotSupportedException`

`CompileDelegate` 在入口处通过 `IsIlSupported` 提前短路，避免 IL2CPP 平台的无效 try-catch：

```csharp
if (FluxPlatform.IsIlSupported)
{
    // IL 路径仅在 Mono/CoreCLR 上进入
}
// IL2CPP 直接跳过，走 表达式树
```

## JIT 不可用的平台

以下平台不支持 `Expression.Compile()`：

- **IL2CPP**（Unity 的 AOT 编译后端）：iOS、WebGL、大多数 console
- **NativeAOT**（.NET 的原生 AOT 部署）
- **Mono 的 Full AOT 模式**（部分 Android 配置）

在这些平台上，`Expression.Compile()` 调用抛出 `PlatformNotSupportedException`。注意：IL2CPP 平台 `IsIlSupported` 为 false，不会进入 IL 路径；NativeAOT 和 Mono Full AOT 的 `IsIlSupported` 可能为 true（取决于运行时配置），IL 路径的降级由 try-catch 处理。

## 一次性检测 + 全局降级

`DisableJit()` 的设计要点：

- **`volatile`**：确保多线程可见性。虽然在 Unity 主线程场景下不关键，但为潜在的异步编译场景预留。
- **不可逆**：一旦检测到 JIT 不可用，整个进程生命周期内不再尝试。没有 `EnableJit()`，JIT 能力不会在运行时恢复。
- **手动触发**：`DisableJit()` 由 `CompileDelegate` 在 表达式树编译失败时调用。也可以由用户主动调用（如在已知 AOT 平台上跳过不必要的一次尝试）。

## 三阶降级触发链

```
FluxAssembler.Instantiate(jit: true)
  └→ TryResolveJitDelegate (FormulaCache 查询)
       └→ CompileDelegate (编译器选择器)
            ├─ (1) FluxILCompiler.Compile()       ← IL 发射 (IsIlSupported 为 true 时)
            │    └→ PlatformNotSupportedException → 降级到 (2)
            └─ (2) FluxExprCompiler.Compile()      ← 表达式树 (全平台回退)
                 └→ PlatformNotSupportedException → FluxPlatform.DisableJit()
                      └→ (3) 回退到解释器路径
```

降级是**透明且自动的**。调用方不需要在 `Instantiate(jit: true)` 和 `Instantiate(jit: false)` 之间做选择。JIT 失败时，`Instantiate` 内部自动回退到解释器路径，同时返回的 `FluxInstance` 走解释器执行。

## 为什么不是 `jit: true` 默认？

`Instantiate` 的 `jit` 参数默认是 `false`。这是因为：

1. **安全默认**：解释器在任何平台都可用。JIT 不是。默认开启 JIT 会导致 IL2CPP 平台上的首次调用必然失败。
2. **显式选择**：用户需要明确表达目标平台支持 JIT。这在 Unity 的 Inspector（`FluxAsset` 的 JIT 开关）中有 UI 提示。
3. **降级成本**：首次 JIT 失败的异常抛出和捕获有性能开销。如果已知目标平台不支持 JIT，直接传 `false` 避免浪费。

## Unity 集成

在 Unity 端，`FluxAsset` 提供 `UseJit` 属性，Inspector 中可勾选。勾选时，Asset 面板会显示平台兼容性警告（"JIT 在 IL2CPP 平台不可用"）。实际运行时，`FluxAssembler.Instantiate(formula, jit: asset.UseJit)` 传入用户选择。

## 测试覆盖限制

`DisableJit()` 方法在单元测试中**不应被调用**。它是全局不可逆开关，一旦调用会污染整个测试套件的 JIT 覆盖率测量。JIT 降级路径的正确性由以下间接保证：

- IL2CPP/WebGL 平台的集成测试（不在 CI 中运行）
- `JitConsistencyTests` 验证 JIT 与解释器的语义等价性（同进程两路径结果一致）

详见 [测试覆盖边界](../test-coverage-boundary.md)。
