# ADR: dbHub Installation and Source Synchronization

**Status**: Accepted
**Author**: Architect Agent (autonomous mode)
**PRD**: [dbhub-integration-prd.md](dbhub-integration-prd.md)
**Created**: 2026-07-15
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

Issue #882 combines a Windows installer, secret-bearing connection discovery, preservation-sensitive TOML edits, lifecycle hooks, and a Ring-consumed MCP progress contract. These concerns must share settings, ownership, validation, and reporting while keeping workstation mutations outside `ISettingsRepository.ConfigureEnvironment` and `RemoveEnvironment`.

## Decision

Create a `Clio.Common.DbHub` service boundary with separately testable installer, connection-discovery, TOML-store, HTTP-client, and synchronization interfaces. Persist only dbHub configuration in clio settings; persist source ownership atomically inside delimited managed TOML blocks. Add conditional deploy/uninstall stages and use the generic warning outcome from #881 for best-effort failures.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| Rewrite TOML through an object serializer | Simple semantic editing | Destroys comments/order/unknown formatting and broadens ownership | Rejected |
| Persist ownership only in appsettings | Easy lookup | Cross-file transaction can split ownership from TOML | Rejected |
| Run sync inside settings repository mutations | Automatic everywhere | Adds surprising I/O and can fail unrelated registration | Rejected |
| Surgical managed blocks + parser validation + lifecycle orchestration | Preserves user text, atomic ownership, explicit warnings | More focused code and tests | **Chosen** |
| Logger-only best-effort warning | No protocol change | Ring does not reliably present successful tool output | Rejected |

## Architecture

### Settings

`Settings.DbHub` maps JSON `dbhub` to `DbHubSettings` (`enabled`, `config-path`, `host`, `port`, `sync-local-environments`). `ISettingsRepository.GetDbHubSettings` returns safe defaults; `SetDbHubSettings` uses the existing locked optimistic `UpdateSettings` path. The JSON schema documents constraints.

### TOML ownership and safety

Each source owned by clio is wrapped in unique marker comments containing an encoded environment key and normalized source ID. The store parses all source IDs for conflict detection but replaces/removes only complete clio marker ranges. Candidate text is validated before commit. Mutations acquire an adjacent cross-process lock, refuse unsafe link/reparse targets, write UTF-8 without BOM to a sibling temp file, flush to disk, preserve ACL/Unix mode, then `File.Replace` or atomic move. Temp cleanup is guaranteed.

### Connection discovery

The source factory reads `ConnectionStrings.config` via `XDocument`, selects `dbPostgreSql` or `db`, then parses with `NpgsqlConnectionStringBuilder` or `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`. It emits individual TOML fields so special characters are escaped correctly. Integrated SQL Server authentication returns a safe skip result. Secret-bearing DTOs never implement diagnostic `ToString` output and never enter logs/progress.

### Installation

`IDbHubInstallerService` validates Windows, loopback host, port availability, Node >=22.5/npm, and global npm state. Missing dbHub is installed exactly as `npm install -g @bytebase/dbhub@0.23.0`; existing official installations are adopted. `npm prefix -g` resolves the absolute `dbhub.cmd` shim. A testable scheduled-task manager creates/repairs current-user XML with `LogonTrigger`, `InteractiveToken`, `Hidden=true`, and explicit HTTP/host/port/config arguments. A compatible existing task/config is preserved. Health verification uses `GET /healthz`; MCP verification uses a valid Streamable HTTP POST.

### Synchronization

`IDbHubSynchronizationService` provides reconcile-all, reconcile-one, and exact managed removal. Reconciliation precomputes eligible environments and normalized IDs to prevent collisions before writes. Online verification waits beyond the 500 ms debounce and polls boundedly; offline verification returns a warning while retaining the file update. Results are structured and secret-safe.

### Lifecycle and progress

Deploy manifest adds conditional `sync-dbhub` after `wait-ready`. Uninstall adds conditional `remove-dbhub-source` after destructive cleanup and before `unregister`; path-only uninstall skips it. Integration failures call the generic `WarnStage`, log one warning, and complete via `success-with-warnings`, without a non-zero exit or failed-stage cascade. ClioRing mirrors/renders warning vocabulary and treats both success outcomes as environment-refresh success.

## Files to Create

| File/folder | Purpose |
|-------------|---------|
| `clio/Common/DbHub/*` | Models, interfaces, TOML store, source parser, installer/task/HTTP/sync services |
| `clio/Command/InstallDbHubCommand.cs` | Thin CLI installer command/options |
| `clio/Command/SyncDbHubCommand.cs` | Thin manual reconciliation command/options |
| `clio.tests/Common/DbHub/*` | Unit/integration coverage for parsing, preservation, atomicity, installer, sync |
| `clio.tests/Command/*DbHub*Tests.cs` | DI-resolved command behavior tests |
| command help/docs files | User-facing command documentation |

## Files to Modify

| File | Change |
|------|--------|
| `clio/Environment/ConfigurationOptions.cs`, `ISettingsRepository.cs` | settings model and locked accessors |
| `clio/tpl/jsonschema/schema.json.tpl`, `clio/appsettings.json` | dbHub settings contract/examples |
| `clio/BindingsModule.cs`, `clio/Program.cs` | DI and command dispatch |
| `CreatioInstallerService.cs`, `CreatioUninstaller.cs`, `StageIds.cs` | conditional lifecycle integration |
| existing MCP progress contract/tests/fixtures | warning forwarding and stage coverage |
| Ring IPC/view models/views/tests/fixture | warning and success-with-warnings compatibility |
| command docs/help/index/wiki anchors and deploy/uninstall guidance | complete user contract |

## Key Interfaces

```csharp
public interface IDbHubSynchronizationService
{
    DbHubSyncSummary Synchronize(string? environmentName = null);
    DbHubSyncResult SynchronizeEnvironment(string environmentName);
    DbHubSyncResult RemoveEnvironmentSource(string environmentName);
}

public interface IDbHubTomlStore
{
    DbHubTomlMutationResult Upsert(DbHubManagedSource source);
    DbHubTomlMutationResult Remove(string environmentName);
}

public interface IDbHubInstallerService
{
    DbHubInstallationResult InstallOrRepair(DbHubInstallRequest request);
}
```

Public interfaces and members carry XML documentation. Behavior types are injected and registered through interfaces; data carriers are records.

## CLI Specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `install-dbhub --config-path` | string | No | Explicit TOML path; persisted path wins, then existing user TOML, then clio-home default |
| `install-dbhub --host` | string | No | Loopback host, default `127.0.0.1`; non-loopback rejected |
| `install-dbhub --port` | int | No | HTTP port, default `7999` |
| `install-dbhub --sync-local-environments` | bool | No | Enable automatic deploy/uninstall reconciliation, default true |
| `sync-dbhub --environment` | string | No | Restrict manual reconciliation to one local environment |

## Test Strategy

| Layer | What to cover |
|-------|---------------|
| Unit | settings/defaults, validators, normalization, collision/ownership, parsers, installer orchestration, lifecycle call ordering, secret safety |
| Integration | real temp-file preservation, atomic rollback, locking/concurrency, ACL, process/task XML boundaries |
| MCP E2E | deploy/uninstall warning event, exit/result success, secret-free progress, ordered fixture |
| Local runtime | pinned install/adoption, loopback task, health/MCP, hot reload, disposable Creatio deploy/add/offline-warning/uninstall/remove |
| Ring | warning rendering/receipt/refresh, mirror parity, unknown-field tolerance, ordered replay, NativeAOT publish |

## Consequences

- **Positive**: one ownership and safety path serves install, manual sync, deploy, and uninstall.
- **Positive**: lifecycle success remains reliable and warnings become visible on every surface.
- **Trade-off**: a TOML parser dependency is added only for validation/semantic inspection; serialization remains surgical.
- **Trade-off**: Windows task integration is isolated behind interfaces and validated hermetically plus one local smoke path.
- **Breaking change**: No. Progress vocabulary is additive and schema version remains compatible.

## Pre-implementation Checklist

- [x] PRD and ADR exist before code.
- [x] CLI options are kebab-case.
- [x] Existing command/DI/test patterns identified.
- [x] MCP installer excluded intentionally; deploy/uninstall MCP impact identified.
- [x] Ring consumer paths and NativeAOT gate identified.
- [x] #881 warning-contract dependency recorded.
