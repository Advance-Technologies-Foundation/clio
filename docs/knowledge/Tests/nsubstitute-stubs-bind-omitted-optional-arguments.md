---
description: an NSubstitute setup or Received assertion that omits an optional parameter matches only the literal default, so adding a parameter such as enableContentionRetry silently unbinds every existing stub instead of breaking the build
applies-to:
  - clio.tests/Command/ApplicationSectionCreateServiceTests.cs
  - clio.tests/Command/McpServer/ApplicationToolTests.cs
  - clio.tests/Command/McpServer/CaptionCultureArgMappingToolTests.cs
ticket: ENG-93089
date: 2026-08-19
---

**What is true** — C# fills omitted optional arguments at the call site, so an NSubstitute setup
written as `service.CreateSection(settings, request)` compiles into a call carrying the literal
defaults and matches only invocations that pass exactly those values. When
`IApplicationSectionCreateService.CreateSection` gained `bool enableContentionRetry = false`, every
existing stub kept compiling but stopped matching the MCP path, which passes `true`. The fixtures
therefore spell the tail out explicitly: `Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(),
Arg.Any<Action<string>>()`.

**Why it is this way** — argument matchers are ordinary arguments; NSubstitute never sees that a
value came from a default rather than from the test.

**What breaks if you ignore it** — the substitute falls through to its auto-value (a default result,
or `false`/`""`), so the test still passes while exercising a different branch than its name claims,
and a `Received` assertion on the same method reports zero matching calls even though the call
happened. No compiler error and no NSubstitute error points at the cause. When you add an optional
parameter to a service interface, grep the fixtures for that method and extend every setup and
assertion with an explicit matcher for the new argument.
