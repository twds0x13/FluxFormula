# FluxArtifactKind

二进制产物类型枚举。映射到文件扩展名：`.ff` 和 `.vff`。

## 签名

```csharp
public enum FluxArtifactKind : byte
{
    Formula = 0,  // .ff — 公式字节码
    Virtual = 1,  // .vff — 虚拟公式引用
}
```

## 值

| 值 | 名称 | 扩展名 | 说明 |
|------|------|------|------|
| `0` | `Formula` | `.ff` | 公式字节码：`FluxFormula<TData, TDef>.ToBytes()` 的产物 |
| `1` | `Virtual` | `.vff` | 虚拟公式引用：`VffFormat.ToBytes()` 的产物 |

## 使用

用于 `IFluxFileFormatter.Save()` 的 `kind` 参数，使外部保存器能区分文件类型：

```csharp
byte[] data = formula.ToBytes();
builder.Save(data, FluxArtifactKind.Formula, "Damage.ff");

byte[] vffData = VffFormat.ToBytes(links, overrides);
builder.Save(vffData, FluxArtifactKind.Virtual, "DamageChain.vff");
```

## 参见

- [IFluxFileFormatter](./iflux-file-formatter) — 持久化接口
- [FluxFormula](./flux-formula) — 公式字节码序列化
- [VffFormat](./vff-format) — VFF 格式序列化与解析
