# FluxBlobBuilder

Blob 构建管线：扫描项目所有 FluxAsset 中的公式字节码，拼接为单一二进制 .bytes 文件。生成产物供 source generator 编译期解析及运行时 `FluxBlob.Load()` 加载。

## 签名

```csharp
public static class FluxBlobBuilder
```

## 方法

### Build

```csharp
public static int Build()
```

执行完整的 blob 构建流程：

1. 通过 `AssetDatabase.FindAssets("t:FluxAsset")` 扫描项目中所有 `FluxAsset`
2. 通过 `DualHash64` 去重（内容相同则跳过）
3. 若 `FluxConfig.Current.CompressBlob` 为 true，对每条公式调用 `FluxCompression.Compress()`
4. 按双哈希排序后拼接为单一字节数组，构建 `BlobEntry[]` 偏移表
5. 通过 `BlobFormat.WriteHeader` / `WriteEntry` 写入 .bytes 文件
6. 调用 `AssetDatabase.Refresh()` 刷新资源数据库

| 返回值 | 说明 |
|--------|------|
| `int` | 写入的公式条目数。0 表示无有效公式或项目中没有 FluxAsset |

### BuildFromMenu

```csharp
[MenuItem("FluxFormula/Build Blob", priority = 200)]
public static void BuildFromMenu()
```

Editor 菜单调用入口。调用 `Build()` 后弹出结果对话框。

### ClearFromMenu

```csharp
[MenuItem("FluxFormula/Clear Blob", priority = 201)]
public static void ClearFromMenu()
```

Editor 菜单调用入口。确认后删除 .bytes 文件和 `.meta` 文件，刷新资源数据库。

## 嵌套类型

### BuildPreprocessor

```csharp
public class BuildPreprocessor : IPreprocessBuildWithReport
```

Player Build 前自动触发 `Build()`，确保发布版本包含最新的公式 blob。

| 成员 | 值 |
|------|------|
| `callbackOrder` | `-100` |

## 使用示例

```csharp
// 手动触发构建
int count = FluxBlobBuilder.Build();
Debug.Log($"Blob built: {count} formulas");

// 通过菜单触发
// 菜单路径: FluxFormula > Build Blob

// 构建前自动触发：Player Build 时 BuildPreprocessor 自动调用 Build()
```

## 参见

- [BlobFormat](./blob-format) — .blob 二进制格式定义
- [FluxBlob](./flux-blob) — 运行时 blob 加载与卸载
- [IFluxBlobRegistry](./iflux-blob-registry) — Blob 注册表接口与 source generator
