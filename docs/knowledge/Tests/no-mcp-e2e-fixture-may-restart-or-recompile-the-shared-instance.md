---
description: clio.mcp.e2e has no fixture that restarts or recompiles the Creatio instance - a mid-suite restart cascades to the ~30 fixtures sharing it under NumberOfTestWorkers=2, and marking such a fixture [Explicit] makes it a script rather than a gate
applies-to:
  - clio.mcp.e2e/
  - clio.mcp.e2e/Support/Configuration/ClioCliCommandRunner.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — no fixture in `clio.mcp.e2e` performs a real install that restarts the platform or
makes the target rebuild its configuration. `install-gate` has no fixture at all - it appears only as
probe-first arrange (`ClioCliCommandRunner.EnsureCliogateInstalledAsync`) for roughly thirty
fixtures; `compile-creatio` and `restart-web-app` cover only their negative paths;
`deploy-creatio` deliberately feeds a corrupt archive so nothing is created. `clio.mcp.e2e/AGENTS.md`
documents the destructive sub-tier for uninstall/deploy fixtures, but a mere **restart** is the case
it does not name.

**Why it is this way** — one Creatio instance backs the whole run and
`clio.mcp.e2e.runsettings` sets `NumberOfTestWorkers=2`, so a restart lands in the middle of other
fixtures' work. Readiness is not binary either: an instance already answering
`/api/HealthCheck/Ping` can still be warming its auth pipeline, which is why
`ClioCliCommandRunner.WaitForLoginReadinessAsync` exists at all.

**What breaks if you ignore it** — one restarting fixture reds a scatter of unrelated fixtures whose
failures point nowhere near the change, and a rerun may pass. The tempting fix - mark it `[Explicit]`
like `DbHubLifecycleWarningE2ETests` - does not buy a regression gate: it never runs in CI, so it is a
package-mutating script with assertions attached. Cover the restarting path with stand-free tests
plus unit tests instead, and when adding one prove it is not vacuous by flipping an assertion to a
false one and confirming it fails with real data.
