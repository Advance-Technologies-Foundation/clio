---
description: an empty .props file that a csproj imports makes MSBuild fail the whole project with MSB4024 "Root element is missing"
applies-to:
  - clio/Common/PropsBuilder.cs
  - clio/Common/NugetMaterializer.cs
ticket: GH-263
date: 2026-08-30
---

**What is true** — MSBuild treats an `<Import>` of a zero-byte (or otherwise root-less) file as a
hard project-load error, not as a no-op:

```
error MSB4024: The imported project file "<Pkg>-netstandard.nuget.props" could not be loaded.
Root element is missing.
```

So `PropsBuilder` must never write a props file with no content, and `NugetMaterializer` must never
add the matching `<Import>` for a props file that was not written. When a moniker produces no content,
`PropsBuilder` also removes from `Files/Libs/<moniker>` exactly the assemblies the previous props file
declared — that folder holds hand-placed dlls too, so it is never emptied wholesale on that path. `PropsBuilder.Build` returns
`PropsBuildResult` for exactly this reason — the per-moniker flags are the only thing that tells the
caller which imports are safe to add.

**Why it is this way** — a nuget dependency does not necessarily produce a runtime dll for every
target moniker. An analyzer or tooling-only package (`StyleCop.Analyzers`), or a package shipping a
single moniker, leaves `.nuget/<Pkg>/bin/<moniker>` with nothing to reference. Before this was
handled, `Process` returned an empty string and the file was written anyway.

A package already broken this way is repaired on the next run: `RepairUnusablePropsImports` deletes
the unusable props file and removes its import even when every `PackageReference` is already
commented out and there is nothing left to materialize. When that run leaves no props import at all,
it also turns the `PackageReference` comments back into elements, because otherwise the project
builds again while silently losing the analyzers and build assets those packages carried.

**What breaks if you ignore it** — `switch-nuget-to-dll-reference` reports success, and the package
it just converted no longer builds at all: every build of that csproj dies at project-load time,
before any target runs. The user gets no warning connecting the broken build to the command.
