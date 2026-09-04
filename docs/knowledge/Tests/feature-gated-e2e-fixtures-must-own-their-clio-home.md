---
description: a feature-gated MCP tool is advertised only when the ambient clio home has the flag on, so an e2e fixture that reads the machine's clio home decides its own test set by which build agent ran it
applies-to:
  - clio.mcp.e2e/MobilePageConversionGuideSandboxE2ETests.cs
  - clio.mcp.e2e/MobilePageConversionGuideToolE2ETests.cs
  - clio.mcp.e2e/Support/Mcp/McpContractFixtureBase.cs
ticket: "1382"
date: 2026-09-04
---

**What is true** — feature flags persist in `appsettings.json` inside the clio home of the user
running clio, and a tool carrying `[FeatureToggle(...)]` is not registered on the MCP surface when
its flag is off. An e2e fixture that starts the server against the ambient home therefore inherits
whatever the last person or job to run clio on that machine left behind. Give the fixture its own
home instead: copy the ambient settings so the environment registration survives, force the flag on,
and point the child process at it with `CLIO_HOME` (`CreateIsolatedClioHome` in
`McpContractFixtureBase`). Once the fixture owns the home, an absent tool is a regression and must
fail — it can no longer mean "this machine has the flag off".

**Why it is this way** — the toggle is a user setting by design (`clio experimental --name ... --enable`),
there is no environment-variable override, and CI does not provision one. Copying rather than
replacing the ambient settings is required for the Sandbox tier specifically: it converts against a
registered environment, so a home built from scratch would lose the registration and the fixture
would degrade to "environment unreachable" instead.

**What breaks if you ignore it** — the gate silently becomes a property of the build agent. In the
TeamCity survey behind issue #1382 the `mobile-page-converter` fixture was ignored in 43 of 55 runs
and executed in 12, and the split was explained perfectly by which of 25 agents picked up the build —
no build parameter distinguished them. A suite in that state is not flaky, it is absent, and it
reports green.
