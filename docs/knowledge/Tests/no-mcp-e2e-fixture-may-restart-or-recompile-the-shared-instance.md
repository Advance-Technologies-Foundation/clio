---
description: no automatic clio.mcp.e2e fixture restarts or recompiles the Creatio instance - a mid-suite restart cascades to the ~30 fixtures sharing it under NumberOfTestWorkers=2; the two compiling fixtures are developer-local [Explicit] scripts, not gates
applies-to:
  - clio.mcp.e2e/
  - clio.mcp.e2e/Support/Configuration/ClioCliCommandRunner.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — no fixture that can run automatically in `clio.mcp.e2e` performs a real install
that restarts the platform or makes the target rebuild its configuration. `install-gate` has no fixture
at all - it appears only as probe-first arrange (`ClioCliCommandRunner.EnsureCliogateInstalledAsync`) for
roughly thirty fixtures; `restart-web-app` covers only its negative paths, and `compile-creatio` its
negative paths plus one `process-name` success that compiles NOTHING (a process without C#, answered by
the server without a build - `ScriptTaskElementToolE2ETests`); `deploy-creatio` deliberately feeds a
corrupt archive so nothing is created. The only fixtures that DO compile are developer-local, in the
destructive sub-tier `clio.mcp.e2e/AGENTS.md` documents (`LocalOnly` + `[Explicit]` + `McpE2E.Manual`, a
TeamCity/GitHub guard and the `McpE2E__AllowDestructiveMcpTests` opt-in): `UserTaskUnlimitedTextToolE2ETests`
and `ScriptTaskCompileLifecycleE2ETests`, a process-name compile that builds. A mere **restart** is the
case that sub-tier does not name.

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
