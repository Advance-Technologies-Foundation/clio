# ADR: `find-app` fast app discovery (ENG-91275)

- **Status:** Accepted
- **Date:** 2026-06-10
- **Ticket:** [ENG-91275](https://creatio.atlassian.net/browse/ENG-91275) (sub-task of ENG-90506)
- **Driver:** The app-discovery phase of ENG-90506 `run1` took ~18 min. Root cause was a
  cascade: env not registered (unhelpful `Environment '…' not found. Check your clio
  configuration.`), no semantic `find-app`, and an N+1 per-app section scan to locate a
  section by caption.

## Context

clio MCP already exposes `list-apps`, `get-app-info`, `list-app-sections` and, for schemas,
`find-entity-schema`. There is **no** semantic app search. An agent that knows only an
imprecise app name (e.g. "Customer Request Management" → really `CrtCaseManagementApp` /
"Case Management") must `list-apps` and then call `list-app-sections` per app — N+1 MCP
round-trips through a slow wrapper.

Two env-resolution paths coexist:

- `find-entity-schema` and other `BaseTool`-derived tools resolve a per-call command via
  `ToolCommandResolver`, whose env-not-found message already lists available environments.
- `list-apps` / `get-app-info` / `list-app-sections` use startup-injected services
  (`ApplicationListService`, `ApplicationInfoService`, `ApplicationSectionGetListService`)
  that throw the generic `"...not found. Check your clio configuration."`.

This divergence is the **message-level** half of the "split-brain" the ticket flags. The
**process-level** half (a native MCP context and a wrapper context resolving different
settings files) is an environment/bootstrap concern tracked by **ENG-91276** and is out of
scope here.

## Decision

1. **`find-app` mirrors `find-entity-schema`.** A new `FindAppCommand : Command<FindAppOptions>`
   takes `IApplicationClient` + `IServiceUrlBuilder` (resolved **per environment** by
   `ToolCommandResolver`) and is surfaced by `FindAppTool : BaseTool<FindAppOptions>`. This
   keeps the new tool on the env-aware resolver path (not the startup-bound services), so its
   env-not-found message is the actionable one by construction.

2. **Two queries, no N+1.** `FindApplications` issues exactly **two** `SelectQuery` calls per
   environment regardless of app count:
   - all `SysInstalledApp` rows (`Id`, `Code`, `Name`, `Description`, `Version`);
   - all `ApplicationSection` rows (`Id`, `ApplicationId`, `Code`, `Caption`, `Description`,
     `EntitySchemaName`) — **without** an `ApplicationId` filter.

   Sections are joined to apps in memory by `ApplicationId`. The `search-pattern` is matched
   (case-insensitive `Contains`) against app `Name`/`Code`/`Description` **and** each section's
   `Caption`/`Code`. An empty pattern returns every app (with its sections) — the
   "apps + sections in one call" enumeration. An optional exact `code` narrows to one app.

3. **Each match carries its sections.** The result is `app + sections` in a single response,
   removing the round-trips that the agent previously made.

4. **Unified, actionable env-not-found.** A shared `Clio.Common.EnvironmentNotFoundError.Build`
   produces one message used by both env-resolution paths. It lists available environments and
   appends a copy-pasteable
   `clio reg-web-app <name> -u <url> -l <login> -p <password>`. `ToolCommandResolver` and the
   application-family services/commands all route through it, killing the message divergence.

## Alternatives considered

- **Per-app section query (N+1) inside `find-app`.** Rejected: it merely moves N+1 server-side.
  The unfiltered `ApplicationSection` query collapses it to a constant 2 queries.
- **Extend `list-apps` with a pattern.** Rejected: `list-apps` is a thin list and uses the
  startup-bound service; mirroring `find-entity-schema` (resolver path) is more consistent and
  fixes the env-message divergence for free.
- **Reuse `IApplicationListService` / `IApplicationSectionGetListService` from the new command.**
  Rejected for the hot path: those create their own clients and the section service is per-app.
  `FindAppCommand` runs both queries on its single injected `IApplicationClient`.

## Consequences

- New CLI verb `find-app` + MCP tool `find-app`; docs (`help/en`, `docs/commands`, `Commands.md`,
  `Wiki/WikiAnchors.txt`, `docs/McpCapabilityMap.md`) and `clio.mcp.e2e` coverage updated.
- All application-family env-not-found errors become identical and actionable.
- `find-app` reads every `ApplicationSection` row once per call; on environments with very many
  sections this is a single larger query rather than many small ones — a net win for the agent
  loop, acceptable for a read tool.
- Process-level split-brain remains for the existing startup-bound app tools; deferred to
  ENG-91276 and noted in the change summary.
