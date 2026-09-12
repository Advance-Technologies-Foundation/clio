# switch-nuget-to-dll-reference

## Name

switch-nuget-to-dll-reference - Switch NuGet references to DLL references in csproj files

## Description

Rewrites project references from NuGet packages to direct DLL references.

## Synopsis

```bash
clio switch-nuget-to-dll-reference <PackageName> [OPTIONS]
```

## Arguments

```bash
<PackageName>
Required. Name of the workspace package whose csproj is converted. The name must match a package
declared by the current workspace; anything else exits with code `1`.
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
`PackageReference` in the csproj instead of being commented out. A package is considered
materialized when a copied assembly is named after it, which also covers packages whose assembly
name extends the package id (`NUnit` ships `nunit.framework.dll`).

When neither target framework produced a props file, the csproj is left completely unchanged
and the command exits with code `1`.

A package broken by a clio version older than this fix — an empty props file plus its `<Import>` —
is repaired the next time the command runs against it: the unusable props file is deleted and its
import removed, so the package builds again. When the csproj declares no `PackageReference` at all,
because an earlier conversion commented every one of them out, and the repair leaves no props import
behind, those commented references are turned back into elements. Otherwise the project would build
while silently losing the analyzers and build assets those packages carried. References are not
restored while one props import survives, because a `PackageReference` and the dll reference that
replaced it in the same project fail the build on a duplicate assembly.

When a target framework ends up with no dependency to reference, the assemblies its previous props
file declared are removed from `Files/Libs/<moniker>` as well. Its `<Import>` is removed, so a dll
materialized by an earlier run would otherwise stay in the Creatio package and be deployed with it.
Only the names the generated props file lists are removed: that folder also holds dlls put there by
hand and referenced through their own `HintPath`, and those are left alone. A moniker for which no
props file was ever generated has nothing removed.

Every path the command writes to or deletes must be a real file or directory. A package folder, the
`.nuget` helper folder, the helper csproj, the package csproj and its `.bak`, the props files and the
package `Files/Libs` folder are each checked, and a symbolic link or junction anywhere between the
workspace `packages` (or `.nuget`) folder and the target makes the command exit with code `1` without
writing or deleting through it. The confinement check that keeps the command inside the workspace
compares canonical path strings, which does not resolve links, so a link that already exists on disk
would otherwise carry those writes — and the recursive `bin`/`obj` delete — outside the workspace.
The `packages` and `.nuget` folders themselves are not checked: they are the workspace's own layout,
and a `packages` folder that is a junction onto another drive is an ordinary setup.

## Examples

```bash
clio switch-nuget-to-dll-reference MyPackage
Convert the NuGet references of the workspace package MyPackage to dll references
```

```bash
clio nuget2dll MyPackage
Same conversion through the short alias
```

## See Also

ref-to - Update project core reference paths

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#switch-nuget-to-dll-reference)
