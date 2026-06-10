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

2. **Single MCP round-trip; sections loaded per application.** `FindApplications` loads all
   applications with one `SysInstalledApp` query, then loads each application's sections with a
   per-application `ApplicationSection` query filtered by `ApplicationId`.

   > Live testing on a real stand revealed that `ApplicationSection` returns **zero rows** for an
   > unfiltered `SelectQuery` — it only yields data when filtered by `ApplicationId`, which is
   > exactly why `list-app-sections` filters server-side. The original "fetch all sections in one
   > unfiltered query and join in memory" plan does not work; sections must be read per application.

   So the internal query count is `1 + N` (N = application count), or `1 + 1` when an exact `code`
   is supplied (other applications are skipped). The `search-pattern` is matched (case-insensitive
   `Contains`) against app `Name`/`Code`/`Description` **and** each section's `Caption`/`Code`. An
   empty pattern returns every app (with its sections) — the "apps + sections in one call"
   enumeration. The N+1 the ticket targets — the *agent* calling `list-apps` and then
   `list-app-sections` per app over the slow MCP wrapper — is removed: the agent makes **one**
   `find-app` call and clio performs the whole sweep in-process.

3. **Each match carries its sections.** The result is `app + sections` in a single response,
   removing the round-trips that the agent previously made.

4. **Unified, actionable env-not-found.** A shared `Clio.Common.EnvironmentNotFoundError.Build`
   produces one message used by both env-resolution paths. It lists available environments and
   appends a copy-pasteable
   `clio reg-web-app <name> -u <url> -l <login> -p <password>`. `ToolCommandResolver` and the
   application-family services/commands all route through it, killing the message divergence.

## Alternatives considered

- **Single unfiltered `ApplicationSection` query for all sections, joined in memory.** Attempted
  first and rejected: `ApplicationSection` returns zero rows without an `ApplicationId` filter
  (confirmed on a live stand), so sections must be read per application.
- **An `IN (id1, id2, …)` filter to fetch all sections in one query.** Possible but needs a custom
  multi-value filter wire shape; deferred. The per-app query mirrors the proven `list-app-sections`
  path and still keeps everything inside one tool call.
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
- `find-app` issues `1 + N` DataService queries (N = application count) for an enumerate/pattern
  call, or `1 + 1` for an exact-code call — all inside a single tool call, so the agent still makes
  one round-trip. On environments with very many applications this is several small queries; an
  `IN`-filter optimization to collapse the section reads into one query is a possible follow-up.
- Process-level split-brain remains for the existing startup-bound app tools; deferred to
  ENG-91276 and noted in the change summary.
