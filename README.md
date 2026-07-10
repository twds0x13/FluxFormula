<p align="center">
  <img src="logo.png" alt="FluxFormula" width="160" />
</p>

# FluxFormula

[English](./README.en.md)

[![CI](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml/badge.svg)](https://github.com/twds0x13/FluxFormula/actions/workflows/test.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-green)](https://twds0x13.github.io/FluxFormula/)
[![Coverage](https://img.shields.io/badge/coverage-97.3%25-brightgreen)](./docs/technical/test-coverage-boundary.md)

Unity 高性能线性公式编译管线（执行期零 GC，编译期一次性分配）。自定义运算符集，中缀表达式编译为紧凑字节码，解释器或 JIT 双后端执行

## 特性

- **执行期零 GC**：ref struct、stackalloc 与 unsafe 指针操作，运行时零堆分配。编译期仅一次 Instruction[] 分配加字面量字符串解析，后续执行走纯栈
- **双后端执行**：解释器全平台兼容（含 IL2CPP/AOT），JIT 基于 LINQ Expression Tree 编译为委托，不支持 JIT 的平台自动降级
- **自定义指令集**：实现 `IFluxExprDefinition<TData>` 接口定义领域运算符，一次编写同时获得解释器与 JIT 两条路径
- **紧凑字节码**：`Instruction` 为 8 字节定长结构体，显式内存布局。256 虚拟寄存器，最大 arity 6，立即数内联至指令缓冲区
- **手写词法分析**：基于 `ReadOnlySpan<char>` 的零分配扫描器，无正则。支持可配置运算符、括号、变量模式与隐式运算符
- **三态求值器**：热路径解释器全速执行，柯里化求值器渐进式绑定变量（支持分叉），单步调试器逐指令排查。三种模式共享同一寄存器机核心

## 为什么用 FluxFormula

如果你的游戏有大量公式计算，比如伤害公式、技能修正、概率判据，每次计算都走字符串解析会拖慢帧率。FluxFormula 让你用自然的中缀表达式写公式，编译为字节码后执行期零分配。你写的运算符定义同时获得解释器和 JIT 两条路径，JIT 模式下单次求值只需几纳秒，不支持 JIT 的平台自动降级。

## 性能

以下数据来自 BenchmarkDotNet（Intel Core Ultra 9 275HX，.NET 9，ShortRun）：

| 阶段 | 操作 | 耗时 | 分配 |
|------|------|------|------|
| Lexer | 简单表达式 | ~103 ns | 392 B |
| Lexer | 复杂表达式 | ~422 ns | 1080 B |
| Compile | 简单表达式 | ~34 ns | 112 B |
| Compile | 复杂表达式 | ~119 ns | 496 B |
| 解释器 | 简单公式求值 | ~27 ns | **0 B** |
| 解释器 | 复杂公式求值 | ~42 ns | **0 B** |
| JIT | 简单公式求值 | ~2 ns | **0 B** |
| JIT | 复杂公式求值 | ~4 ns | **0 B** |

编译一次性开销 ~30–110 ns + 数百字节分配。执行期零分配，JIT 比解释器快约 15 倍。
## 包结构

此仓库为 monorepo，包含五个独立包：

| 包 | 用途 | 依赖 |
|----|------|------|
| `fluxformula.core` | 纯 C# 公式管线（零 Unity 依赖） | 无 |
| `fluxformula` | Unity 集成（ScriptableObject 容器 + 编辑器） | Core |
| `fluxformula.burst` | Burst/Jobs 求值器：多线程零分配并发执行 | Core + Burst + Collections |
| `fluxformula.addressables` | 可选 Addressables 加载支持 | Core + FluxFormula + Addressables |
| `fluxformula.addressables.unitask` | UniTask 异步加载扩展（项目已用 UniTask 时安装） | Addressables |

## 兼容性

CI 在以下环境运行完整测试套件：

| 环境 | 测试版本 |
|------|---------|
| Unity | 2021.3 LTS · 2022.3 LTS · 6000.0 |
| .NET SDK | 8.0 · 9.0 |

Core 包（`fluxformula.core`）目标框架为 netstandard2.1，兼容 .NET Core 3.0+ 及所有现代 .NET 运行时。Unity 端支持 Mono 与 IL2CPP 双脚本后端。

## 安装

**最小安装（仅 Core 运行时）：**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core"
  }
}
```

**Unity 用户（推荐）：**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula"
  }
}
```

**需要 Addressables 加载时，追加第三个包：**

```json
"com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables"
```

**全量安装（所有包）：**

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula",
    "com.twds0x13.fluxformula.burst": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.burst",
    "com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables",
    "com.twds0x13.fluxformula.addressables.unitask": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables.unitask"
  }
}
```

最低 Unity 版本：2021.3

## 快速开始

用 FluxFormula 构建 Noita 式法术修正系统。每张卡有伤害修正和抽牌提供，串联为法术链后一次施法跑完整条链：

```csharp
// 三张法术卡：+10 / +7 / +5 伤害，无额外抽牌
var card1 = runner.Compile(lexer.Lex("[prev] + 10|idx:0"));
var mod2  = runner.Compile(lexer.Lex("[prev] + 7|idx:1")).ToModifier();
var mod3  = runner.Compile(lexer.Lex("[prev] + 5|idx:2")).ToModifier();

// 串成法术链
var chain = card1.Connect(mod2).Connect(mod3);

// 执行：7 抽，3 张卡，法术回绕至全部卡消费完毕
SpellContext state = new(0, 7);
do {
    state = runner.Instantiate(chain).Set("prev", state).Run();
} while (/* 掩码未满 */);
```

[CardDraw 完整可运行代码](https://twds0x13.github.io/FluxFormula/examples/card-draw) · [更多示例](https://twds0x13.github.io/FluxFormula/examples/float-math)

如果你只想看最简 API 形状：

```csharp
var config = new LexerConfig<float>();          // 运算符与括号配置
var lexer  = new FluxLexer<float>(config);      // 词法分析器
var def    = new MathDef();                     // 运算符定义（+ - * /）
var runner = new FluxAssembler<float, MathDef>(def); // 编译器 + 实例工厂

var lexResult = lexer.Lex("([atk] * 2 + [bonus]) / 100");
float result = runner.Instantiate(runner.Compile(lexResult))
    .Set("atk", 150f).Set("bonus", 25f).Run();
// result = 3.25
```

编译与执行分离（编译一次，多次复用）：

```csharp
var formula = runner.Compile(lexResult);        // 编译（可缓存）
var inst    = runner.Instantiate(formula);       // 实例化（轻量，可反复创建）
float r     = inst.Set("atk", 100f).Set("bonus", 20f).Run();
```

完整教程见 [快速入门指南](https://twds0x13.github.io/FluxFormula/guide/getting-started)。

## 文档

详细的 API 参考、进阶配置与使用指南，请访问：<https://twds0x13.github.io/FluxFormula/>

## 许可证

MIT License © 2026 twds0x13
