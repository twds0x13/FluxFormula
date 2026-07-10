import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";

export default withMermaid(
  defineConfig({
    title: "FluxFormula",
    description: "High-performance, Zero-GC linear formula pipeline for Unity.",
    base: "/FluxFormula/",

    head: [
      ["link", { rel: "icon", type: "image/png", href: "/FluxFormula/favicon.png" }],
      ["meta", { property: "og:image", content: "https://twds0x13.github.io/FluxFormula/logo.png" }],
      ["meta", { property: "og:image:width", content: "1254" }],
      ["meta", { property: "og:image:height", content: "1254" }],
      ["meta", { name: "twitter:card", content: "summary_large_image" }],
      ["meta", { name: "twitter:image", content: "https://twds0x13.github.io/FluxFormula/logo.png" }],
      ["style", {}, `.VPNavBarTitle .VPImage { height: 36px; border-radius: 50%; }`],
      ["style", {}, `.VPHero .image-src { max-width: 250px; } .VPHero .image-container { position: relative; } .VPHero .image-container::before { content: ''; position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); width: 260px; height: 260px; background: linear-gradient(to right, rgba(0, 194, 250, 0.8), rgba(0, 97, 240, 0.8)); border-radius: 50%; filter: blur(80px); z-index: -1; }`],
      ["style", {}, `.VPHero .name { background: linear-gradient(to right, rgb(0, 194, 250), rgb(0, 97, 240)); -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent; }`],
    ],

    mermaid: {
      htmlLabels: true,
      themeCSS: `
        .label foreignObject { overflow: visible !important; }
        .nodeLabel foreignObject { overflow: visible !important; }
        .edgeLabel foreignObject { overflow: visible !important; }
        .label div, .nodeLabel div, .edgeLabel div { padding-bottom: 5px; }
      `,
      themeVariables: {
        fontFamily: '"Microsoft YaHei", "Noto Sans SC", "PingFang SC", sans-serif',
      },
    },

    locales: {
      root: {
        label: "简体中文",
        lang: "zh-CN",
        title: "FluxFormula",
        description: "高性能零GC线性公式管线 for Unity",
        themeConfig: {
          logo: "/logo.png",
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
                { text: "字面量扫描器", link: "/guide/literal-scanner" },
                { text: "分步求值器", link: "/guide/curry-evaluator" },
                { text: "单步调试器", link: "/guide/step-debugger" },
                { text: "高级用法", link: "/guide/advanced" },
                { text: "Blob 注册表", link: "/guide/blob-registry" },
              ],
            },
            {
              text: "参考",
              items: [
                { text: "术语速查", link: "/reference/glossary" },
              ],
            },
            {
              text: "API 参考",
              items: [
                {
                  text: "核心管线",
                  collapsed: false,
                  items: [
                    { text: "总览", link: "/api/overview" },
                    { text: "FluxAssembler", link: "/api/flux-assembler" },
                    { text: "FluxFormula", link: "/api/flux-formula" },
                    { text: "FluxChain", link: "/api/flux-chain" },
                    { text: "FluxInstance", link: "/api/flux-instance" },
                  ],
                },
                {
                  text: "词法与求值",
                  collapsed: false,
                  items: [
                    { text: "FluxLexer", link: "/api/flux-lexer" },
                    { text: "FluxCurryEvaluator", link: "/api/flux-curry-evaluator" },
                    { text: "FluxStepEvaluator", link: "/api/flux-step-evaluator" },
                  ],
                },
                {
                  text: "运算符定义",
                  collapsed: false,
                  items: [
                    { text: "IFluxDefinition", link: "/api/idefinition" },
                    { text: "Instruction", link: "/api/instruction" },
                    { text: "FluxToken", link: "/api/flux-token" },
                  ],
                },
                {
                  text: "缓存与配置",
                  collapsed: false,
                  items: [
                    { text: "FormulaCache", link: "/api/formula-cache" },
                    { text: "DualHash64", link: "/api/dualhash64" },
                    { text: "FluxConfig", link: "/api/flux-config" },
                    { text: "IFluxCacheProvider", link: "/api/iflux-cache-provider" },
                  ],
                },
                {
                  text: "持久化与格式",
                  collapsed: false,
                  items: [
                    { text: "VffFormat", link: "/api/vff-format" },
                    { text: "FluxArtifactKind", link: "/api/flux-artifact-kind" },
                    { text: "IFluxFileFormatter", link: "/api/iflux-file-formatter" },
                  ],
                },
                {
                  text: "Blob 与 Mod",
                  collapsed: false,
                  items: [
                    { text: "BlobFormat", link: "/api/blob-format" },
                    { text: "BlobEntry", link: "/api/blob-entry" },
                    { text: "IFluxBlobRegistry", link: "/api/iflux-blob-registry" },
                    { text: "FluxBlob", link: "/api/flux-blob" },
                  ],
                },
              ],
            },
            {
              text: "示例",
              items: [
                {
                  text: "基础示例",
                  collapsed: false,
                  items: [
                    { text: "浮点四则运算", link: "/examples/float-math" },
                    { text: "元素伤害公式", link: "/examples/elem-math" },
                    { text: "Vector3 运算", link: "/examples/vector3" },
                    { text: "高级浮点运算", link: "/examples/adv-math" },
                    { text: "法术上下文", link: "/examples/card-draw" },
                    { text: "链式 Connect", link: "/examples/chain-connect" },
                  ],
                },
                {
                  text: "进阶示例",
                  collapsed: false,
                  items: [
                    { text: "IL 内联运算符", link: "/examples/il-inline" },
                    { text: "Burst Jobs 求值", link: "/examples/burst-jobs" },
                    { text: "VFF 持久化与参数覆写", link: "/examples/vff-persistence" },
                    { text: "Addressables 加载", link: "/examples/addressables-load" },
                    { text: "UniTask 加载", link: "/examples/unitask-load" },
                  ],
                },
              ],
            },
            {
              text: "技术深度",
              items: [
                {
                  text: "编译器管线",
                  collapsed: false,
                  items: [
                    { text: "管线全景", link: "/technical/pipeline/overview" },
                    { text: "词法分析器", link: "/technical/pipeline/lexer" },
                    { text: "调车场编译器", link: "/technical/pipeline/compiler" },
                    { text: "8 字节指令布局", link: "/technical/pipeline/instruction" },
                    { text: "解释器执行循环", link: "/technical/pipeline/evaluator" },
                    { text: "IL 发射编译器", link: "/technical/pipeline/il-compiler" },
                    { text: "表达式树编译", link: "/technical/pipeline/jit" },
                    { text: "平台适配", link: "/technical/pipeline/platform" },
                    { text: "数据注入器", link: "/technical/pipeline/injector" },
                  ],
                },
                {
                  text: "架构与设计",
                  collapsed: false,
                  items: [
                    { text: "内部原理", link: "/technical/internals" },
                    { text: "架构决策记录", link: "/technical/architecture-decisions" },
                    { text: "源码技术分析", link: "/technical/technical-analysis" },
                    { text: "测试覆盖边界", link: "/technical/test-coverage-boundary" },
                    { text: "编译缓存管线", link: "/technical/compile-cache" },
                    { text: "ChainLink 深度解析", link: "/technical/chainlink-deep-dive" },
                  ],
                },
              ],
            },
            {
              text: "更多",
              items: [
                { text: "常见问题", link: "/faq" },
                { text: "多语言协作", link: "/multilingual-collaboration" },
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
          logo: "/logo.png",
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
                { text: "Literal Scanner", link: "/en/guide/literal-scanner" },
                { text: "Progressive Binding", link: "/en/guide/curry-evaluator" },
                { text: "Step Debugger", link: "/en/guide/step-debugger" },
                { text: "Advanced Usage", link: "/en/guide/advanced" },
                { text: "Blob Registry", link: "/en/guide/blob-registry" },
              ],
            },
            {
              text: "Reference",
              items: [
                { text: "Glossary", link: "/en/reference/glossary" },
              ],
            },
            {
              text: "API Reference",
              items: [
                {
                  text: "Core Pipeline",
                  collapsed: false,
                  items: [
                    { text: "Overview", link: "/en/api/overview" },
                    { text: "FluxAssembler", link: "/en/api/flux-assembler" },
                    { text: "FluxFormula", link: "/en/api/flux-formula" },
                    { text: "FluxChain", link: "/en/api/flux-chain" },
                    { text: "FluxInstance", link: "/en/api/flux-instance" },
                  ],
                },
                {
                  text: "Lexing & Evaluation",
                  collapsed: false,
                  items: [
                    { text: "FluxLexer", link: "/en/api/flux-lexer" },
                    { text: "FluxCurryEvaluator", link: "/en/api/flux-curry-evaluator" },
                    { text: "FluxStepEvaluator", link: "/en/api/flux-step-evaluator" },
                  ],
                },
                {
                  text: "Operator Definition",
                  collapsed: false,
                  items: [
                    { text: "IFluxDefinition", link: "/en/api/idefinition" },
                    { text: "Instruction", link: "/en/api/instruction" },
                    { text: "FluxToken", link: "/en/api/flux-token" },
                  ],
                },
                {
                  text: "Caching & Configuration",
                  collapsed: false,
                  items: [
                    { text: "FormulaCache", link: "/en/api/formula-cache" },
                    { text: "DualHash64", link: "/en/api/dualhash64" },
                    { text: "FluxConfig", link: "/en/api/flux-config" },
                    { text: "IFluxCacheProvider", link: "/en/api/iflux-cache-provider" },
                  ],
                },
                {
                  text: "Persistence & Format",
                  collapsed: false,
                  items: [
                    { text: "VffFormat", link: "/en/api/vff-format" },
                    { text: "FluxArtifactKind", link: "/en/api/flux-artifact-kind" },
                    { text: "IFluxFileFormatter", link: "/en/api/iflux-file-formatter" },
                  ],
                },
                {
                  text: "Blob & Mod",
                  collapsed: false,
                  items: [
                    { text: "BlobFormat", link: "/en/api/blob-format" },
                    { text: "BlobEntry", link: "/en/api/blob-entry" },
                    { text: "IFluxBlobRegistry", link: "/en/api/iflux-blob-registry" },
                    { text: "FluxBlob", link: "/en/api/flux-blob" },
                  ],
                },
              ],
            },
            {
              text: "Examples",
              items: [
                {
                  text: "Basics",
                  collapsed: false,
                  items: [
                    { text: "Float Arithmetic", link: "/en/examples/float-math" },
                    { text: "Elemental Damage Formula", link: "/en/examples/elem-math" },
                    { text: "Vector3 Operations", link: "/en/examples/vector3" },
                    { text: "Advanced Float Arithmetic", link: "/en/examples/adv-math" },
                    { text: "Spell Context", link: "/en/examples/card-draw" },
                    { text: "Chain Connect", link: "/en/examples/chain-connect" },
                  ],
                },
                {
                  text: "Advanced",
                  collapsed: false,
                  items: [
                    { text: "IL Inline Operators", link: "/en/examples/il-inline" },
                    { text: "Burst Jobs", link: "/en/examples/burst-jobs" },
                    { text: "VFF Persistence & Overrides", link: "/en/examples/vff-persistence" },
                    { text: "Addressables Load", link: "/en/examples/addressables-load" },
                    { text: "UniTask Load", link: "/en/examples/unitask-load" },
                  ],
                },
              ],
            },
            {
              text: "Technical Depth",
              items: [
                {
                  text: "Compiler Pipeline",
                  collapsed: false,
                  items: [
                    { text: "Pipeline Overview", link: "/en/technical/pipeline/overview" },
                    { text: "Lexer", link: "/en/technical/pipeline/lexer" },
                    { text: "Shunting-Yard Compiler", link: "/en/technical/pipeline/compiler" },
                    { text: "Instruction Layout", link: "/en/technical/pipeline/instruction" },
                    { text: "Interpreter Execution Loop", link: "/en/technical/pipeline/evaluator" },
                    { text: "IL Compiler", link: "/en/technical/pipeline/il-compiler" },
                    { text: "Expression Tree Compilation", link: "/en/technical/pipeline/jit" },
                    { text: "Platform", link: "/en/technical/pipeline/platform" },
                    { text: "Data Injector", link: "/en/technical/pipeline/injector" },
                  ],
                },
                {
                  text: "Architecture & Design",
                  collapsed: false,
                  items: [
                    { text: "Internals", link: "/en/technical/internals" },
                    { text: "Architecture Decisions", link: "/en/technical/architecture-decisions" },
                    { text: "Source Technical Analysis", link: "/en/technical/technical-analysis" },
                    { text: "Test Coverage Boundary", link: "/en/technical/test-coverage-boundary" },
                    { text: "Compile Cache Pipeline", link: "/en/technical/compile-cache" },
                    { text: "ChainLink Deep Dive", link: "/en/technical/chainlink-deep-dive" },
                  ],
                },
              ],
            },
            {
              text: "More",
              items: [
                { text: "FAQ", link: "/en/faq" },
                { text: "Multilingual Collaboration", link: "/en/multilingual-collaboration" },
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
