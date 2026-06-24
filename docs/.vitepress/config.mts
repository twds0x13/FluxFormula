import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";

export default withMermaid(
  defineConfig({
    title: "FluxFormula",
    description: "High-performance, Zero-GC linear formula pipeline for Unity.",
    base: "/FluxFormula/",

    locales: {
      root: {
        label: "简体中文",
        lang: "zh-CN",
        title: "FluxFormula",
        description: "高性能零GC线性公式管线 for Unity",
        themeConfig: {
          nav: [
            { text: "首页", link: "/" },
            { text: "指南", link: "/guide/getting-started" },
            { text: "API", link: "/api/overview" },
            { text: "FAQ", link: "/faq" },
          ],
          sidebar: [
            {
              text: "指南",
              items: [
                { text: "安装", link: "/guide/installation" },
                { text: "快速入门", link: "/guide/getting-started" },
                { text: "核心概念", link: "/guide/core-concepts" },
                { text: "自定义运算符", link: "/guide/writing-a-definition" },
                { text: "高级用法", link: "/guide/advanced" },
              ],
            },
            {
              text: "API 参考",
              items: [
                { text: "总览", link: "/api/overview" },
                { text: "FluxAssembler", link: "/api/flux-assembler" },
                { text: "FluxFormula", link: "/api/flux-formula" },
                { text: "FluxInstance", link: "/api/flux-instance" },
                { text: "IFluxDefinition", link: "/api/idefinition" },
                { text: "Instruction", link: "/api/instruction" },
                { text: "FluxToken", link: "/api/flux-token" },
                { text: "VffFormat", link: "/api/vff-format" },
                { text: "FormulaFormat", link: "/api/formula-format" },
                { text: "FormulaCache", link: "/api/formula-cache" },
                { text: "DualHash64", link: "/api/dualhash64" },
                { text: "FluxConfig", link: "/api/flux-config" },
                { text: "IFluxCacheProvider", link: "/api/iflux-cache-provider" },
                { text: "FluxArtifactKind", link: "/api/flux-artifact-kind" },
                { text: "IFluxBinaryBuilder", link: "/api/iflux-binary-builder" },
              ],
            },
            {
              text: "示例",
              items: [
                { text: "浮点四则运算", link: "/examples/float-math" },
                { text: "游戏伤害公式", link: "/examples/damage-formula" },
                { text: "R0 短路错误处理", link: "/examples/error-handling" },
                { text: "Vector3 运算", link: "/examples/vector3" },
                { text: "链式 Connect", link: "/examples/chain-connect" },
              ],
            },
            {
              text: "技术深度",
              items: [
                { text: "内部原理", link: "/technical/internals" },
                { text: "编译缓存管线", link: "/technical/compile-cache" },
                { text: "ChainLink 深度解析", link: "/technical/chainlink-deep-dive" },
                { text: "架构决策记录", link: "/technical/architecture-decisions" },
                { text: "源码技术分析", link: "/technical/technical-analysis" },
                { text: "管线全景", link: "/technical/pipeline/overview" },
                { text: "数据注入器", link: "/technical/pipeline/injector" },
              ],
            },
            {
              text: "更多",
              items: [
                { text: "常见问题", link: "/faq" },
              ],
            },
          ],
          socialLinks: [
            { icon: "github", link: "https://github.com/twds0x13/FluxFormula" },
          ],
          footer: {
            message: "基于 MIT 许可发布。",
            copyright: "Copyright © 2024-present twds0x13",
          },
        },
      },

      en: {
        label: "English",
        lang: "en-US",
        title: "FluxFormula",
        description: "High-performance, Zero-GC linear formula pipeline for Unity.",
        themeConfig: {
          nav: [
            { text: "Home", link: "/en/" },
            { text: "Guide", link: "/en/guide/getting-started" },
            { text: "API", link: "/en/api/overview" },
            { text: "FAQ", link: "/en/faq" },
          ],
          sidebar: [
            {
              text: "Guide",
              items: [
                { text: "Installation", link: "/en/guide/installation" },
                { text: "Getting Started", link: "/en/guide/getting-started" },
                { text: "Core Concepts", link: "/en/guide/core-concepts" },
                { text: "Writing a Definition", link: "/en/guide/writing-a-definition" },
                { text: "Advanced Usage", link: "/en/guide/advanced" },
              ],
            },
            {
              text: "API Reference",
              items: [
                { text: "Overview", link: "/en/api/overview" },
                { text: "FluxAssembler", link: "/en/api/flux-assembler" },
                { text: "FluxFormula", link: "/en/api/flux-formula" },
                { text: "FluxInstance", link: "/en/api/flux-instance" },
                { text: "IFluxDefinition", link: "/en/api/idefinition" },
                { text: "Instruction", link: "/en/api/instruction" },
                { text: "FluxToken", link: "/en/api/flux-token" },
                { text: "VffFormat", link: "/en/api/vff-format" },
                { text: "FormulaFormat", link: "/en/api/formula-format" },
                { text: "FormulaCache", link: "/en/api/formula-cache" },
                { text: "DualHash64", link: "/en/api/dualhash64" },
                { text: "FluxConfig", link: "/en/api/flux-config" },
                { text: "IFluxCacheProvider", link: "/en/api/iflux-cache-provider" },
              ],
            },
            {
              text: "Examples",
              items: [
                { text: "Float Arithmetic", link: "/en/examples/float-math" },
                { text: "Damage Formula", link: "/en/examples/damage-formula" },
                { text: "R0 Error Handling", link: "/en/examples/error-handling" },
                { text: "Vector3 Operations", link: "/en/examples/vector3" },
                { text: "Chain Connect", link: "/en/examples/chain-connect" },
              ],
            },
            {
              text: "Technical Depth",
              items: [
                { text: "Internals", link: "/en/technical/internals" },
                { text: "Compile Cache Pipeline", link: "/en/technical/compile-cache" },
                { text: "ChainLink Deep Dive", link: "/en/technical/chainlink-deep-dive" },
                { text: "Architecture Decisions", link: "/en/technical/architecture-decisions" },
                { text: "Source Technical Analysis", link: "/en/technical/technical-analysis" },
                { text: "Pipeline Overview", link: "/en/technical/pipeline/overview" },
                { text: "Data Injector", link: "/en/technical/pipeline/injector" },
              ],
            },
            {
              text: "More",
              items: [
                { text: "FAQ", link: "/en/faq" },
              ],
            },
          ],
          socialLinks: [
            { icon: "github", link: "https://github.com/twds0x13/FluxFormula" },
          ],
          footer: {
            message: "Released under the MIT License.",
            copyright: "Copyright © 2024-present twds0x13",
          },
        },
      },
    },

    themeConfig: {},
  })
);
