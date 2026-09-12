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

Only a condition that is a single `$(TargetFramework)` comparison is interpreted. A condition that
mentions the property inside something more complex - an `And`, an `Or`, a negation, a property
function - is treated as NOT applying, so the dll **is** written into the props file. That direction
is deliberate: a duplicate reference is an MSBuild warning, while a missing one is a compile error.
A condition that does not mention `$(TargetFramework)` at all is assumed to apply, as before.

**Why it is this way** — the package template clio ships declares a dependency twice: a `<Reference>`
for `net472` and a `<PackageReference>` for `netstandard2.0` (`System.Text.Json`,
`Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection`).

**What breaks if you ignore it** — building the netstandard props file, the net472-only `<Reference>`
counts as "already referenced": the dll is written into no props file and copied into no
`Files/Libs/netstandard`. Combined with `switch-nuget-to-dll-reference` commenting out the
`PackageReference`, the netstandard build then has no source for the dependency at all.
