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

**`<Otherwise>` (PR #1496 review)** — an `<Otherwise>` carries NO `Condition` attribute; its condition is
the implicit negation of its sibling `<When>`s. The ancestor walk reads `Condition` attributes only, so it
found nothing above a reference inside `<Otherwise>`, concluded it applied unconditionally, and suppressed
it from the props file of the very framework the sibling `<When>` excludes — the same defect as the
`<When>` side, mirrored. An `<Otherwise>` whose `<Choose>` has a `<When>` mentioning `$(TargetFramework)`
is therefore treated as unevaluable and the dependency is kept, on the same "a duplicate reference is a
warning, a missing one is a compile error" rule as the complex-condition arm. An `<Otherwise>` under a
`<Choose>` that says nothing about the target framework is left alone.


**The mention test covers the whole `TargetFramework*` family (PR #1496 review)** — the "does this
condition mention the property" regex is `\$\(TargetFramework[^)]*\)`, so `$(TargetFrameworks)`,
`$(TargetFrameworkVersion)` and `$(TargetFrameworkIdentifier)` all qualify even though none of them is
the property the simple-comparison regex evaluates. That is deliberate and it lands on the same safe
side: a condition built on one of them is unevaluable, so the dependency is kept. Narrowing the test to
the exact property would make the classic `'$(TargetFrameworkVersion)' == 'v4.7.2'` idiom read as
"no opinion about the target framework", and the dll it guards would be suppressed.

The knock-on is worth stating because it is not free: a workspace on that classic idiom gets the dll
into **both** props files, copied into both `Files/Libs/*`, and listed in `materializedAssemblies` —
which is what makes `switch-nuget-to-dll-reference` comment out its `PackageReference`. Two references
where one would do is an MSBuild warning, not a failure, so this is the cost the safe direction buys.

**Not covered: the target framework moniker is fixed, not read from the csproj** — the builder compares
conditions against the literal `net472` / `netstandard2.0` (`NugetMaterializer` carries the same pair).
A workspace whose csproj targets a different netstandard moniker is therefore evaluated against the
wrong one, and a `!=` condition naming that other moniker is the one shape where this check can
subtract: clio reads it as applying and suppresses a dll MSBuild would have needed. Out of scope for
GH-1283, which is about honouring conditions at all; fixing it means resolving the moniker from
`<TargetFrameworks>` and belongs with the materializer that shares the constant.
