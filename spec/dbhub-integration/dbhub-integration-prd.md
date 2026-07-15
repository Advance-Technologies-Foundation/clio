# PRD: dbHub Installation and Creatio Source Synchronization

**Status**: Approved
**Author**: PM Agent (autonomous mode)
**Created**: 2026-07-15
**GitHub issue**: [#882](https://github.com/Advance-Technologies-Foundation/clio/issues/882)

---

## Problem Statement

Windows developers currently install and operate dbHub by hand, then manually copy database credentials from locally deployed Creatio instances into a shared TOML file. The process is easy to misconfigure, can expose an unauthenticated HTTP service beyond loopback, and drifts whenever clio deploys or removes a local environment.

## Goals

- [ ] Install or adopt one pinned dbHub HTTP server idempotently. **SM-01**: repeated installation produces the same healthy task/configuration without reinstalling the npm package. **Counter**: no unpinned startup-time download.
- [ ] Reconcile eligible local Creatio databases without damaging user-maintained TOML. **SM-02**: all eligible environments have exactly one clio-owned source after sync. **Counter**: manual sources, comments, unknown fields, and custom tools remain byte-for-byte unchanged outside clio blocks.
- [ ] Keep deploy and uninstall honest across CLI, MCP, and ClioRing. **SM-03**: dbHub failures produce a visible warning and exit/result success. **Counter**: no successful primary lifecycle operation becomes failed because dbHub is unavailable.
- [ ] Keep secrets out of all observable output. **SM-04**: password/DSN sentinel tests find zero occurrences in logs, MCP results, progress, and errors.

## Non-goals

- Will NOT expose workstation installation as an MCP tool without an authorization model.
- Will NOT synchronize cloud environments registered only by URL/credentials.
- Will NOT restart dbHub after TOML updates; HTTP hot reload is authoritative.
- Will NOT support SQL Server integrated Windows authentication that dbHub cannot use.
- Will NOT rewrite or normalize the user's complete TOML document.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| Windows developer | `clio install-dbhub` to install or repair a loopback dbHub task | I get a durable local MCP server without maintaining startup scripts |
| Creatio developer | `clio sync-dbhub` to reconcile local environments | my database tools match clio's local environment catalog |
| deploy/uninstall user | automatic best-effort source changes | successful lifecycle operations leave dbHub consistent without becoming fragile |
| ClioRing user | typed warning progress | I can distinguish success from success with follow-up work |

## Feature Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Persist additive `dbhub` settings for enabled state, explicit config path, loopback host, port, and automatic-sync toggle | Must |
| FR-02 | Add Windows-first `install-dbhub` with actionable Node >=22.5/npm prerequisite checks and an exact pinned `@bytebase/dbhub` install | Must |
| FR-03 | Create/adopt/repair a hidden current-user logon Scheduled Task that runs the absolute npm shim with HTTP transport and explicit loopback/config/port arguments | Must |
| FR-04 | Preserve a valid existing TOML and task where compatible; repair unsafe binding, stale launcher, missing executable, and mismatched config/port without replacing user sources | Must |
| FR-05 | Verify port availability, `GET /healthz`, and the HTTP MCP endpoint; report partial installation failures with safe actionable messages | Must |
| FR-06 | Add `sync-dbhub` and a dedicated synchronization service; repository environment mutations themselves remain side-effect free | Must |
| FR-07 | Discover only environments with `EnvironmentPath` and readable `ConnectionStrings.config`; use that file as authoritative | Must |
| FR-08 | Support PostgreSQL and SQL Server password authentication; warn/skip unsupported SQL Server integrated authentication | Must |
| FR-09 | Generate lowercase `[a-z0-9_]` source IDs, detect normalization collisions, and persist exact ownership in clio-delimited TOML metadata | Must |
| FR-10 | Add/update/remove only clio-managed source blocks; preserve manual sources, comments, ordering, custom tools, and unknown fields | Must |
| FR-11 | Serialize mutations, write atomically, preserve ACL/mode, reject unsafe links, validate the candidate TOML before replacement, and retain the original on failure | Must |
| FR-12 | Make identical reconciliation a no-op and refuse to overwrite an unowned conflicting source | Must |
| FR-13 | After successful deploy readiness, add/update the source; failed deployment never adds it | Must |
| FR-14 | After successful destructive uninstall cleanup and before unregister, remove the exact managed source; earlier failure retains it | Must |
| FR-15 | Automatic dbHub failure is best effort: one safe warning, CLI exit 0, MCP `IsError=false`, warning stage, and terminal `success-with-warnings` | Must |
| FR-16 | Wait for the documented 500 ms hot-reload debounce and verify via HTTP when online; keep a valid TOML update and warn that live verification was skipped when offline | Must |
| FR-17 | Redact secrets from every log, result, progress event, exception, and test diagnostic | Must |
| FR-18 | Keep command help, detailed docs, settings schema, indexes, MCP guidance, contract fixtures, and ClioRing presentation aligned | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New command | `install-dbhub --config-path <path> --host 127.0.0.1 --port 7999 --sync-local-environments` | No |
| New command | `sync-dbhub [--environment <name>]` | No |
| Existing deploy/uninstall | conditional dbHub stages and additive warning outcome | No; additive typed vocabulary |

All option names are kebab-case. `install-dbhub` is CLI-only. A dedicated `sync-dbhub` MCP tool is also excluded initially because it mutates a workstation file; existing deploy/uninstall MCP surfaces still carry automatic synchronization warnings.

## Acceptance Criteria

- [ ] **AC-01**: Given absent dbHub settings, when settings load, then dbHub is disabled without a hard-coded user path; given explicit settings, when saved/reloaded, then every value round-trips.
- [ ] **AC-02**: Given Node/npm are missing or too old, when install runs, then it exits non-zero with actionable version guidance and performs no partial task creation.
- [ ] **AC-03**: Given no dbHub installation, when install runs, then exact package version `0.23.0` is installed once, a valid TOML exists, and a hidden current-user logon task binds `127.0.0.1` on the configured port.
- [ ] **AC-04**: Given an existing compatible installation/config/task, when install repeats, then it adopts/no-ops; given repairable drift, then only owned launcher/task state is repaired and user TOML remains intact.
- [ ] **AC-05**: Given a conflicting port or failed health/MCP verification, when install runs, then it identifies the failed step without exposing secrets and exits non-zero.
- [ ] **AC-06**: Given eligible PostgreSQL and SQL Server local environments, when sync runs, then correct managed sources are created from `ConnectionStrings.config`; remote/missing/unsupported environments are warned/skipped.
- [ ] **AC-07**: Given colliding normalized names or an unowned source ID, when sync runs, then clio warns/refuses that source and overwrites nothing.
- [ ] **AC-08**: Given manual TOML content and unknown fields, when a managed source is added, changed, or removed, then non-clio content is preserved and an identical change is a no-op.
- [ ] **AC-09**: Given concurrent writers or candidate validation/write failure, when sync runs, then updates serialize and the original file/ACL remains intact.
- [ ] **AC-10**: Given a successful local deployment, when readiness succeeds, then automatic sync runs afterward; given any earlier failure, then no source is added.
- [ ] **AC-11**: Given successful uninstall cleanup, when files/database are removed, then the exact owned source is removed before environment unregister; given earlier cleanup failure, then the source remains.
- [ ] **AC-12**: Given dbHub integration failure during successful deploy/uninstall, when the command completes, then CLI/MCP/Ring show one warning, exit/result remain successful, and typed progress ends `success-with-warnings`.
- [ ] **AC-13**: Given dbHub is online, when TOML changes, then clio waits through debounce and verifies the source through HTTP without restart; given it is offline, then the valid file remains and skipped live verification is reported.
- [ ] **AC-14**: Given sentinel credentials, when every success/failure path is observed, then no output, progress, log, or error contains the password or full connection string.
- [ ] **AC-15**: Given the finished change, when documentation and compatibility gates run, then both command doc sets/indexes, MCP E2E, Ring tests, mirrored fixtures, and Windows x64 NativeAOT publish are current and green.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|------------|---------------|
| A-01 | Exact npm version `0.23.0` is the shipping pin for this PR | Requires an intentional version bump before release |
| A-02 | Existing `~/dbhub.toml` is adopted only when no persisted path exists; otherwise clio defaults under its own home | A different discovery priority could surprise existing users |
| A-03 | Clio-managed marker comments are authoritative ownership metadata | Manual deletion of markers intentionally relinquishes ownership |
| A-04 | Additive warning vocabulary from #881 is merged before final lifecycle integration | Rebase is required if #881 changes the generic API |

## Open Questions

None. The issue, official dbHub contract, repository patterns, and autonomous-mode assumptions are sufficient to implement narrowly.

## Dependencies

- Depends on: generic typed `warning` / `success-with-warnings` vocabulary introduced by #881; implementation may proceed independently and rebase before shared progress changes.
- Blocks: reliable local database access for future clio/dbHub workflows.
