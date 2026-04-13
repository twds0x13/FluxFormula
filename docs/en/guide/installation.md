# Installation

## Unity Package Manager

In Unity, open **Window → Package Manager**, click **+ → Add package from git URL**, and enter:

```
https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula
```

Unity 2019.3.4f1 or later is required for `?path` query parameter support. The package itself requires Unity 2021.3 minimum.

## Manual Installation

Add the dependency in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.twds0x13.fluxformula": "https://github.com/twds0x13/FluxFormula.git?path=/com.twds0x13.fluxformula"
  }
}
```

## Unsafe Code Permission

The package runtime assembly requires `unsafe` code permission, enabled by default in `FluxFormula.asmdef`. No additional configuration is needed for projects with global `unsafe` restrictions — the asmdef setting overrides project-level constraints.

## Local Testing

Run the full unit test suite without Unity. Covers compilation, interpreter, JIT, error propagation, and Connect edge cases:

```bash
dotnet test standalone-tests/FluxFormula.Tests.csproj
```
