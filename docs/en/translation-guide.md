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

| Chinese | English | Status |
|------|------|:---:|
| `index.md` | `en/index.md` | ✅ |
| `faq.md` | `en/faq.md` | ✅ |
| `guide/installation.md` | `en/guide/installation.md` | ✅ |
| `guide/getting-started.md` | `en/guide/getting-started.md` | ✅ |
| `guide/core-concepts.md` | `en/guide/core-concepts.md` | ✅ |
| `guide/writing-a-definition.md` | `en/guide/writing-a-definition.md` | ✅ |
| `guide/literal-scanner.md` | `en/guide/literal-scanner.md` | ✅ |
| `guide/advanced.md` | `en/guide/advanced.md` | ✅ |
| `api/overview.md` | `en/api/overview.md` | ✅ |
| `api/flux-assembler.md` | `en/api/flux-assembler.md` | ✅ |
| `api/flux-formula.md` | `en/api/flux-formula.md` | ✅ |
| `api/flux-chain.md` | `en/api/flux-chain.md` | ✅ |
| `api/flux-instance.md` | `en/api/flux-instance.md` | ✅ |
| `api/idefinition.md` | `en/api/idefinition.md` | ✅ |
| `api/instruction.md` | `en/api/instruction.md` | ✅ |
| `api/flux-token.md` | `en/api/flux-token.md` | ✅ |
| `api/flux-config.md` | `en/api/flux-config.md` | ✅ |
| `api/flux-artifact-kind.md` | `en/api/flux-artifact-kind.md` | ✅ |
| `api/formula-cache.md` | `en/api/formula-cache.md` | ✅ |
| `api/iflux-cache-provider.md` | `en/api/iflux-cache-provider.md` | ✅ |
| `api/iflux-file-formatter.md` | `en/api/iflux-file-formatter.md` | ✅ |
| `api/vff-format.md` | `en/api/vff-format.md` | ✅ |
| `api/dualhash64.md` | `en/api/dualhash64.md` | ✅ |
| `examples/float-math.md` | `en/examples/float-math.md` | ✅ |
| `examples/custom-literal.md` | `en/examples/custom-literal.md` | ✅ |

| `examples/card-draw.md` | `en/examples/card-draw.md` | ✅ |
| `examples/vector3.md` | `en/examples/vector3.md` | ✅ |
| `examples/chain-connect.md` | `en/examples/chain-connect.md` | ✅ |
| `examples/burst-jobs.md` | `en/examples/burst-jobs.md` | ✅ |
| `examples/addressables-load.md` | `en/examples/addressables-load.md` | ✅ |
| `examples/unitask-load.md` | `en/examples/unitask-load.md` | ✅ |
| `examples/vff-persistence.md` | `en/examples/vff-persistence.md` | ✅ |
| `technical/internals.md` | `en/technical/internals.md` | ✅ |
| `technical/compile-cache.md` | `en/technical/compile-cache.md` | ✅ |
| `technical/chainlink-deep-dive.md` | `en/technical/chainlink-deep-dive.md` | ✅ |
| `technical/architecture-decisions.md` | `en/technical/architecture-decisions.md` | ✅ |
| `technical/technical-analysis.md` | `en/technical/technical-analysis.md` | ✅ |
| `technical/test-coverage-boundary.md` | `en/technical/test-coverage-boundary.md` | ✅ |
| `technical/pipeline/overview.md` | `en/technical/pipeline/overview.md` | ✅ |
| `technical/pipeline/injector.md` | `en/technical/pipeline/injector.md` | ✅ |
| `technical/pipeline/compiler.md` | `en/technical/pipeline/compiler.md` | ✅ |
| `technical/pipeline/evaluator.md` | `en/technical/pipeline/evaluator.md` | ✅ |
| `technical/pipeline/instruction.md` | `en/technical/pipeline/instruction.md` | ✅ |
| `technical/pipeline/jit.md` | `en/technical/pipeline/jit.md` | ✅ |
| `technical/pipeline/lexer.md` | `en/technical/pipeline/lexer.md` | ✅ |
| `technical/pipeline/platform.md` | `en/technical/pipeline/platform.md` | ✅ |
| `migration-guide.md` | `en/migration-guide.md` | ✅ |
| `translation-guide.md` | `en/translation-guide.md` | ✅ |
| `multilingual-collaboration.md` | `en/multilingual-collaboration.md` | ✅ |

## Questions

If you are unsure about technical terminology during translation:
- Reference existing English translation pages for precedent
- Ask in GitHub Discussions

For guidance on participating without Chinese or English fluency, see the [Multilingual Collaboration](/en/multilingual-collaboration) page.
