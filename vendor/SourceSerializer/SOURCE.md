# SourceSerializer

- **上游仓库**: https://github.com/twds0x13/SourceSerializer
- **Vendored 版本**: v3.5.2
- **Vendored 日期**: 2026-07-28
- **本地修改**: 无（纯净 vendor，直接从上游复制）
- **许可证**: MIT（与 FluxFormula 相同）
- **同步方法**: 从上游 Release 下载源码，覆盖本目录，提交时标注 `chore: sync SourceSerializer vX.Y.Z from upstream`
- **在 FluxFormula 中的用途**: 为 `LexerConfig.LiteralScanner` 提供默认实现——SourceSerializer 的编译期代码生成替代了手写文字量词法分析器，消除反射开销。
