# 示例：IL 内联运算符（EmitOp 内联）

展示 `IFluxILDefinition<TData>.EmitOp` 接口的使用方式：为特定操作码手写 IL 指令，在编译期完全消除虚调用开销。

> 此示例假设已理解 [IL 发射编译器](../technical/pipeline/il-compiler.md) 的两级内联体系。EmitOp 是 EmitOp 内联 接口，返回 `true` 表示已处理操作码；返回 `false` 则编译器自动回退 Compute 指针调用（指针 Compute 调用）。

## 操作符枚举

```csharp
public enum FloatOp : byte
{
    Const, Add, Sub, Mul, Div, Neg, Return = 255,
}
```

## 定义体声明

同时实现 `IFluxILDefinition<float>`（EmitOp 内联 内联）和 `IFluxExprDefinition<float>`（Expression 树回退 + 解释器）：

```csharp
public readonly struct FloatMathILDef : IFluxILDefinition<float>, IFluxExprDefinition<float>
```

基础方法（`GetKind`、`GetArity`、`GetPrecedence`、`GetFirstPosition`、`Compute` 等）与普通 Definition 完全相同。唯一新增的是 `EmitOp` 方法。

## EmitOp：手写 IL

`EmitOp` 收到 `ILGenerator` 和 `regArr`（`TData[]` 类型的本地变量），通过 `ldelem`/`stelem` 访问寄存器数组，完全跳过方法调用。

### Add：两条 ldelem + add + stelem

```csharp
if ((FloatOp)op == FloatOp.Add)
{
    il.Emit(OpCodes.Ldloc, regArr);          // [arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Dest); // [arr, destIdx]
    il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0); // [arr, destIdx, arr, idx0]
    il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0]]
    il.Emit(OpCodes.Ldloc, regArr);          // [arr, destIdx, arr[arg0], arr]
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1); // [arr, destIdx, arr[arg0], arr, idx1]
    il.Emit(OpCodes.Ldelem, typeof(float));  // [arr, destIdx, arr[arg0], arr[arg1]]
    il.Emit(OpCodes.Add);                    // [arr, destIdx, sum]
    il.Emit(OpCodes.Stelem, typeof(float));  // arr[destIdx] = sum; []
    return true;
}
```

**栈追踪**（单条 Add 指令的逐步变化）：

| 步骤 | IL 指令 | 操作后栈顶 | 栈深度 |
|------|---------|-----------|--------|
| 1 | `ldloc regArr` | `arr` | 1 |
| 2 | `ldc.i4 Dest` | `destIdx` | 2 |
| 3 | `ldloc regArr` | `arr` | 3 |
| 4 | `ldc.i4 Arg0` | `idx0` | 4 |
| 5 | `ldelem float` | `arr[arg0]` | 3 |
| 6 | `ldloc regArr` | `arr` | 4 |
| 7 | `ldc.i4 Arg1` | `idx1` | 5 |
| 8 | `ldelem float` | `arr[arg1]` | 4 |
| 9 | `add` | `sum` | 3 |
| 10 | `stelem float` | _空_ | 0 |

### Mul：与 Add 仅在操作码不同

```csharp
if ((FloatOp)op == FloatOp.Mul)
{
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Dest);
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg0);
    il.Emit(OpCodes.Ldelem, typeof(float));
    il.Emit(OpCodes.Ldloc, regArr);
    il.Emit(OpCodes.Ldc_I4, (int)inst.Arg1);
    il.Emit(OpCodes.Ldelem, typeof(float));
    il.Emit(OpCodes.Mul);                    // ※ 第 9 步差异
    il.Emit(OpCodes.Stelem, typeof(float));
    return true;
}
```

### 回退模式

对于不识别或不想内联的操作码，返回 `false`：

```csharp
// Sub、Div、Neg 等操作码不处理，自动回退 Compute 指针调用
return false;
```

编译器在 EmitOp 返回 false 后，自动生成 Compute 指针调用 的 `constrained.callvirt Compute(IntPtr)` 调用路径。

## 完整代码

`samples/ILInlineExample/FloatMathILDef.cs` 包含可直接编译运行的完整 Definition，覆盖了 Add/Mul 的 EmitOp 内联 内联和其余操作码的 Compute 指针调用 回退。

## 使用方法

```csharp
var def = default(FloatMathILDef);
var assembler = new FluxAssembler<float, FloatMathILDef>(def);

// 构造 Lexer（与普通 Definition 完全相同）
var lexer = new FluxLexer<float>(new LexerConfig<float>
{
    LiteralOper    = (byte)FloatOp.Const,
    LiteralScanner = LexerConfig<float>.CreateDefaultNumberScanner(s => float.Parse(s.TrimEnd('f'))),
    Operators =
    {
        new("+", (byte)FloatOp.Add),
        new("-", (byte)FloatOp.Sub),
        new("*", (byte)FloatOp.Mul),
        new("/", (byte)FloatOp.Div),
    },
});

var formula = assembler.Compile(lexer.Lex("1 + 2 * 3").Tokens);
float result = assembler.Instantiate(formula, jit: true).Run();
// result: 7.  Add 和 Mul 通过内联 IL 执行，Sub/Div 通过 Compute 指针调用 指针调用执行。
```

## 与 Compute 指针调用 的对比

| 维度 | Compute 指针调用（指针 Compute） | EmitOp 内联（EmitOp） |
|------|----------------------|-------------------|
| 实现成本 | 覆写 `IFluxDefinition.Compute(IntPtr)` | 实现 `IFluxILDefinition.EmitOp`，需理解 IL 栈模型 |
| 运行时开销 | 一次 `constrained.callvirt` + 方法调用 | 零调用，操作符逻辑完全内联到委托体 |
| 覆盖范围 | 所有操作码自动获得 IL 路径 | 仅选择性的操作码（Add/Mul 等高频简单操作符） |
| 回退链 | 无（Compute 指针调用 是默认） | 返回 false → 编译器自动降级 Compute 指针调用 |

## stelem 栈顺序

这是 EmitOp 内联 实现最容易出错的点。`stelem` 的 ECMA-335 语义是从栈顶依次弹出 `value`、`index`、`array`：

```
stelem 期望栈: [ ..., array, index, value ]  ← 栈顶 = value
```

因此必须先推入 `array` 和 `index`，最后计算并推入 `value`。**先推目标地址，再计算源操作数，最后 stelem**。

上述示例的 Add/Mul 实现均遵守此顺序：第 1-2 步推入 `arr` 和 `destIdx`，第 3-8 步计算操作数值，第 9 步执行运算，第 10 步写入结果。

## 注意事项

- EmitOp 中声明的 IL 指令使用 `inst.Arg0..5` 获取操作数寄存器索引、`inst.Dest` 获取目标寄存器索引。这些索引在 IL 编译器 Pass 2（寄存器计数阶段）已确认在数组范围内。
- `ILGenerator` 的求值栈深度由 CLR 自动管理；`stelem` 弹出 3 个元素、`ldelem` 弹出 2 个推入 1 个、`add` 弹出 2 个推入 1 个。
- EmitOp 内联 内联仅影响 IL 发射路径（Mono/CoreCLR）。IL2CPP 平台走 Expression 树路径，EmitOp 不会被调用。
- 内联过深（如 Sum6）的 IL 代码可读性迅速下降。建议仅对高频简单操作符使用 EmitOp 内联，其余操作符返回 false 走 Compute 指针调用。
