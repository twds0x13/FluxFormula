# FluxToken

词法单元，中缀表达式的原子构件。

## 签名

```csharp
public struct FluxToken<TData, TOper>
    where TData : unmanaged
    where TOper : unmanaged, Enum
```

## 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `Oper` | `TOper` | 操作符枚举值 |
| `Data` | `TData` | 数据值（仅对 Immediate 类 Token 有效） |

## 用法

```csharp
// 构造立即数 Token
new FluxToken<float, FloatOp> { Oper = FloatOp.Const, Data = 3.14f };

// 构造运算符 Token
new FluxToken<float, FloatOp> { Oper = FloatOp.Add };
// Data 为 default(float) = 0f，无实际意义
```

## 编码约定

- **Immediate Token**：`Oper` 对应的 `GetKind()` 返回 `OpType.Immediate`，`Data` 携带具体值
- **Operator Token**：`Oper` 对应的 `GetKind()` 返回 `OpType.Instruction`，`Data` 忽略
- **括号 Token**：`Oper` 对应的 `GetPair()` 返回 `PairRole = Left/Right`
