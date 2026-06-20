# 安装

FluxFormula 是三分包 monorepo。按需选择安装哪些包——用户只需装真正需要的模块。

## 包结构

| 包名 | 用途 | 依赖 |
|------|------|------|
| `com.twds0x13.fluxformula.core` | 纯 C# 管线引擎（零 Unity 依赖） | 无 |
| `com.twds0x13.fluxformula` | Unity 集成（ScriptableObject + Editor） | Core |
| `com.twds0x13.fluxformula.addressables` | Addressables 公式加载 | Core + FluxFormula + Unity.Addressables |

## 按场景选择

| 场景 | 安装的包 |
|------|----------|
| 独立 .NET / Godot / 服务器 | 仅 Core |
| Unity 基础使用 | Core + FluxFormula |
| Unity + Addressables 加载 | Core + FluxFormula + Addressables |

## Unity Package Manager

打开 **Window → Package Manager**，点击 **+ → Add package from git URL**，逐条输入：

```
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables
```

安装顺序无要求。UPM 自动解析依赖——例如添加 `fluxformula` 时若 `core` 不存在会报错，先装 `core` 即可。

需要 Unity 2019.3.4f1 或更高版本以支持 `?path` 查询参数。包自身最低要求 Unity 2021.3。

## 手动安装

在 `Packages/manifest.json` 中添加依赖：

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula",
    "com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables"
  }
}
```

去掉不需要的包行即可。`core` 是 `fluxformula` 和 `addressables` 的前置依赖，安装后者时需同时添加 `core`。

## unsafe 代码权限

包运行时程序集已默认开启 `allowUnsafeCode`，无需额外操作。asmdef 设置会覆盖项目级 `unsafe` 约束。

## 依赖项

Core 包依赖 `com.unity.collections` (≥1.2.4)，为 Unity 2021.3 提供 `System.Memory` 支持。Unity 2022.3 及以上版本内建此依赖，不会重复安装。

## 本地测试

在无 Unity 环境下运行完整单元测试（149 个测试用例）：

```bash
dotnet test tests/FluxFormula.Core.Tests/FluxFormula.Tests.csproj
```

需要 .NET SDK 8.0+。
