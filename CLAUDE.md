# FluxFormula 项目规则

通用行为规则见 `~/.claude/CLAUDE.md`。项目记忆见 `.claude/memory/MEMORY.md`。

## 1. CHANGELOG 由 semantic-release 管理
禁止手动编辑 CHANGELOG.md。`@semantic-release/changelog` 从 conventional commit messages 自动生成。
禁止在 commit body 中放冗长的技术清单——它们会原样进入 release notes。
禁止在 commit message 中包含 `[skip test]` 或 CI trailers。

## 2. IL 发射：检查 ECMA-335
Mono 验证器比 CoreCLR 更严格。发射 IL 前确认类型兼容性。
