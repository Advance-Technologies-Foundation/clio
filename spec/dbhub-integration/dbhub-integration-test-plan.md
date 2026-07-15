# Test Plan: dbHub Installation and Creatio Source Synchronization

**Feature**: dbhub-integration
**Story**: [dbhub-integration-story.md](dbhub-integration-story.md)
**Author**: QA Planner Agent (autonomous mode)
**Status**: Approved
**Created**: 2026-07-15

---

## Scope

### In scope

- Settings/schema, Windows installation/adoption/repair, scheduled-task contract, health/MCP checks.
- Connection discovery, managed TOML preservation/ownership/atomicity/concurrency, manual reconciliation.
- Deploy/uninstall best-effort ordering and CLI/MCP/Ring warning behavior.
- Secret leakage guards and disposable local Creatio lifecycle proof.

### Out of scope

- Real cloud environments, SQL Server integrated identity, dbHub restart behavior, and MCP-exposed workstation installation/manual sync.

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| TOML corruption or manual-content loss | Medium | High | parser validation, surgical markers, temp/rollback and preservation integration tests |
| Secret leakage | Medium | Critical | sentinel assertions across every log/result/progress/error path |
| Lifecycle regression | Medium | High | exact ordering and failure-path tests plus disposable deployment |
| Ring contract drift | Medium | High | mirrored fixture, consumer tests, AOT publish |
| Windows task differences | Medium | Medium | task XML/process abstractions and local smoke validation |
| MCP E2E not a required CI gate | High | Medium | run targeted net8/net10 E2E locally before PR |

## Unit Tests (`clio.tests`)

| ID | Scenario |
|----|----------|
| TC-U-01 | dbHub settings default disabled/no path and explicit values round-trip through repository |
| TC-U-02 | install validation rejects non-Windows, non-loopback host, invalid port, missing/old Node, and missing npm with actionable messages |
| TC-U-03 | installer installs exact `@bytebase/dbhub@0.23.0` only when absent and resolves shim through `npm prefix -g` |
| TC-U-04 | compatible install/task/config adopts as no-op; unsafe/mismatched owned launcher/task repairs idempotently |
| TC-U-05 | port conflict, task failure, health failure, and MCP failure report the failing step without secrets |
| TC-U-06 | source IDs normalize to lowercase underscore form; raw and normalized collisions are refused |
| TC-U-07 | PostgreSQL and SQL Server builders map correct individual fields and escape TOML strings |
| TC-U-08 | SQL integrated authentication and missing/malformed/unreadable config warn/skip safely |
| TC-U-09 | eligibility excludes remote/no-path environments and uses `ConnectionStrings.config` rather than alias metadata |
| TC-U-10 | owned matching source no-ops; owned drift updates; unowned conflict refuses; exact environment removal targets only its block |
| TC-U-11 | reconciler handles existing, missing, stale, colliding, skipped, and selected-one environments deterministically |
| TC-U-12 | online verification waits/polls after debounce; offline verification retains mutation and reports skipped live verification |
| TC-U-13 | deploy sync runs only after wait-ready and failed deploy paths never invoke it |
| TC-U-14 | uninstall removal runs after DB/files cleanup, before unregister, and is retained on earlier failure/path-only uninstall |
| TC-U-15 | dbHub lifecycle failure emits one warning, exit 0, warning stage, and `success-with-warnings` without failed cascade |
| TC-U-16 | command options/dispatch/DI and `BaseCommandTests` fixtures follow kebab-case and resolve through container |
| TC-U-17 | MCP execution result contains typed WarningMessage with `IsError=false`; progress is ordered and secret-free |
| TC-U-18 | Ring renders warning/yellow and completed-with-warnings, refreshes environments, records receipts, and tolerates unknown fields/order |
| TC-U-19 | every sentinel password/DSN is absent from output, logs, results, progress, exceptions, and diagnostics |

## Integration Tests (`clio.tests`)

| ID | Scenario |
|----|----------|
| TC-I-01 | real temp TOML add/update/remove preserves manual sources, comments, ordering, tools, and unknown fields |
| TC-I-02 | candidate parse failure/write failure leaves original byte-identical and cleans temporary files |
| TC-I-03 | concurrent mutation serializes without lost updates and an unavailable lock times out safely |
| TC-I-04 | existing file ACL/mode is preserved and symlink/reparse targets are refused |
| TC-I-05 | generated Scheduled Task XML carries hidden logon trigger, current-user principal, absolute shim, explicit loopback/port/config, and safe quoting |
| TC-I-06 | settings JSON schema accepts valid dbHub settings and rejects invalid host/port/types |

## MCP E2E (`clio.mcp.e2e`)

| ID | Scenario |
|----|----------|
| TC-E-01 | deploy automatic sync failure streams warning + `success-with-warnings`, returns success, and leaks no sentinel secret |
| TC-E-02 | uninstall automatic removal failure does the same and still unregisters |
| TC-E-03 | mirrored contract fixture includes warning vocabulary and remains ordered/byte compatible |

Run TC-E locally for both net8.0 and net10.0; do not wait for TeamCity MCP E2E.

## Local Runtime Validation

1. Exercise `install-dbhub` against the existing global `@bytebase/dbhub` installation and TOML using a backup/sandbox config path; verify explicit `127.0.0.1`, current-user hidden task, `/healthz`, and MCP POST. Restore any pre-existing task/config state exactly.
2. Verify hot reload by adding/removing a disposable managed source and polling HTTP after >500 ms without restarting the task.
3. If lifecycle proof cannot be fully simulated, deploy the user-supplied Creatio archive under a unique disposable name. Verify successful deploy adds the source, offline dbHub gives warning/exit 0, and successful uninstall removes the exact source. Confirm IIS/database/files/settings/TOML cleanup.

### Runtime evidence (2026-07-15)

- Used an isolated `CLIO_HOME`, ports `18090`/`18091`, and the supplied Creatio archive; the real user settings and Scheduled Task were not modified.
- Verified offline deploy completes with one warning and retains the managed TOML source for later reconciliation.
- Verified online deploy hot-adds the Creatio PostgreSQL source, returns HTTP 200 from the deployed site, and produces credential-free output.
- Verified dbHub 0.23.0 rejects PostgreSQL `prefer`/`allow` TLS modes and an empty source inventory; the implementation maps those modes to supported values and maintains a marked lazy in-memory `clio_control` source.
- Ran the destructive MCP uninstall scenario locally: it returned success-with-warnings for the independent profile warning, preserved strict stage order, removed the exact managed dbHub source, and hot-reloaded dbHub back to only `clio_control`.
- Verified the disposable site, files, clio environment registration, managed TOML source, isolated settings, and isolated dbHub processes were removed after the run.

## Regression and Delivery Gates

```powershell
dotnet build clio/clio.csproj -c Release
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"
dotnet test clio.tests/clio.tests.csproj --filter "Category=Integration&FullyQualifiedName~DbHub"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Release -f net8.0 --filter "FullyQualifiedName~DeployUninstallProgressTests"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Release -f net10.0 --filter "FullyQualifiedName~DeployUninstallProgressTests"
dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release
dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

## Definition of Done for QA

- [ ] Every TC-U and TC-I case is implemented and green.
- [ ] Relevant TC-E cases pass locally on both target frameworks.
- [ ] Disposable runtime state is verified and cleaned or explicitly not required with evidence.
- [ ] Full unit gate passes because shared `Common`, `BindingsModule`, and multiple modules change.
- [ ] Ring tests and Windows x64 NativeAOT publish pass without IL2026/IL3050 warnings.
