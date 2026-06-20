# Installation

FluxFormula is a three-package monorepo. Install only what you need.

## Package Structure

| Package | Purpose | Dependencies |
|---------|---------|--------------|
| `com.twds0x13.fluxformula.core` | Pure C# pipeline engine (zero Unity dependency) | None |
| `com.twds0x13.fluxformula` | Unity integration (ScriptableObject + Editor) | Core |
| `com.twds0x13.fluxformula.addressables` | Addressables-based formula loading | Core + FluxFormula + Unity.Addressables |

## Choose by Scenario

| Scenario | Packages to install |
|----------|---------------------|
| Standalone .NET / Godot / server | Core only |
| Unity basic usage | Core + FluxFormula |
| Unity + Addressables loading | Core + FluxFormula + Addressables |

## Unity Package Manager

Open **Window → Package Manager**, click **+ → Add package from git URL**, and enter each URL:

```
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula
https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables
```

Installation order does not matter. UPM resolves dependencies automatically — adding `fluxformula` without `core` will produce a dependency error; install `core` first.

Requires Unity 2019.3.4f1 or later for `?path` query parameter support. The packages themselves require Unity 2021.3 minimum.

## Manual Installation

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula.core": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.core",
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula",
    "com.twds0x13.fluxformula.addressables": "https://github.com/twds0x13/FluxFormula.git?path=packages/fluxformula.addressables"
  }
}
```

Remove the lines for packages you don't need. `core` is a prerequisite for both `fluxformula` and `addressables` — include it when installing either.

## Unsafe Code

Package runtime assemblies have `allowUnsafeCode` enabled by default. No additional configuration is needed — the asmdef setting overrides any project-level `unsafe` restriction.

## Dependencies

The Core package depends on `com.unity.collections` (≥1.2.4) to provide `System.Memory` on Unity 2021.3. Unity 2022.3 and later include this dependency natively.

## Local Testing

Run the full test suite without Unity (149 test cases):

```bash
dotnet test tests/FluxFormula.Core.Tests/FluxFormula.Tests.csproj
```

Requires .NET SDK 8.0+.
