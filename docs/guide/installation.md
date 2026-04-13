# 安装

## Unity Package Manager

在 Unity 中打开 **Window → Package Manager**，点击 **+ → Add package from git URL**，输入：

```
https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula
```

需要 Unity 2019.3.4f1 或更高版本以支持 `?path` 查询参数。包自身最低要求 Unity 2021.3。

## 手动安装

在 `Packages/manifest.json` 中添加依赖：

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula"
  }
}
```

## unsafe 代码权限

包运行时程序集需要 `unsafe` 代码权限，已在 `FluxFormula.asmdef` 中默认开启。若项目有全局 `unsafe` 限制，无需额外操作，asmdef 设置会覆盖项目级约束。

## 本地测试

以下命令在无 Unity 环境下运行完整单元测试，覆盖编译、解释器、JIT、错误传播与 Connect 边界条件：

```bash
dotnet test standalone-tests/FluxFormula.Tests.csproj
```
