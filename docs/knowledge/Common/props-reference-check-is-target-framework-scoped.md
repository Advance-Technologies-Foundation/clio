---
description: a Reference scoped to one TargetFramework must not suppress the props entry for the others
applies-to:
  - clio/Common/PropsBuilder.cs
ticket: GH-1283
date: 2026-08-30
---

**What is true** — `PropsBuilder` decides whether a dll is already referenced by the package csproj.
That check has to honour the `Condition` of the `<Reference>` and of every enclosing
`<Choose>`/`<When>`. `XDocument.Descendants` walks into conditional blocks and returns elements that
do not apply to the target framework being built.

Only conditions written against `$(TargetFramework)` are interpreted. Any other condition is assumed
to apply, which keeps the earlier behaviour for expressions clio cannot evaluate.

**Why it is this way** — the package template clio ships declares a dependency twice: a `<Reference>`
for `net472` and a `<PackageReference>` for `netstandard2.0` (`System.Text.Json`,
`Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection`).

**What breaks if you ignore it** — building the netstandard props file, the net472-only `<Reference>`
counts as "already referenced": the dll is written into no props file and copied into no
`Files/Libs/netstandard`. Combined with `switch-nuget-to-dll-reference` commenting out the
`PackageReference`, the netstandard build then has no source for the dependency at all.
