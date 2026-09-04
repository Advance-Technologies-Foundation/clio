# ref-to

Change package project core paths.

## Synopsis

```bash
clio ref-to [OPTIONS]
```

## Description

Updates project references so a package uses the intended Creatio core binaries or paths.

## See also

- [switch-nuget-to-dll-reference](./switch-nuget-to-dll-reference.md)

## Behavior

The command detects the project's current reference style from its `HintPath` values, then rewrites
every matching reference to the requested one. Recognized styles: the NuGet SDK folder
(`CreatioSDK`, and the pre-rename `BpmonlineSDK`), core sources, the local `Bin` folder, unit-test
binaries and `$(TsCoreBinPath)`.

Exit codes:

- `0` — references were rewritten, or the project is already in the requested style (running the
  command twice is not an error).
- `1` — the current reference style was not recognized. Nothing is written: rewriting nothing while
  removing `packages.config` would leave a project that cannot restore its assemblies.

The number of rewritten references is reported as `Changed N references`.

