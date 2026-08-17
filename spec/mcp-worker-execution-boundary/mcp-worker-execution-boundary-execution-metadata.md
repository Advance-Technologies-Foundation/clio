# Inventory 1 — execution metadata per MCP tool

**Feature:** mcp-worker-execution-boundary · **Jira:** ENG-95262 · **Stage:** 0 (design artifact)
**Measured against:** `origin/master` @ `3fc50bf99`, 2026-08-17

This is the input to Stage 1. Stage 1 adds the six reflected fields below to every `[McpServerTool]` and a
catalog coverage test that fails when an enabled canonical tool is unclassified, or when a starter and its
status poller disagree. This document is the **worklist that test consumes**, not the final truth: the
`Location` column is derived by the heuristic in §4 and every row is confirmed or corrected when the
attribute is actually applied. That confirmation is the work of Stage 1 — the inventory's job is to make
sure nothing is missed, and to name up front the rows where the heuristic is known to be weak (§5).

## 1. The six fields

| Field | Values | Decides |
|---|---|---|
| `Location` | `in-process` \| `worker` | whether the call is relayed at all |
| `Lifetime` | `per-call` \| `sticky` \| `n/a` | whether the worker survives the response |
| `OperationFamily` | `none` \| `configuration-build` \| `restart` \| `app-section-create` \| `deploy` | which sticky worker a status poll must reach; which shared reservation applies |
| `BudgetPolicy` | `none` \| `parent-kill (default)` \| `parent-kill (extended)` \| `terminal-stage` | how the parent bounds the call |
| `RequiresClientRequests` | `none` \| `sampling` \| `progress` \| both | whether the relay must be full-duplex for this tool |
| `SharedFileResource` | `none` \| `.clio-pages` \| `browser-session-cache` \| `configuration-build` | which interprocess file gate Stage 9 must install |

## 2. Census

189 `[McpServerTool]` declarations across 125 files; every one carries a resolvable, unique name.

| Safety hint | Count |
|---|---|
| `ReadOnly = true` | 65 |
| `Destructive = true` | 87 |
| **neither — bounded by nothing today** | **37** |

| Proposed classification | Count |
|---|---|
| `Location = worker` | 157 |
| `Location = in-process` | 32 |
| `Lifetime = sticky` | 7 |
| `OperationFamily ≠ none` | 9 |

Current lock behaviour, for comparison: 115 tools reach the per-tenant monitor, 72 take no lock at all, and
2 take the narrow `configuration-build` reservation instead.

**Reproducing the census.** A naive `grep -c '\[McpServerTool'` over-counts by ~16: attributes span multiple
lines, `[McpServerTool]` appears inside XML doc comments, and once inside an exception-message string literal
in `McpToolInvokerRegistry.cs`. The number above comes from a brace-scoped parse with comments and string
literals blanked and per-class `const string` resolution for `Name = ToolName`. Two counts that differ by
sixteen are not a disagreement about the code — they are a disagreement about the parser, so the parser is
stated. The parse used here was ad hoc (a session script, not committed); **Stage 1's coverage test is what
puts this count in the build**, which is the point at which it stops needing to be re-derived by hand.

Against the issue's 2026-08-13 measurement (185 / 63 / 84 / 38 on `82947ba0c`) the catalog grew by 4 tools
over 35 commits. The census reproduces; it is not stale.

## 3. Assignment rules

- **`Location`** — `in-process` only when the tool can never block on a Creatio environment: guidance,
  tool contracts, component/knowledge lookups, telemetry consent, purely local workspace scaffolding.
  Everything that resolves an environment is `worker`.
- **`Lifetime`** — `sticky` for the five long-running starters (`compile-creatio`, `install-process-builder`,
  the two `restart-*` starters, `create-app-section`) plus the two status pollers that must reach the same
  worker (`compile-status`, `restart-status`) — 7 rows across **four** operation families. Everything else
  is `per-call`.
- **`OperationFamily`** — set only where a status poll or a shared reservation needs it. It is the routing
  key that sends `compile-status` to the worker that is running `compile-creatio`.
- **`BudgetPolicy`** — `parent-kill (default)` for ordinary worker calls; `parent-kill (extended)` for
  sticky ones; `terminal-stage` for `deploy-creatio` / `uninstall-creatio`, where ClioRing waits for the
  authoritative terminal stage and a generic kill could leave a half-installed environment (rule 4);
  `none` in-process.
- **`RequiresClientRequests`** — `sampling` where the tool calls `server.SampleAsync`; `progress` where it
  emits `notifications/progress` or stage events. Both mean the relay must be full-duplex for that call.
- **`SharedFileResource`** — the concrete artifact two processes could now corrupt (rule 8).

**Cross-field invariants (enforced by the Stage 1 coverage test, TC-U-108).** The rules above constrain each
column separately, which is how a row can satisfy all six and still be internally impossible — the original
`deploy-creatio` row classified a `deploy`-family tool as `in-process | BudgetPolicy: none`, contradicting
rule 4, this file's own §3 prose and its `uninstall-creatio` sibling. Two invariants make that class of row
fail in the build rather than in review:

- `OperationFamily = deploy` ⇒ `Location = worker` **and** `BudgetPolicy = terminal-stage`.
- `Location = in-process` ⇒ `OperationFamily = none`, `Lifetime = n/a` and `BudgetPolicy = none` — a tool
  that never routes to a worker has no parent budget to expire and no sticky worker for a poll to reach.

## 4. Heuristic used for `Location`, and its limits

`Location` was derived by static signal: does the tool's declaring file resolve an environment
(`EnvironmentOptions`, an `environment-name` argument, or the tenant-key path). The heuristic is **file-level,
so it over-assigns `worker`** — a tool declared in a file that also contains an environment-scoped sibling
inherits the signal. Over-assignment is the safe direction (an unnecessary worker costs 0.7 s; a missed one
keeps the wedge), but it is not free, and the following classes are the ones to re-check by hand in Stage 1:

- purely local scaffolding that happens to accept an environment name (`new-test-project`,
  `new-integration-test-project`, `new-ui-project`, `create-workspace`);
- toolkit/skill management (`install-toolkit`, `update-toolkit`) — local file operations;
- `experimental`, `send-telemetry` — process-local;
- multi-tool files where one tool is environment-scoped and its neighbours are not.

## 5. Rows that carry the design weight

### 5.1 Starter/status pairs — the coverage test's real target

Only **two** operation registries exist: `ICompileOperationRegistry` (`BindingsModule.cs:738`) and
`IRestartOperationRegistry` (`BindingsModule.cs:742`). Three of the four long-running modes have no registry
at all, which is why "reap on terminal status" cannot manage them and workers need a private completion
signal (rule 5).

| Starter | Status poller | Registry | Consequence |
|---|---|---|---|
| `compile-creatio` | `compile-status` | `ICompileOperationRegistry` | sticky worker must serve both; registry is in-process today |
| `restart-by-environment-name` | `restart-status` | `IRestartOperationRegistry` | same |
| `restart-by-credentials` | — (deliberately unreportable) | none | no status path exists to reap on |
| `install-process-builder` | — | **none** | private completion signal required |
| `create-app-section` | — | **none** | private completion signal required (OQ-4: whether it gets a real registry) |

A starter and its poller **must** agree on `OperationFamily` and `Lifetime`. That disagreement is exactly
what the Stage 1 coverage test is for.

### 5.1b Deprecated aliases are separate rows, and must not drift from their canonical

A deprecated tool name is registered as its **own** `[McpServerTool]` method that delegates to the
canonical one — not as catalog metadata. `StopTool.cs` declares both `stop-all-creatio` (`:34`) and the
PascalCase `StopAllCreatio` (`:52`), the second marked *"[Deprecated: use stop-all-creatio]"* and
implemented as `=> StopAllCreatio(requestContext)`.

Consequence for Stage 1: an alias and its canonical execute the **same code** and must therefore carry
**identical** execution metadata. If they diverge, one name routes to a worker and the other runs
in-process — the same failure shape as a starter/status disagreement, and it belongs in the same coverage
test. (`StopAllCreatio` is also why the table below is not uniformly kebab-case: it is a real, deliberate
legacy name, not a parse artifact.)

### 5.2 Full-duplex requirement

- **Sampling — exactly two callers:** `update-page` and `sync-pages`, both via `PageBodySamplingService`
  (`PageBodySamplingService.cs:130`). A relay that is not full-duplex degrades these to `Skipped=true`
  silently — no error, just a quietly worse answer (rule 1).
- **Progress / stage events — 14 tools:** `compile-creatio`, `create-app`, `create-app-section`,
  `delete-app-section`, `deploy-creatio`, `get-app-info`, `install-process-builder`, `list-app-sections`,
  `list-apps`, `restart-by-credentials`, `restart-by-environment-name`, `sync-schemas`, `uninstall-creatio`,
  `update-app-section`. These are the tools whose ordering guarantee rule 12 protects.

### 5.3 Shared file resources (Stage 9 gates)

| Resource | Tools | Hazard |
|---|---|---|
| `.clio-pages/{schema}/meta.json` | `get-page`, `update-page`, `sync-pages` | read-modify-write with swallowed I/O failures; two processes now race it |
| browser-session cache | `get-browser-session` | shared under the clio home directory |
| `configuration-build` reservation | `compile-creatio`, `install-process-builder` | in-process today; Stage 7 moves it to the parent, keyed by normalised tenant + resource |
| DbHub | — | already cross-process safe (`.clio.lock`, `FileShare.None`) — no work needed |

### 5.4 The 37 tools bounded by nothing

Neither `ReadOnly` nor `Destructive`, so `McpReadDeadlineGate` admits none of them — except `get-page`,
which is admitted by a one-name whitelist. Of these 37, **28 are environment-scoped and take the per-tenant
monitor**, which is the exact combination that produced the 1800 s `clio-run get-schema` call.

`add-package`, `add-package-dependency`, `build-theme`, `clear-themes-cache`, `create-business-process`,
`create-client-unit-schema`, `create-schema`, `create-sql-schema`, `create-theme`, `create-user-task`,
`create-workspace`, `disable-knowledge-source`, `download-configuration-by-build`,
`download-configuration-by-environment`, `enable-knowledge-source`, `experimental`, `finish-hotfix`,
`generate-source-code`, `get-browser-session`, `get-classic-page-sources`, `get-client-unit-schema`,
`get-identity-assertion`, `get-page`, `get-schema`, `get-sql-schema`, `install-gate`, `install-toolkit`,
`new-integration-test-project`, `new-test-project`, `new-ui-project`, `odata-create`, `reg-web-app`,
`send-telemetry`, `start-creatio`, `unlock-for-hotfix`, `update-toolkit`, `upload-image`.

Note that this list is **not** the same as "unsafe": several are local-only. It is the list of tools for
which nothing today decides a bound — which is why the metadata must be explicit rather than inferred from
the safety hints (rule 7).

### 5.5 Feature-toggled tools

Workers inherit the parent's **frozen** enabled-tool generation (rule 11), so these must be resolved once at
parent startup and passed down, never re-read in the child.

| Toggle | Tools |
|---|---|
| `deploy-identity` | 6 |
| `process-designer` | 6 |
| `mobile-page-converter` | 1 |
| `watch-compilation` | 1 |

### 5.6 Residency is not execution

17 tools are resident in `tools/list` (`McpCoreToolProfile` plus the three lazy-mode entry points). Residency
and execution location are orthogonal: `get-page` is resident **and** must run in a worker, while most
long-running tools are non-resident and are reached as
`clio-run {"command": "compile-creatio", …}`. The router must therefore resolve its key **after unwrapping
`clio-run`** (rule 7) — routing on the outer name would send every long-running call to the same place.

## 6. Full assignment — all 189 tools

`*` marks a tool resident in `tools/list`. A backticked toggle name marks a feature-gated tool.

| tool | hint | Location | Lifetime | OperationFamily | BudgetPolicy | RequiresClientRequests | SharedFileResource |
|---|---|---|---|---|---|---|---|
| `StopAllCreatio` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `add-data-binding-row` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `add-item-model` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `add-knowledge-source` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `add-package` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `add-package-dependency` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `advise-theme-palette` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `assert-infrastructure` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `build-theme` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `check-auth-code-flow` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `check-settings-health` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `check-theming-access` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `clear-browser-session` | Destructive | worker | per-call | none | parent-kill (default) | none | browser-session-cache |
| `clear-redis-db-by-credentials` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `clear-redis-db-by-environment` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `clear-themes-cache` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `clio-run`* | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `clio-run-destructive`* | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `compile-creatio` | Destructive | worker | sticky | configuration-build | parent-kill (extended) | progress | configuration-build |
| `compile-status` | ReadOnly | worker | sticky | configuration-build | parent-kill (extended) | none | configuration-build |
| `configure-knowledge-feedback-policy` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `create-app` | Destructive | worker | per-call | none | parent-kill (default) | progress | none |
| `create-app-section` | Destructive | worker | sticky | app-section-create | parent-kill (extended) | progress | none |
| `create-business-process` `process-designer` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-client-unit-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-data-binding` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-data-binding-db` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-entity-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-entity-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-lookup` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-oauth-technical-user` `deploy-identity` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-page` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-page-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-related-page-addon` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-server-to-server-oauth-app` `deploy-identity` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-sql-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-sys-setting` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `create-theme` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-user-task` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `create-workspace` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `dataforge-context` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-find-lookups` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-find-tables` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-get-relations` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-get-table-columns` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-initialize` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-status` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `dataforge-update` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-app` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-app-section` | Destructive | worker | per-call | none | parent-kill (default) | progress | none |
| `delete-entity-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-knowledge` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `delete-page-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-theme` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `delete-toolkit` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `deploy-creatio` | Destructive | worker | per-call | deploy | terminal-stage | progress | none |
| `deploy-identity` `deploy-identity` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `describe-business-process` `process-designer` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `describe-environment` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `disable-knowledge-source` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `download-configuration-by-build` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `download-configuration-by-environment` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `enable-knowledge-source` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `execute-esq` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `experimental` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `find-app`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `find-empty-iis-port` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `find-entity-schema`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `finish-hotfix` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `generate-process-model` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `generate-source-code` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-app-info`* | ReadOnly | worker | per-call | none | parent-kill (default) | progress | none |
| `get-browser-session` | **unbounded** | worker | per-call | none | parent-kill (default) | none | browser-session-cache |
| `get-classic-page-sources` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-client-unit-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-component-info`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-entity-schema-column-properties`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-entity-schema-properties`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-fsm-mode` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-guidance`* | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `get-identity-assertion` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-identity-public-jwk` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-identity-service-config` `deploy-identity` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-knowledge-feedback-policy` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `get-mobile-page-conversion-guide` `mobile-page-converter` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-page`* | **unbounded** | worker | per-call | none | parent-kill (default) | none | .clio-pages |
| `get-page-hierarchy` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-process-signature` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-record-rights` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-related-page-addon` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-request-info`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-schema-name-prefix` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-sql-schema` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `get-sys-setting` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-target-package` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-telemetry-consent` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `get-tool-contract`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `get-user-culture` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `info-knowledge` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `install-application` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `install-gate` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `install-knowledge` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `install-process-builder` `process-designer` | Destructive | worker | sticky | configuration-build | parent-kill (extended) | progress | configuration-build |
| `install-sql-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `install-toolkit` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `link-from-repository-by-env-package-path` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `link-from-repository-by-environment` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `link-from-repository-unlocked` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `list-app-sections`* | ReadOnly | worker | per-call | none | parent-kill (default) | progress | none |
| `list-apps`* | ReadOnly | worker | per-call | none | parent-kill (default) | progress | none |
| `list-creatio-builds` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `list-entity-client-schemas` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-environments` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `list-knowledge-examples` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `list-knowledge-sources` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `list-packages`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-page-templates` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-pages`* | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-printables` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-sys-settings` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-themes` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `list-user-tasks` `process-designer` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `modify-business-process` `process-designer` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `modify-entity-schema-column` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `modify-user-task-parameters` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `new-integration-test-project` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `new-test-project` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `new-ui-project` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `odata-create` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `odata-delete` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `odata-read` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `odata-update` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `pkg-to-db` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `pkg-to-file-system` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `push-workspace` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `read-data-binding-db` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `read-entity-business-rules` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `read-page-business-rules` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `reg-web-app` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `regenerate-identity-signing-key` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `remove-data-binding-row` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `remove-data-binding-row-db` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `remove-knowledge-source` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `remove-package-dependency` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `resolve-oauth-system-user` `deploy-identity` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `restart-by-credentials` | Destructive | worker | sticky | restart | parent-kill (extended) | progress | none |
| `restart-by-environment-name` | Destructive | worker | sticky | restart | parent-kill (extended) | progress | none |
| `restart-status` | ReadOnly | worker | sticky | restart | parent-kill (extended) | none | none |
| `restore-db-by-credentials` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `restore-db-by-environment` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `restore-db-to-local-server` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `restore-workspace` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `send-telemetry` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `set-background-image` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `set-entity-schema-properties` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `set-fsm-mode` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `set-logo` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `set-record-rights` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `set-user-theme` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `show-passing-infrastructure` | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `start-creatio` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `stop-all-creatio` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `stop-creatio` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `sync-pages` | Destructive | worker | per-call | none | parent-kill (default) | sampling | .clio-pages |
| `sync-schemas` | Destructive | worker | per-call | none | parent-kill (default) | progress | none |
| `uninstall-creatio` | Destructive | worker | per-call | deploy | terminal-stage | progress | none |
| `unlock-for-hotfix` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `update-app-section` | Destructive | worker | per-call | none | parent-kill (default) | progress | none |
| `update-client-unit-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-entity-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-entity-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-knowledge` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `update-page` | Destructive | worker | per-call | none | parent-kill (default) | sampling | .clio-pages |
| `update-page-business-rules` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-sql-schema` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-sys-setting` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-theme` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `update-toolkit` | **unbounded** | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `upload-image` | **unbounded** | worker | per-call | none | parent-kill (default) | none | none |
| `upsert-data-binding-row-db` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `validate-page`* | ReadOnly | in-process | n/a | none | none (never blocks on Creatio) | none | none |
| `validate-process-graph` `process-designer` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `verify-oauth-app` `deploy-identity` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `watch-compilation` `watch-compilation` | ReadOnly | worker | per-call | none | parent-kill (default) | none | none |
| `withdraw-telemetry-consent` | Destructive | in-process | n/a | none | none (never blocks on Creatio) | none | none |

---

Generated from `origin/master` @ `3fc50bf99`. Regenerate after any change to the tool catalog; the Stage 1
coverage test is the mechanism that makes a stale row fail loudly instead of silently.
