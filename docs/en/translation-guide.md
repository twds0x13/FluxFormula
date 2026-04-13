# Documentation Translation Guide

The FluxFormula documentation site maintains two locales: Simplified Chinese (`zh-CN`, default) and English (`en`). Contributions to improve translation quality are welcome.

## Scope

```
docs/
├── index.md                 ← Chinese source
├── faq.md
├── guide/
├── api/
├── examples/
├── technical/
└── en/                      ← English translations (1:1 correspondence with Chinese sources)
    ├── index.md
    ├── faq.md
    ├── guide/
    ├── api/
    ├── examples/
    └── technical/
```

Chinese files are the source. `docs/en/` mirrors the same structure with English translations. Translation work involves rendering Chinese source paragraphs into English and updating the corresponding files under `docs/en/`.

## Conventions

### Principles

- **Accuracy first**, fluency second
- Keep terminology consistent: `立即数 → immediate`, `字节码 → bytecode`, `短路返回 → early exit`, `寄存器 → register`
- Do not translate code blocks — comments within code may be translated, but variable names and type names stay as-is
- Link paths need `/en/` prefix: Chinese source writes `/guide/getting-started`, English translation writes `/en/guide/getting-started`

### Style

- Engineering documentation tone: state facts, no exclamation marks, no subjective modifiers
- Natural English takes priority over word-for-word rendering — but do not deviate from the Chinese source's technical meaning
- Reference existing translated pages for tone and terminology choices

## Process

1. State your intent in [Issues](https://github.com/twds0x13/FluxFormula/issues) (which file or section) to avoid duplicate effort
2. Fork the repo, edit files under `docs/en/`
3. Preview locally:

```bash
cd docs
npx vitepress dev
```

4. Open a PR describing which pages were modified
5. Chinese and English page counts must stay in sync — when adding a new Chinese page, provide an English translation as well (a skeletal translation is acceptable; it can be iterated on later)

## Translated Pages

See the Chinese version of this guide for the full status table.

## Questions

If you are unsure about technical terminology during translation:
- Reference existing English translation pages for precedent
- Ask in GitHub Discussions
