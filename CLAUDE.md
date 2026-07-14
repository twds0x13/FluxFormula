# CRITICAL RULES — READ BEFORE ANY ACTION

## 1. No speculative coding
Before committing or declaring done, verify:
- [ ] Grep old type/symbol name — zero residual references across ALL csproj files
- [ ] Full build all projects, not just the one you changed
- [ ] IL emission: check ECMA-335 type compatibility rules (Mono verifier is stricter than CoreCLR)
- [ ] Struct copy semantics: if a method takes `struct`, it modifies a copy

## 2. CHANGELOG is owned by semantic-release
DO NOT manually edit CHANGELOG.md. `@semantic-release/changelog` generates it from conventional commit messages.
DO NOT put verbose technical dumps in commit bodies — they become release notes verbatim.
DO NOT include `[skip test]` or CI trailers in commit messages.

## 3. CI is for confirmation, not discovery
Test locally before pushing. If CI fails, observe first — do not guess-fix.
Use `gh` to check CI logs before touching code.

## 4. Search before you act
When asked to do something, grep/Grep relevant code FIRST. Do not rely on memory or assumptions about the codebase.
