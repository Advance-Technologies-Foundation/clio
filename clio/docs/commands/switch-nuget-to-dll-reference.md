# switch-nuget-to-dll-reference

## Name

switch-nuget-to-dll-reference - Switch NuGet references to DLL references in csproj files

## Description

Rewrites project references from NuGet packages to direct DLL references.

## Synopsis

```bash
clio switch-nuget-to-dll-reference [OPTIONS]
```

## Options

```bash
Supports the canonical switch-nuget-to-dll-reference command options.
```

## Behavior

For each target framework (`net472` and `netstandard2.0`) the command builds a props file
listing the dlls the package depends on, copies those dlls into `Files/Libs/<moniker>`,
and adds the matching `<Import>` to the package csproj.

When a target framework has no dependency dll to reference — for example the package only
references an analyzer or tooling-only NuGet package — no props file is written for it and
no `<Import>` is added, because MSBuild fails the whole project when it imports a file with
no root element. A warning names the skipped props file.

A NuGet package that contributes no assembly at all — an analyzer, for example — keeps its
`PackageReference` in the csproj instead of being commented out.

When neither target framework produced a props file, the csproj is left completely unchanged
and the command exits with code `1`.

A package broken by a clio version older than this fix — an empty props file plus its `<Import>` —
is repaired the next time the command runs against it: the unusable props file is deleted and its
import removed, so the package builds again.

## Examples

```bash
clio switch-nuget-to-dll-reference --help
Display canonical options and usage examples
```

## See Also

ref-to - Update project core reference paths

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#switch-nuget-to-dll-reference)
