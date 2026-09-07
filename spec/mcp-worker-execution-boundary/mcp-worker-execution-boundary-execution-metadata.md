# Inventory 1 — execution metadata per MCP tool

**Feature:** mcp-worker-execution-boundary · **Jira:** ENG-95262 · **Stage:** 1 (annotation landed)
**Measured against:** `origin/master` @ `3fc50bf99`, 2026-08-17
**Reconciled against shipped annotations:** 2026-08-17 — see "Status" below

## Status: confirmed, no longer proposed

This document started as the Stage-0 **worklist**: the `Location` column was derived by the file-level
heuristic in §4 and every row was to be confirmed or corrected when the attribute was actually applied.
**That has now happened.** All 189 `[McpServerTool]` declarations carry an explicit `[McpToolExecution]`
with all six fields, each one decided by reading what the method does rather than by the heuristic, and
`McpToolExecutionMetadataCoverageTests` asserts the result with an **empty** `NotYetClassifiedTools` gate
(20/20 green). Every table below now records the **shipped, confirmed** value.

Nine rows were corrected against the heuristic during annotation; they are marked **(corrected)** in §6 with
the reason, and the aggregate effect is in §2. The heuristic itself is retired — see §4.

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

189 `[McpServerTool]` declarations across **122** files; every one carries a resolvable, unique name, and
every one now carries an `[McpToolExecution]` with all six fields.

(The Stage-0 draft said 125 files. Re-measured while reconciling: 123 files under
`clio/Command/McpServer/` contain the text `McpServerTool(`, and one of those —
`McpToolInvokerRegistry.cs` — only mentions it in a doc comment and an exception message, so 122 files
actually declare tools. The declaration count of 189 is unaffected and reproduces exactly.)

| Safety hint | Count |
|---|---|
| `ReadOnly = true` | 65 |
| `Destructive = true` | 87 |
| **neither — bounded by nothing today** | **37** |

| Confirmed classification (shipped) | Count | Stage-0 heuristic proposed |
|---|---|---|
| `Location = worker` | **153** | 157 |
| `Location = in-process` | **36** | 32 |
| `Lifetime = sticky` | **7** | 7 |
| `OperationFamily ≠ none` | **9** | 9 |

`BudgetPolicy` follows `Location` and `Lifetime` exactly: it is `none` on all 36 in-process rows,
`parent-kill (extended)` on all 7 sticky rows, `terminal-stage` on the 2 `deploy`-family rows and
`parent-kill (default)` on the remaining 144. `RequiresClientRequests` is `progress` on 15 rows and
`sampling` on 2 (§5.2); `SharedFileResource` is non-`none` on 8 rows (§5.3).

The worker count moved by −4 net, from **six** `Location` corrections in the same direction (five tools that
turned out never to resolve an environment: `add-data-binding-row`, `remove-data-binding-row`,
`get-tool-contract`, `new-test-project`, `new-integration-test-project`) minus one in the other
(`new-ui-project`, which does). The remaining three corrections are `RequiresClientRequests` only and do not
touch these counts. All nine are listed in §6.

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

## 4. Heuristic used for `Location` — SUPERSEDED by per-tool review

**This section is history.** The `Location` column is no longer heuristic output: every one of the 189 rows
was decided by reading the tool method and the command behind it, and the shipped attribute is the authority.
The heuristic is recorded here only because it explains the Stage-0 numbers in §2 and because its predicted
weak spots turned out to be exactly where the corrections landed.

The heuristic was a static signal: does the tool's declaring file resolve an environment
(`EnvironmentOptions`, an `environment-name` argument, or the tenant-key path). It was **file-level, so it
over-assigned `worker`** — a tool declared in a file that also contained an environment-scoped sibling
inherited the signal. Over-assignment was the safe direction (an unnecessary worker costs 0.7 s; a missed one
keeps the wedge). The classes flagged as needing hand review, and what review found:

| Flagged class | Outcome of per-tool review |
|---|---|
| local scaffolding that merely accepts an environment name (`new-test-project`, `new-integration-test-project`, `new-ui-project`, `create-workspace`) | **Split.** `new-test-project` / `new-integration-test-project` corrected to `in-process` (their options do not derive from `EnvironmentOptions`, no `IToolCommandResolver`, output is generated locally); `create-workspace` confirmed `in-process`; `new-ui-project` corrected the OTHER way, to `worker` — `UiProjectCreator.Create` calls `FindExistingPackage` unconditionally, which reaches `SelectQueryHelper.ExecuteSelectQuery` with the default `Timeout.Infinite`. |
| toolkit/skill management (`install-toolkit`, `update-toolkit`) | Confirmed `in-process` — they install or update agent plugin files and never resolve an environment. They do block on the network, which is a budget gap, not a location error; see §5.4. |
| `experimental`, `send-telemetry` | Confirmed `in-process`. `send-telemetry` additionally must not move: ADR rule 11 forbids a worker running the host's telemetry drain. |
| multi-tool files where one tool is environment-scoped and its neighbours are not | **This is where the heuristic actually failed.** `DataBindingTool.cs` (`add-data-binding-row` / `remove-data-binding-row` corrected to `in-process`, inheriting the signal from their `create-data-binding` sibling) and `ToolContractGetTool` (`get-tool-contract`, a pure contract lookup). |

Three rows were confirmed rather than corrected, but on grounds *different* from the heuristic's, and the
reasons are load-bearing later:

- `download-configuration-by-build` stayed `worker` even though it never resolves an environment (it
  short-circuits to `DownloadFromPath` whenever `BuildZipPath` is set). It pins the process-wide working
  directory under `McpToolExecutionLock.CwdLock`. ADR §2.3 deletes `CwdLock` at Stage 10, which is only sound
  once every cwd mutator runs in its own child — so classifying this one `in-process` would either block that
  deletion or reintroduce the race with the other cwd writers.
- `assert-infrastructure` and `find-empty-iis-port` stayed `in-process` even though they block on Kubernetes /
  local database / Redis probes: §3 keys `Location` on blocking *on a Creatio environment*, and neither does.
  Recorded here in case the rule's intent is ever read as "can block on anything remote", which would move
  both rows.

## 5. Rows that carry the design weight

### 5.1 Starter/status pairs — the coverage test's real target

Only **two** operation registries exist: `ICompileOperationRegistry` (`BindingsModule.cs:756`) and
`IRestartOperationRegistry` (`BindingsModule.cs:760`). Three of the four long-running modes have no registry
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
canonical one — not as catalog metadata. `StopTool.cs` declares both `stop-all-creatio` (`:48`) and the
PascalCase `StopAllCreatio` (`:74`), the second marked *"[Deprecated: use stop-all-creatio]"* and
implemented as `=> StopAllCreatio(requestContext)`.

Consequence for Stage 1: an alias and its canonical execute the **same code** and must therefore carry
**identical** execution metadata. If they diverge, one name routes to a worker and the other runs
in-process — the same failure shape as a starter/status disagreement, and it belongs in the same coverage
test. (`StopAllCreatio` is also why the table below is not uniformly kebab-case: it is a real, deliberate
legacy name, not a parse artifact.)

**Shipped state — two method-level alias pairs, both machine-readable.** The annotation used the attribute's
`AliasOf` property, so the coverage test discovers them by reflection instead of a pinned literal:

| Alias | Canonical | Declared at |
|---|---|---|
| `StopAllCreatio` | `stop-all-creatio` | `StopTool.cs` — `StopAllCreatioLegacy` is `=> StopAllCreatio(requestContext)` |
| `clio-run-destructive` | `clio-run` | `ClioRunTool.cs` — `ClioRunDestructiveTool` is documented as a deprecated alias; `destructiveSurface: true` is retained on the executor signature for back-compat but no longer routes or refuses, so both names run one body |

Both pairs are verified identical on all six routing fields by TC-U-107, which also checks the
compatibility-catalog aliases through the reader. The Stage-0 draft named only the first pair; the second was
found while annotating and did not exist as a checked invariant before.

### 5.2 Full-duplex requirement

- **Sampling — exactly two callers:** `update-page` and `sync-pages`, both via `PageBodySamplingService`
  (`PageBodySamplingService.cs:130`). A relay that is not full-duplex degrades these to `Skipped=true`
  silently — no error, just a quietly worse answer (rule 1). **Confirmed unchanged** by per-tool review.
- **Progress / stage events — 15 tools** (corrected from the Stage-0 count of 14): `compile-creatio`,
  `create-app`, `create-app-section`, `delete-app-section`, `deploy-creatio`, `get-app-info`,
  `install-process-builder`, `list-app-sections`, `restart-by-credentials`, `restart-by-environment-name`,
  `start-creatio`, `stop-creatio`, `sync-schemas`, `uninstall-creatio`, `update-app-section`. These are the
  tools whose ordering guarantee rule 12 protects.

  Three corrections, all from reading the emit sites rather than counting one mechanism:

  - **`start-creatio` and `stop-creatio` added.** Both attach `StatusChanged` on the resolved command and
    forward each event to `server.SendNotificationAsync("notifications/progress", …)` on the caller's progress
    token (`StartTool.cs`, `StopTool.cs` `OnStatusChanged`). The Stage-0 census counted
    `McpProgressHeartbeat` callers and therefore missed the tools that call `SendNotificationAsync` directly.
    `StopCommand` raises four stage markers, so a half-duplex relay would drop all four.
  - **`list-apps` removed.** Unlike every other tool in `ApplicationTool.cs` it takes no `McpServer` /
    `RequestContext` parameter and runs no heartbeat, so it has no channel to emit progress on at all
    (`ApplicationTool.cs:38-46` carries this reasoning in code). Its `stop-all-creatio`-style sibling
    proximity is what put it on the Stage-0 list.

  Note that `stop-creatio` and `stop-all-creatio` legitimately DIFFER on this field: the first passes a
  `configureCommand` callback that attaches the handler, the second calls `InternalExecute(options)` with no
  callback and therefore attaches nothing.

### 5.3 Shared file resources (Stage 9 gates)

Eight rows carry a non-`none` `SharedFileResource`; the table below is the shipped set.

| Resource | Tools | Hazard |
|---|---|---|
| `.clio-pages/{schema}/meta.json` | `get-page`, `update-page`, `sync-pages` | read-modify-write with swallowed I/O failures; two processes now race it |
| browser-session cache | `get-browser-session`, `clear-browser-session` | shared under the clio home directory. `clear-browser-session` was missing from this row in the Stage-0 draft while §6 already carried it; the shipped annotation follows §6 |
| `configuration-build` reservation | `compile-creatio`, `install-process-builder`, `compile-status` | in-process today; Stage 7 moves it to the parent, keyed by normalised tenant + resource. `compile-status` takes no reservation itself, but carries the tag because under the worker model the registry it reads lives inside the process that holds the reservation |
| DbHub | — | already cross-process safe (`.clio.lock`, `FileShare.None`) — no work needed |

**Two gaps the enum cannot express today** (found during annotation, deliberately NOT invented as values —
these are Stage 9 decisions):

- **Workspace data-binding files.** `create-data-binding` (worker) and the now-in-process
  `add-data-binding-row` / `remove-data-binding-row` read-modify-write the SAME local artifacts — the
  package's `Data/<binding>/descriptor.json`, `data.json` and localization files. After Stage 6 two processes
  can interleave on them. All three rows carry `SharedFileResource = none` because
  `McpToolSharedFileResource` has no member for workspace binding files.
- **The classic-migration manifest.** `get-classic-page-sources` writes
  `.clio-migration/<schema>/manifest.json`, and `get-schema` optionally writes an output file. Both carry
  `none` for the same reason. The migration manifest is produced by exactly one tool (unlike `.clio-pages`,
  which three tools race), so `none` is defensible — but it is an absence of an enum member, not a reviewed
  judgement that the file is safe.

If Stage 9 wants either gated, the enum needs a new member and the affected rows need re-annotating.

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

**Confirmed unchanged** by the shipped annotations: the live set derived from the registry
(`!IsReadOnly && !IsDestructive`) is exactly these 37 names, and the coverage test derives it that way rather
than pinning the list, so it cannot go stale.

**What the annotation revealed about 9 of them.** These nine landed `in-process`, which by the §3
cross-field invariant forces `BudgetPolicy = none`: `create-workspace`, `disable-knowledge-source`,
`enable-knowledge-source`, `experimental`, `install-toolkit`, `new-integration-test-project`,
`new-test-project`, `send-telemetry`, `update-toolkit`. Their `Location` is right — none of them can block on
a Creatio environment — but the consequence is that **Stage 7 will not bound them either**, and two of them
(`install-toolkit`, `update-toolkit`) do perform network work: a git / marketplace install for up to four
coding agents. So the "bounded by nothing" cohort does not shrink to zero when the worker boundary lands; it
shrinks to these nine, and closing them needs a mechanism that is not the parent kill (there is no child to
kill). That is a Stage 7 input, not a metadata error.

**One worker row that will want a wide budget:** `watch-compilation` is `parent-kill (default)` per the §3
rule, but `WatchCompilationCommand` polls Creatio's `CompilationHistory` in a one-second loop until it settles
or `give-up-after-seconds` expires — default 300 s, caller-settable with no upper cap. It is deliberately NOT
`configuration-build` family: it observes compilations started OUTSIDE clio and never consults
`ICompileOperationRegistry`, so it has no sticky worker to reach and `per-call` is correct. Whoever sizes the
default budget in Stage 7 needs this row.

### 5.5 Feature-toggled tools

Workers inherit the parent's **frozen** enabled-tool generation (rule 11), so these must be resolved once at
parent startup and passed down, never re-read in the child.

| Toggle | Tools |
|---|---|
| `deploy-identity` | 6 |
| `process-designer` | 6 |
| `mobile-page-converter` | 1 |
| `watch-compilation` | 1 |

**Confirmed** (14 gated tools across 14 files). All 14 are annotated: a feature-gated tool is excluded from
the coverage REQUIREMENT while its toggle is off, but it stays classifiable and classified — TC-U-106 proves
both halves, using the gated names as a probe input so the two views give different verdicts on the same
input.

### 5.6 Residency is not execution

17 tools are resident in `tools/list` (`McpCoreToolProfile` plus the three lazy-mode entry points). Residency
and execution location are orthogonal: `get-page` is resident **and** must run in a worker, while most
long-running tools are non-resident and are reached as
`clio-run {"command": "compile-creatio", …}`. The router must therefore resolve its key **after unwrapping
`clio-run`** (rule 7) — routing on the outer name would send every long-running call to the same place.

## 6. Full assignment — all 189 tools (shipped values)

`*` marks a tool resident in `tools/list`. A backticked toggle name marks a feature-gated tool.

Every row is the value actually declared by the tool's `[McpToolExecution]` attribute on this branch, verified
against the source by the coverage test. **(corrected)** marks the nine rows where per-tool review overrode
the Stage-0 heuristic; the reason is on the row, and the aggregate is in §2. Where a row and the code ever
disagree again, the code wins and this table is stale — the coverage test is what makes that loud.

| tool | hint | Location | Lifetime | OperationFamily | BudgetPolicy | RequiresClientRequests | SharedFileResource |
|---|---|---|---|---|---|---|---|
| `StopAllCreatio` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `add-data-binding-row` | Destructive | **in-process** (corrected) | n/a | none | none (never blocks on Creatio) | none | none |
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
| `get-tool-contract`* | ReadOnly | **in-process** (corrected) | n/a | none | none (never blocks on Creatio) | none | none |
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
| `list-apps`* | ReadOnly | worker | per-call | none | parent-kill (default) | **none** (corrected) | none |
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
| `new-integration-test-project` | **unbounded** | **in-process** (corrected) | n/a | none | none (never blocks on Creatio) | none | none |
| `new-test-project` | **unbounded** | **in-process** (corrected) | n/a | none | none (never blocks on Creatio) | none | none |
| `new-ui-project` | **unbounded** | **worker** (corrected) | per-call | none | parent-kill (default) | none | none |
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
| `remove-data-binding-row` | Destructive | **in-process** (corrected) | n/a | none | none (never blocks on Creatio) | none | none |
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
| `start-creatio` | **unbounded** | worker | per-call | none | parent-kill (default) | **progress** (corrected) | none |
| `stop-all-creatio` | Destructive | worker | per-call | none | parent-kill (default) | none | none |
| `stop-creatio` | Destructive | worker | per-call | none | parent-kill (default) | **progress** (corrected) | none |
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

Generated from `origin/master` @ `3fc50bf99`; **reconciled 2026-08-17 against the annotations shipped on
`feature/ENG-95262-mcp-worker-execution-boundary`**, where all 189 tools carry `[McpToolExecution]` and
`NotYetClassifiedTools` is empty.

The tables above are now a **record**, not a proposal — the attribute in the source is the authority, and
`clio.tests/Command/McpServer/McpToolExecutionMetadataCoverageTests.cs` is what makes a divergence fail loudly
instead of silently. Regenerate after any change to the tool catalog. Two things this reconciliation did NOT
resolve, both handed to Stage 7 / Stage 9 rather than papered over: the nine `in-process` tools that no budget
will bound (§5.4) and the two shared local artifacts that `McpToolSharedFileResource` cannot name (§5.3).

## Ground-truth audit of the classification (2026-08-18)

The `Location` column was seeded from a file-level heuristic, and section 4 above admitted it
over-assigns. Two adversarial passes then established ground truth independently of the declarations —
by following the code, not by reading the attribute. Results, so nobody has to redo it.

### The count is right; the sentence explaining it was not

189 declarations across 122 files, zero duplicate names, all six fields present on every one. Derived
by a brace-scoped parse with comments and string literals blanked and per-class `const string`
resolution. Every aggregate reproduces: 153/36 by `Location`, 144/36/7/2 by `BudgetPolicy`,
172/15/2 by `RequiresClientRequests`.

The earlier claim that a naive grep over-counts "by roughly 16" does not reproduce. Measured:
`\[McpServerTool(` gives +3, `\[McpServerTool(\(|\])` gives +23, and a bare `\[McpServerTool` gives
+134 because it collides with the `McpServerToolType` prefix. No variant lands on 205. All 23 extra
hits of the middle form are doc comments or string literals. There are no `[McpServerTool]`
declarations anywhere in `clio/` outside `clio/Command/McpServer/`, so the reader's assembly scan sees
everything.

### The severe direction is clean — all 36 hand-checked, zero defects

Every tool declared `InProcess` was checked at four levels: method body, class constructor, the
delegated `Command<T>`, and each injected service. None reaches Creatio. The two that looked live
resolve correctly:

- `add-data-binding-row` — the only HTTP path is explicitly disabled at the call
  (`allowRemoteDisplayValueResolution: false`); the `IApplicationClient` in that file belongs to a
  resolver neither method calls.
- `new-test-project` — passes an environment through, so it does take the resolving path, but its
  options carry neither `[RequiresPackage]` nor `[RequiresCreatioVersion]` (the only gates that force
  a round trip), the command holds no client, and container construction is lazy.

`clio-run` is `InProcess` by design and fail-closed: the router keys on the unwrapped, alias-canonical
inner name, and `clio-run` refuses any verb absent from the registry — and the registry is a subset of
what the metadata reader scans, so an unclassified inner verb cannot exist.

### The other direction — four rows are Worker without ever reaching Creatio

Not defects in the wedge sense; each costs one needless process spawn per call.

- `clear-browser-session` — the whole body is a local cache delete and a completed task. Its sibling
  `get-browser-session` does authenticate, so only the clear side is affected.
- `stop-creatio`, `stop-all-creatio` and its alias — stop a local application pool, OS service and
  process. Zero HTTP references in the command.
- `uninstall-creatio` — local-only, as its own remark says. But `Worker` is right here for a different
  reason than reachability: it is a long destructive operation that wants containment. Do not
  "correct" this one.

`restore-db-*` (three rows) speak to PostgreSQL, SQL Server and Kubernetes but never to Creatio over
HTTP. They still block on external input and output, so `Worker` is defensible; the point is that the
justification is containment, not reachability.

`compile-status` and `restart-status` do no Creatio work at all, yet must be `Worker` + `Sticky`:
they read an in-process registry owned by whichever process ran the operation, so the poll has to
reach that same worker. `restart-status` documents this; `compile-status` carries the same shape with
no comment, which is worth adding.

### Five of the six fields have no runtime consumer yet

Only `Location` is read at runtime, by `McpExecutionRouter`. `OperationFamily`, `Lifetime`,
`RequiresClientRequests` and `SharedFileResource` have zero consumers, and `BudgetPolicy` is read by
nothing — every relayed call is bounded by the single 120-second dispatcher budget regardless of what
it declares. That is expected while stages 7 and 8 are unbuilt, but it means a wrong value in those
five degrades nothing today and will degrade something later, without a test noticing. The coverage
test asserts presence, not correctness, and cannot close this.

`RequiresClientRequests` was nonetheless verified correct: exactly one production sampling call site,
consumed by exactly the two tools that declare `Sampling`; and the fifteen `Progress` declarations map
onto exactly the emit sites, including the two that call the notification API directly rather than
through the heartbeat, and the two that forward stage events.

### One live defect the audit surfaced

`get-related-page-addon`, a shipped cohort member, holds the SHARED fallback lock across a Creatio
round trip — see story 19.

### One contradiction, adjudicated with primary evidence

A second pass claimed the Worker direction is entirely clean — no row that never reaches Creatio — and
that the four listed above are therefore not findings. That claim was checked and **rejected**, because
the two passes did not use the same method: the clearing pass inferred reachability from the tool's
constructor dependencies, while the pass that flagged them read the command body.

Verified directly, at the source:

- `BrowserSessionService.ClearSessionAsync` is nine lines: a local cache delete, an optional file
  delete, and `Task.CompletedTask`.
- `StopCommand.cs` — zero occurrences of `IApplicationClient`, `HttpClient`, either request verb, the
  URL builder, or `WebRequest`.
- `CreatioUninstaller.cs` — the same, zero.

A constructor dependency proves a service is available, not that a code path calls it. The clearing
pass disclosed this tier itself. Where the two disagree, the read of the command body wins, and the
four rows stand — with the reminder above that `uninstall-creatio` should not be "corrected", because
its `Worker` classification is about containing a long destructive operation, not about reachability.
