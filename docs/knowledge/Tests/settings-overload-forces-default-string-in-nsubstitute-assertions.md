---
description: NSubstitute assertions on services with both a name-based and an EnvironmentSettings-based overload must write default(string)! not default! or the call is CS0121 ambiguous
applies-to:
  - clio/Command/ApplicationInfoService.cs
  - clio/Command/EntitySchemaDesigner/CaptionCultureResolver.cs
  - clio.tests/Command/
ticket: ENG-93347
date: 2026-08-19
---

**What is true** — several environment-sensitive services expose a pair of overloads that differ only
in the first parameter's reference type: `GetApplicationInfo(string environmentName, ...)` alongside
`GetApplicationInfo(EnvironmentSettings environmentSettings, ...)` in `ApplicationInfoService`, and
the same shape in `CaptionCultureResolver` and the application/section services. Any NSubstitute
verification against such a method has to spell the literal as `default(string)!`; the bare
`default!` is untyped and matches both overloads.

**Why it is this way** — `default!` carries no type, so overload resolution has nothing to
discriminate on and the call is ambiguous at compile time (CS0121). Roughly twenty
`DidNotReceiveWithAnyArgs()` sites across `clio.tests/Command/` already carry the explicit form.

**What breaks if you ignore it** — adding a settings-based overload to a service breaks the build in
test files you did not touch, with an error that points at the assertion rather than at the new
overload, so the cause is easy to misread. Before adding one, grep the fixtures for `default!` against
that method and convert the first argument; converting it does not change what the assertion means.
