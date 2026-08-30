---
description: ref-to detects the current reference style by matching a literal folder name in HintPath; an unmatched style rewrites nothing
applies-to:
  - clio/Project/CreatioPkgProject.cs
  - clio/Command/ReferenceCommand.cs
ticket: GH-1280
date: 2026-08-30
---

**What is true** — `CreatioPkgProject.DetermineCurrentRef` recognizes the project's current
reference style by searching `HintPath` values for a literal substring: `CreatioSDK` or the
pre-rename `BpmonlineSDK` for the nuget style, `..\..\..\..\..\..\..\Bin\Debug\` for core sources,
`UnitTest`, `$(TsCoreBinPath)`. When none matches, `CurrentRefType` stays `Undef`,
`GetSearchPattern` returns the literal string `"undefined"`, and the rewrite touches nothing.

The bin style is detected **after** the core-source style, because `..\..\..\Bin\` is a substring of
`..\..\..\..\..\..\..\Bin\Debug\` and would otherwise classify every core-source project as bin.

`ChangedReferencesCount` counts references whose `HintPath` actually changed, not those that matched
the pattern. A `HintPath` written with forward slashes matched the pattern but was never rewritten,
which reproduced this very defect while reporting success.

`ChangedReferencesCount` exists so that outcome is visible: `ReferenceCommand` fails instead of
reporting `Done`, and `new-pkg` does not delete `packages.config` after a rebase that did nothing.

**Why it is this way** — the detection predates the Bpmonline-to-Creatio rename, and the shipped
package template `clio/tpl/Proj.csproj.tpl` moved to `packages\CreatioSDK.<version>\...` without the
pattern list following it.

**What breaks if you ignore it** — before `CreatioSDK` was added, `new-pkg X -r bin` printed `Done`,
exited 0, left every `HintPath` pointing at the nuget folder, and deleted the `packages.config` that
would have restored those assemblies. The package did not build, and nothing in the output said so.
Add the new folder name to the pattern list whenever the SDK package is renamed again.
