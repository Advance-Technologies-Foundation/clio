---
description: every [Parallelizable(ParallelScope.Self)] fixture in clio.mcp.e2e is stand-free by construction, so a flaky sandbox failure can never be blamed on the parallel pool - the OData rebuild that causes it is started inside the sequential queue and outlives the command
applies-to:
  - clio.mcp.e2e/
  - clio.mcp.e2e/clio.mcp.e2e.runsettings
  - clio.mcp.e2e/Support/Mcp/TransientPlatformConditionRetryGate.cs
ticket: "1381"
date: 2026-09-04
---

**What is true** — `clio.mcp.e2e.runsettings` sets `NumberOfTestWorkers=2`, but the 26 fixtures marked
`[Parallelizable(ParallelScope.Self)]` cannot touch the run's Creatio instance. Each one is guarded in
one of four ways, verified fixture by fixture: an invalid random `environment-name`
(`$"missing-*-{Guid.NewGuid():N}"`) that fails resolution before any mutation; an isolated `CLIO_HOME`
pointing at a fixture-owned path or a loopback stub; purely local synthetic file operations; or
deliberately corrupt input that fails before the mutating stage (`DeployCreatioToolE2ETests`,
`RestoreDbToolE2ETests`). None of them carries `McpE2E.Sandbox` — they are `McpE2E.NoEnvironment` only,
and `clio.tests/McpFixturePolicyTests.cs` already enforces that split. There is no assembly-level
`[Parallelizable]` or `LevelOfParallelism` override, so those 26 are the entire pool.

**Why it is this way** — the `McpE2E.NoEnvironment` tier exists precisely to be a fast deterministic gate
that runs with no Creatio, so its fixtures are the only ones allowed into the parallel pool.

**What breaks if you ignore it** — a flaky `create-app` or read-back failure on the shared stand reads
like a parallelism problem, and the obvious "fix" is to move fixtures out of the parallel pool. That
change is pure churn: it cannot help, because the disturbance comes from the **sequential** queue —
`create-entity-schema` (and the schema publish inside `create-app`) return as soon as they have started
the asynchronous, global OData rebuild, and that rebuild outlives the command that started it, so the
next serialized test sees "Creatio is currently rebuilding the OData library" no matter how strictly the
fixtures are serialized (see [no-mcp-e2e-fixture-may-restart-or-recompile-the-shared-instance.md](no-mcp-e2e-fixture-may-restart-or-recompile-the-shared-instance.md)).
The only boundary that works on the consumer side is a bounded wait/retry on that known window —
`TransientPlatformConditionRetryGate` for the tool call, `DataBindingDbFixtureBase.WaitUntilSchemaIsQueryableAsync`
for a freshly published schema.
