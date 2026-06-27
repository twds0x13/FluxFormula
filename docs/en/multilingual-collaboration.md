# Multilingual Collaboration

The FluxFormula documentation site currently maintains two locales: Simplified Chinese (`zh-CN`, source language) and English (`en`). This page covers how non-Chinese and non-English speakers can participate, how to request new language support, and how to lower the language barrier with machine translation.

## Current Language Status

| Language | Docs Coverage | Source Language | Issue Templates | Discussion Support |
|----------|:---:|:---:|:---:|:---:|
| 简体中文 | Full | Yes | Chinese | Yes |
| English | Full (1:1 translation) | No | English | Yes |
| Other languages | None | No | No | Case by case |

See the [Translation Guide](/en/translation-guide#conventions) for the technical terminology table.

## Participating Without Chinese or English

The project's primary communication languages are Simplified Chinese and English. If you are not fluent in either:

1. Post in any language on [GitHub Discussions](https://github.com/twds0x13/FluxFormula/discussions). Machine-translated replies are fully acceptable — the maintainer will not judge language quality.
2. Bug reports: use the English [Bug Report](https://github.com/twds0x13/FluxFormula/issues/new?template=bug_report.yml) template. The description can mix in your native language. Code and reproduction steps are a universal language.
3. Code contributions: English or Chinese is recommended for PR descriptions, but the code itself is the primary communication medium. Variable names, type names, and comments are already in English in the source.

## Requesting a New Language

Adding a new locale requires three preconditions:

1. At least one maintainer or community member willing to handle the initial translation and ongoing synchronization
2. A sufficiently large potential user base for the language
3. VitePress i18n infrastructure support for the locale

Process:

1. Post in the Ideas category of [Discussions](https://github.com/twds0x13/FluxFormula/discussions) with the title format: `[i18n] <language name> locale request`
2. State translation willingness (self-translate or request community assistance) and expected maintenance model
3. After maintainer evaluation, if conditions are met, the locale directory structure will be created with an initial translation template

## Machine Translation as a Bridge

Machine translation is an effective tool in these scenarios:

- **Reading docs**: use browser built-in translation or DeepL/Google Translate on documentation pages. VitePress-generated static HTML is compatible with mainstream translation engines.
- **Writing issues**: write the content in your native language first, then machine-translate to English before pasting. Append `(machine-translated from <language>)` at the end so the maintainer can trace back to the original if ambiguity arises.
- **Translation contributions**: machine translation can serve as a first draft, but only human-reviewed versions can be merged into the documentation site. Pure machine-translated pages do not meet the accuracy requirements of the [Translation Guide](/en/translation-guide).

## Language-Independent Entry Points

These contribution paths have low dependency on natural language ability:

| Path | Language Requirement |
|------|:---:|
| Bug reports (code + reproduction steps) | Low |
| Unit test contributions | Low |
| Benchmark additions | Low |
| API doc review (source comments vs. docs consistency) | Medium |
| Translation review | High |

## Related Pages

- [Translation Guide](/en/translation-guide): Chinese ↔ English translation conventions and workflow
- [Contributing Guide](https://github.com/twds0x13/FluxFormula/blob/main/CONTRIBUTING.md): bug reports, PR submission, dev environment setup
- [GitHub Discussions](https://github.com/twds0x13/FluxFormula/discussions): questions and discussion
