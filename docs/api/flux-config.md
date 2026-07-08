# FluxConfig

项目级全局配置的单一注入点。所有硬编码常量集中于此，通过 `Current` 静态属性全局生效。

## 签名

```csharp
public class FluxConfig
```

## 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Default` | `FluxConfig` | 出厂默认配置（FormulaCacheCapacity=2048, MergeThreshold=8） |
| `Current` | `FluxConfig` | 当前生效的全局配置。未显式设置时返回 `Default` |

### Set

```csharp
public static void Set(FluxConfig config)
```

替换当前配置。等价于 `Current = config`。传入 `null` 抛 `ArgumentNullException`。

## 配置项

| 属性 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `FormulaCacheCapacity` | `int` | `2048` | `FormulaCache` 哈希表槽位数。增大可减少碰撞，但增加内存 |
| `MergeThreshold` | `int` | `8` | 链式公式合并阈值：链长超过此值时 `ToAtomic()` 合并为原子公式 |
| `BlobFilePath` | `string` | `null` | Blob 二进制文件路径。null 使用 `StreamingAssets/flux.bytes` |
| `CompressBlob` | `bool` | `false` | 是否对 blob 中每条公式启用 Brotli 压缩。运行时由 `FluxBlob.Load()` 自动解压 |
| `DiskCacheDirectory` | `string` | `null` | 磁盘缓存目录。null 使用 `Application.persistentDataPath` |

所有配置项均使用 `init` 访问器，创建后不可修改。

## 使用示例

```csharp
// 应用启动时
FluxConfig.Set(new FluxConfig
{
    FormulaCacheCapacity = 4096,
    MergeThreshold       = 16,
});

// 读取
int cap = FluxConfig.Current.FormulaCacheCapacity; // 4096

// 重置为默认
FluxConfig.Set(FluxConfig.Default);
```

## Unity 集成

`FluxConfigAsset`（`ScriptableObject`）在 `RuntimeInitializeOnLoadMethod` 时自动从 `Resources/FluxConfig.asset` 加载并调用 `Apply()`。创建方式：右键 → Create → FluxFormula → Config。

## 参见

- [FormulaCache](./formula-cache) — 缓存实现，构造时读取 `FormulaCacheCapacity`
- [FluxFormula](./flux-formula) — `ToAtomic()` 使用 `MergeThreshold`
