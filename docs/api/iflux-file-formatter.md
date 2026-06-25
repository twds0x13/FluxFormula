# IFluxFileFormatter

最小文件持久化契约。Core 提供读写方法，消费者注入外部实现或直接使用内置的 `FileFluxFileFormatter`。

## 签名

```csharp
public interface IFluxFileFormatter
{
    void Save(byte[] data, FluxArtifactKind kind, string path);
    byte[] Load(string path, out FluxArtifactKind kind);
}
```

## 设计原理

接口故意非泛型：调用方在传入 `byte[]` 之前已完成序列化（`FluxFormula.ToBytes()` 或 `VffFormat.ToBytes()`），收到 `byte[]` 后自行反序列化（`FluxFormula.FromBytes()` 或 `VffFormat.FromBytes()`）。接口只需关心"字节存到哪"和"从哪读字节"。

## 方法

### Save

```csharp
void Save(byte[] data, FluxArtifactKind kind, string path)
```

将二进制产物持久化到指定路径。

| 参数 | 类型 | 说明 |
|------|------|------|
| `data` | `byte[]` | 序列化后的字节码 |
| `kind` | `FluxArtifactKind` | 产物类型（`.ff` 或 `.vff`） |
| `path` | `string` | 目标路径（文件系统路径或 Unity 项目相对路径） |

### Load

```csharp
byte[] Load(string path, out FluxArtifactKind kind)
```

从指定路径加载二进制产物的字节数据。

| 参数 | 类型 | 说明 |
|------|------|------|
| `path` | `string` | 源路径 |
| `kind` | `FluxArtifactKind` | 产物类型（通过 magic bytes 自动检测） |

## 内置实现：FileFluxFileFormatter

```csharp
public sealed class FileFluxFileFormatter : IFluxFileFormatter
```

基于 `System.IO.File` 的默认实现。自动根据 `FluxArtifactKind` 附加 `.ff` / `.vff` 扩展名；加载时通过 `VffFormat.IsVff()` 检测 magic bytes 自动推断类型。

## 使用示例

```csharp
var formatter = new FileFluxFileFormatter();

// 保存
formatter.Save(formula.ToBytes(), FluxArtifactKind.Formula, "Damage");
// → Damage.ff

// 加载
byte[] data = formatter.Load("Damage", out var kind);
var loaded = FluxFormula<float, MathDef>.FromBytes(data);

// VFF 往返
var links = chain.GetChainLinks().ToArray();
formatter.Save(VffFormat.ToBytes<float>(links, overrides),
    FluxArtifactKind.Virtual, "DamageChain");
// → DamageChain.vff

byte[] vffData = formatter.Load("DamageChain", out var vffKind);
var result = VffFormat.FromBytes<float, MathDef>(vffData);
```

## 参见

- [FluxArtifactKind](./flux-artifact-kind) — 产物类型枚举
- [FluxFormula](./flux-formula) — 公式字节码序列化
- [VffFormat](./vff-format) — VFF 格式序列化与解析
