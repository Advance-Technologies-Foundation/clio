---
description: asserting IsInteractive == false does not prove a NonInteractiveConsole was injected - under dotnet test stdin is redirected, so RealInteractiveConsole reports false too; assert BeSameAs(NonInteractiveConsole.Shared)
applies-to:
  - clio.tests/Command/McpServer/ToolCommandResolverTests.cs
  - clio/Common/RealInteractiveConsole.cs
ticket: ENG-93157
date: 2026-08-19
---

**What is true** — `RealInteractiveConsole.IsInteractive` is `!_isInputRedirected()`, and the test
host redirects stdin. Inside `dotnet test` the real console therefore reports
`IsInteractive == false` exactly like `NonInteractiveConsole` does. An assertion of the form
`resolved.IsInteractive.Should().BeFalse()` consequently proves nothing about which implementation
was injected. To prove the injection, assert the instance:
`resolved.Should().BeSameAs(NonInteractiveConsole.Shared)`.

**Why it is this way** — non-interactivity is a *property of the environment* the test runner already
supplies, while the behaviour under test is a *container registration*. The property is satisfied by
the runner, so it cannot discriminate between the two implementations; only object identity can.

**What breaks if you ignore it** — the test is green with the production code deleted. Removing the
`NonInteractiveConsole.ForceInContainer` registration from a child container leaves every
`IsInteractive == false` assertion passing, so the guard that keeps an MCP-resolved command from
blocking on `Console.ReadKey()` on a TTY-attached host can be dropped in a refactor with full CI
approval. The same trap applies to any test that asserts an environment-supplied property instead of
the injected instance.
