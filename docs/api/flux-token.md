# FluxToken

词法单元，中缀表达式的原子构件。

## 签名

```csharp
public struct FluxToken<TData>
    where TData : unmanaged
```

v3.0.0 移除了 `TOper` 泛型参数：操作符枚举是定义体的内部细节，Token 仅存储 `byte` 操作码。

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `Oper` | `byte` | 操作码（由定义体的 `ResolveToken()` 产出） |
| `Data` | `TData` | 数据值（仅对 Immediate 类 Token 有效） |

`ToString()` 重写：输出 `Oper: {Oper}, Data: {Data}` 格式，调试用。

## 使用示例

```csharp
// 构造立即数 Token：Oper 由定义体定义
new FluxToken<float> { Oper = (byte)MathOp.Const, Data = 3.14f };

// 构造运算符 Token
new FluxToken<float> { Oper = (byte)MathOp.Add };
// Data 为 default(float) = 0f，无实际意义
```

## 编码约定

- **Immediate Token**：`Oper` 对应的 `GetKind()` 返回 `OpType.Immediate`，`Data` 携带具体值
- **Operator Token**：`Oper` 对应的 `GetKind()` 返回 `OpType.Instruction`，`Data` 忽略
- **括号 Token**：`Oper` 对应的 `GetPair()` 返回 `PairRole = Left/Right`

## 参见

- [IDefinition](./idefinition) — Token 的 Oper 字节码由定义体产出
- [FluxAssembler](./flux-assembler) — Token 数组的编译入口
