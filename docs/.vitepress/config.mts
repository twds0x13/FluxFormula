import { defineConfig } from "vitepress";

export default defineConfig({
  title: "FluxFormula",
  description: "High-performance, Zero-GC linear formula pipeline for Unity.",
  base: "/FluxFormula/",
  
  themeConfig: {
    nav: [
      { text: "Home", link: "/" },
      { text: "Guide", link: "/guide/getting-started" },
      { text: "API", link: "/api/overview" },
    ],

    sidebar: [
      {
        text: "Introduction",
        items: [
          { text: "Getting Started", link: "/guide/getting-started" },
          { text: "Installation", link: "/guide/installation" },
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
});
