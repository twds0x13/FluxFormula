; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FLX001 | FluxFormula | Error | Literal Template Error
FLX002 | FluxFormula | Error | Circular template dependency
FLX003 | FluxFormula | Error | Readonly struct cannot use LiteralTemplate
FLX004 | FluxFormula | Warning | Missing template dependency
