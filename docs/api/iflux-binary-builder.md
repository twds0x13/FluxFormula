# IFluxBinaryBuilder

最小持久化契约。Core 不执行文件 I/O——消费者注入外部保存器实现。

## 签名

```csharp
public interface IFluxBinaryBuilder
{
    void Save(byte[] data, FluxArtifactKind kind, string path);
}
```

## 设计原理

接口故意非泛型——调用方在传入 `byte[]` 之前已完成序列化（`FluxFormula.ToBytes()` 或 `VffFormat.ToBytes()`），接口只需关心"把字节存到哪"。

Core 运行时不含此接口的实现。依赖方自行提供保存器：

- **独立 .NET 应用**：`System.IO.File.WriteAllBytes`
- **Unity Editor**：`AssetDatabase.CreateAsset` / `File.WriteAllBytes`
- **测试环境**：内存流或临时文件

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

## 使用示例

```csharp
// 基础 System.IO 实现
public class FileBinaryBuilder : IFluxBinaryBuilder
{
    public void Save(byte[] data, FluxArtifactKind kind, string path)
    {
        string ext = kind == FluxArtifactKind.Formula ? ".ff" : ".vff";
        string fullPath = path.EndsWith(ext) ? path : path + ext;
        System.IO.File.WriteAllBytes(fullPath, data);
    }
}

var builder = new FileBinaryBuilder();

// 保存单条公式
builder.Save(formula.ToBytes(), FluxArtifactKind.Formula, "Damage");

// 保存链式引用
var links = chain.GetChainLinks().ToArray();
builder.Save(VffFormat.ToBytes<float>(links, overrides),
    FluxArtifactKind.Virtual, "DamageChain");
```

## 参见

- [FluxArtifactKind](./flux-artifact-kind) — 产物类型枚举
- [FluxFormula](./flux-formula) — 公式字节码序列化
- [VffFormat](./vff-format) — VFF 格式序列化与解析
