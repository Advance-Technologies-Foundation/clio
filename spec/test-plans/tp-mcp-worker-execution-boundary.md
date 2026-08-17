# Test Plan — MCP worker execution boundary

- **Status:** Draft (Stage 0 artifact; per-stage sections firm up as each stage starts)
- **Date:** 2026-08-17
- **Jira:** [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
- **ADR:** [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
- **Inventories:** [execution metadata](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-execution-metadata.md) ·
  [cross-call state](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-cross-call-state.md) ·
  [credential threat model](../mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md)

## 1. The rule this plan exists to enforce

**Assert on backend request counters, not timings.**

This is not a style preference — it is what separates a real test from a vacuous one here. The defect's
signature is *a call that returns at the deadline having never issued an HTTP request*. A timing assertion
("the call finished in under 12 s") passes on the wedged system, because the wedged system also finishes in
12 s. Only the request counter distinguishes "answered" from "never asked".

Every test that claims the wedge is fixed must sample the stub's `/counters` immediately before and after
the call and assert the delta.

## 2. Risks driving coverage

| # | Risk | Why it is credible |
|---|---|---|
| R1 | A stalled call still wedges the environment | the whole point; regression here is total |
| R2 | Sampling silently degrades to `Skipped=true` under the relay | no error surfaces — `update-page` / `sync-pages` just give a worse answer |
| R3 | Notification reordering breaks ordered replay | **measured**: the SDK dispatches notification callbacks concurrently — `0..5` arrived as `[5,4,2,3,0,1]`, and as `[2,0,1,3,4]` on a retry with a FIFO added (ADR §3.2) |
| R4 | Orphaned worker survives parent death | **measured**: the prototype leaked one orphan |
| R5 | Credential downgrade in the worker (bearer executes as Supervisor) | this exact defect already happened once in this codebase; the symptom is **success** |
| R6 | Sticky worker reachable by the wrong caller | environment-only scoping is the natural — and wrong — key |
| R7 | `.clio-pages` corruption once two processes write it | read-modify-write with **swallowed** I/O failures |
| R8 | ClioRing contract breakage | stage events, progress correlation, terminal semantics |
| R9 | Coverage test passes with tools unclassified | a coverage test that does not fail on a new tool buys nothing |

## 3. Test tiers

| Tier | Filter / project | Notes |
|---|---|---|
| Unit | `--filter "Category=Unit&Module=McpServer"` | metadata, routing, catalog coverage, relay units |
| Full unit suite | `--filter "Category=Unit"` | **required** whenever `BindingsModule.cs` or `clio/Common/**` is touched, or a change spans more than 3 modules (`AGENTS.md:369-373`) — Stages 2, 3, 4, 5, 7, 8, 10; see §6 for the per-stage files that trigger it |
| E2E | `clio.mcp.e2e` | live stdio protocol; **not run by GitHub CI** — must be triggered on TeamCity (`Team_Atf_ClioMcpE2eTests`) and the result stated in the PR |
| ClioRing contract | `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` + Windows x64 NativeAOT publish | mandatory for Stages 4 and 8 |
| Reproduction lab | branch `spike/eng-95262-lab` | the deterministic stub + wedge harness; the shipping artifact is the C# port (TC-E-01) |

## 4. The wedge scenario — the plan's anchor

Four calls against the deterministic stub, `list-pages`, 12 s budget, stub stalling for A/B/C and healthy
for D:

| call | expected on master today (the defect) | expected after Stage 6 |
|---|---|---|
| A — stalls | 12 s, **1** backend request | 12 s, **1** request, child killed |
| B — same tool, +1.5 s | 12 s, **0** requests | 12 s, **1** request, child killed |
| C — after A and B returned | 12 s, **0** requests | 12 s, **1** request, child killed |
| **D — backend healthy again** | **12 s, 0 requests — permanently wedged** | **< 2 s, success** |

D is the acceptance criterion for the whole feature. A and B and C returning at the budget is *correct*
behaviour — the backend genuinely is not answering. D returning 0 requests is the defect.

## 5. Test cases by stage

### Stage 1 — execution metadata + coverage test

| ID | Assertion |
|---|---|
| TC-U-101 | Every enabled canonical tool has all six execution-metadata fields; an unclassified tool **fails the test** |
| TC-U-102 | A synthetic new tool added without metadata fails TC-U-101 (proves the coverage test is not vacuous — R9) |
| TC-U-103 | A starter and its status poller disagreeing on `OperationFamily` or `Lifetime` fails the test |
| TC-U-104 | Metadata resolution unwraps `clio-run` / `clio-run-destructive` and keys on the **inner** command |
| TC-U-105 | The 37 hint-unbounded tools all carry an explicit `BudgetPolicy` — none defaults to "unbounded" |
| TC-U-106 | Feature-disabled tools are excluded from the coverage requirement but not from the catalog |
| TC-U-107 | A deprecated alias and its canonical carry **identical** execution metadata (e.g. `StopAllCreatio` vs `stop-all-creatio`); divergence fails the test |
| TC-U-108 | **Cross-field invariants** (inventory §3): `OperationFamily = deploy` ⇒ `Location = worker` **and** `BudgetPolicy = terminal-stage`; `Location = in-process` ⇒ `OperationFamily = none`, `Lifetime = n/a`, `BudgetPolicy = none`. A row that is per-field valid but internally impossible fails in the build, not in review |

### Stage 2 — process supervisor

| ID | Tier | Assertion |
|---|---|---|
| TC-U-201 | Unit | Concurrency cap admits N, queues N+1, and never drops a call |
| TC-U-202 | Unit | Stale-worker cleanup is **identity-checked** — a reused PID belonging to a stranger is not killed |
| TC-E-201 | E2E | **SIGKILL the parent while a worker has a descendant of its own; both disappear** — Linux and macOS (R4, **R-8a**) |
| TC-E-203 | E2E | The same containment on **Windows**, via Job Object kill-on-close (**R-8b**). Blocked on OQ-1; until it passes, no cohort ships on Windows and any delivery is scoped to R-8a only |
| TC-E-202 | E2E | Budget expiry kills the worker and its descendants; the parent answers with a bounded error |
| TC-M-201 | Manual/measured | **DONE 2026-08-17** (ADR §2.4): spawn + `initialize` p50 2.763 s on Windows Server 2022; Job Object kill-on-close contains the subtree only when assignment precedes first execution |
| TC-E-204 | E2E | **The budget clock starts at spawn, not at admission** — under a concurrency cap, a call queued behind CPU is not killed for waiting (derived from the width-16 measurement: 16.9 s of pure queueing) |
| TC-M-202 | Manual/measured | **DONE 2026-08-17** (ADR §2.4): linear past core count, so the cap is core-count-derived; memory is not the binding constraint |

### Stage 3 — worker mode

| ID | Tier | Assertion |
|---|---|---|
| TC-U-301 | Unit | Worker startup runs no host bootstrap — no telemetry flush/drain, no catalog refresh |
| TC-U-302 | Unit | Worker inherits the parent's **frozen** enabled-tool generation; a mid-session `appsettings.json` toggle change does not alter the worker's tool set |
| TC-U-303 | Unit | A sticky worker **keeps** `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`; an ordinary worker does **not** inherit a read-deadline override |
| TC-E-301 | E2E | **No secret appears in the worker's command line** (and, where readable, its environment block) — R-1 |
| TC-E-302 | E2E | **Fail-first identity assertion:** a non-Supervisor bearer principal is observed *at the Creatio end* as that principal (R5). A "call succeeded" assertion is explicitly insufficient |
| TC-E-303 | E2E | Worker given unusable material **refuses** the call; it does not fall back to registry credentials or a default identity |

### Stage 4 — full-duplex relay

| ID | Tier | Assertion |
|---|---|---|
| TC-E-401 | E2E | **Sampling actually executes** — `update-page` under the relay produces a real semantic review, not `Skipped=true`; a marker planted in the client's sampling answer appears in the tool result (R2) |
| TC-E-402 | E2E | `_meta.clioStageEvent` and `progressToken` are **byte/schema identical** to the committed contract fixture |
| TC-E-403 | E2E | **Monotonic sequence delivery under concurrency** — sequences arrive in order; a reordered delivery fails (R3) |
| TC-E-404 | E2E | Cancellation propagates parent → child and the child stops issuing backend requests |
| TC-U-401 | Unit | The relay does not forward notifications through `McpClientHandlers.NotificationHandlers` (rule 12) — structural assertion, since the reordering is not deterministic enough to test by observation alone |
| TC-U-403 | Unit | **Router ordering (rule 9, AC-06):** an unmatched write tool routed through the worker still hits the destructive-confirmation gate **first**, and a refused confirmation prevents dispatch — asserted on the call order and on the child never being spawned, not by inspection |
| TC-U-402 | Unit | **Both** dispatch seams route and agree: a tool reached as a matched name and the same tool reached through a deprecated alias (unmatched, via `McpDurableCallToolHandler`) resolve to the same execution location |
| TC-C-401 | ClioRing | `ClioRing.Tests` green against the changed contract; unknown-field tolerance and ordered replay preserved |

### Stage 5 — credential channel + per-client isolation

| ID | Tier | Assertion |
|---|---|---|
| TC-U-501 | Unit | Sticky scope key = principal + normalised target + credential fingerprint; any one omitted ⇒ test fails |
| TC-U-502 | Unit | Worker lookup **fails closed** — an unmatched key spawns a new worker, never a "closest match" |
| TC-U-503 | Unit | Target-normalisation equivalence table, **both directions**: equivalent pairs share a key, near-miss pairs do not (R-6) |
| TC-U-504 | Unit | Existing credential-smuggling rejections still hold with routing enabled; the router rejects rather than routes (T-2) |
| TC-E-501 | E2E | **Two concurrent callers, same environment, different principals → two distinct workers**, each observing its own identity at the Creatio end (R6) |
| TC-U-505 | Unit | Redaction: a known secret marker appears nowhere in parent output, error envelopes, or worker-stderr passthrough (R-7) |

### Stage 6 — first cohort

| ID | Tier | Assertion |
|---|---|---|
| TC-E-601 | E2E | **The wedge scenario (§4) — the shipping C# port of the lab harness.** Asserts on `/counters` deltas: D issues ≥ 1 request **on a session distinct from A's** (per-call identity or per-session token, not a global counter), and **no session object is referenced by both A and D**. "D issued a request" alone is necessary but not sufficient — it does not distinguish a new clean session from a reused one |
| TC-E-601b | E2E | **A is cleaned up, not merely outrun** — after D succeeds, A's session shows abandoned/cancelled state and A's child is gone; the environment holds no session on A's behalf. This is the "environment recovers" half of the anchor that a D-only assertion leaves uncovered |
| TC-E-602 | E2E | **No unintended route change**: every tool whose `Location` is `in-process` behaves byte-identically to master, asserted by running the existing e2e suite unchanged. There is no feature toggle to flip (ADR §5), so this is the assertion that replaces "flag off ⇒ identical" — the guard is the metadata, and TC-U-101/108 are what keep the metadata honest |
| TC-E-603 | E2E | Cohort tools produce identical results through the worker and in-process (`get-page`, `list-pages`, `list-app-sections`, `get-schema`, `get-related-page-addon`, SQL/OData). The in-process arm is obtained by **substituting the metadata reader in DI** to report `Location = in-process`, not by a runtime flag |
| TC-E-604 | E2E | Environment recovers **as soon as the backend does** — un-stall the stub and the next call succeeds with no restart |

### Stage 7 — sticky supervision

| ID | Tier | Assertion |
|---|---|---|
| TC-E-701 | E2E | `compile-creatio` returns in-progress; subsequent `compile-status` polls reach the **same** worker and answer from it |
| TC-U-701 | Unit | Private completion signal reaps a worker for the three families with **no** operation registry (`install-process-builder`, `create-app-section`, `restart-by-credentials`) |
| TC-U-702 | Unit | Parent-owned `configuration-build` reservation excludes compile ↔ install-process-builder across **processes**, keyed by normalised tenant + resource |
| TC-U-703 | Unit | Sticky lifetime bounded by credential validity, with an explicit maximum (T-8) |

### Stage 8 — long synchronous / streaming commands

| ID | Tier | Assertion |
|---|---|---|
| TC-E-801 | E2E | `deploy-creatio` is bounded by **terminal stage** per the ADR §3.3 protocol, not a generic kill; a deploy that keeps streaming stages past the ordinary budget runs to its terminal stage and is never killed mid-deploy |
| TC-E-802 | E2E | **The lost-child case** (ADR §3.3): a child killed mid-deploy, and a child that goes silent past the stage-event timeout, each produce an explicit *indeterminate* error naming the last stage reached — **never a success, never an automatic retry**. This is the case that distinguishes the protocol from a generic kill |
| TC-E-803 | E2E | Post-terminal exit grace: a child that emits its terminal stage then hangs is killed after the grace window, and the tool result is the **terminal stage**, not an error |
| TC-C-801 | ClioRing | Full contract suite + **Windows x64 NativeAOT publish** green (a JIT-only pass is explicitly insufficient) |

### Stage 9 — interprocess file gates

| ID | Tier | Assertion |
|---|---|---|
| TC-E-901 | E2E | **PASSED 2026-08-17 against a live stand** (`sae_m_seeenu_15888720_0820`, .NET Framework 4.8 / MSSql), 30 s: two concurrent real `clio update-page` processes on one schema left a whole, parseable `meta.json` carrying its baseline. This was stage 9's only compile-only criterion; it is now executed |
| TC-U-901 | Unit | I/O failures in the baseline/meta path **surface** instead of being swallowed |
| TC-E-902 | E2E | Browser-session cache under concurrent access behaves per its documented policy |

### Stage 10 — deletions

| ID | Tier | Assertion |
|---|---|---|
| TC-E-1001 | E2E | With the monitor, read deadline, gate, `CwdLock` and session pinning removed, the full cohort suite stays green |
| TC-U-1001 | Unit | No remaining code path references `CLIO_MCP_READ_DEADLINE_SECONDS` |
| TC-E-1002 | E2E | **Ordering guard (cross-call state §5):** the `.clio-pages` file gate is in place *before* `CwdLock` is removed — a test that fails if the removal lands first |

### Folded-in fixes (independent of stage)

| ID | Tier | Assertion |
|---|---|---|
| TC-U-F01 | Unit | `PageSchemaMetadataHelper.ExecuteSelectQuery` routed through `ServiceResponseJsonGuard`: an HTML login page, a 500, and a timeout each produce a **distinct transport/auth error**, never `"Failed to query SysPackage"` |
| TC-U-F02 | Unit | The bare `catch` is gone — an unexpected exception is not converted into a domain answer |
| TC-U-F03 | Unit | `MobilePageConversionGuideTool` balances every `GetLock` with `MarkAvailable` at all three sites (`:111`, `:339`, `:531`); the `SharedFallbackKey` mapping is not permanently pinned after a call |
| TC-U-F04 | Unit | It resolves and uses the **real tenant key** rather than `SharedFallbackKey` |

### Stand prerequisites the live TC-E-901 run exposed

Both were found by running, not by reading, and both block the suite rather than one test.

1. **`SchemaNamePrefix` must permit the seeded fixture, and it is enforced at SAVE time, not only at
   creation.** Four e2e files (`ClioPagesConcurrencyE2ETests`, `PageUpdateToolE2ETests`,
   `PageSyncToolE2ETests`, plus the sync conflict cases) depend on a page literally named
   `ClioMcp_BlankPageToSave`. On a stand with the default `SchemaNamePrefix = Usr`, Creatio rejects both
   `create-page` **and every later `update-page`** with *"code ... must start with the Usr prefix"*. So a
   stand hosting this suite must keep `SchemaNamePrefix` set to `ClioMcp_` (or empty) for the whole run —
   relaxing it only while seeding is not enough, which is exactly the mistake the first run made.
2. **The fixture is not self-seeding.** No test creates `ClioMcp_BlankPageToSave`; it must pre-exist in a
   writable package. On a stock stand the only writable package is `Custom` (maintainer `Customer`), and
   the page is created from `BlankPageTemplate`.

### A defect the arrange step revealed

`get-page` **exits 0 when it fails**: a missing schema returns `{"success":false, ... "error":"Schema
'X' not found"}` with exit code **0**. TC-E-901's arrange step asserts `read.ExitCode.Should().Be(0)`, so
that assertion passes on a stand where nothing was materialised, and the test then fails later at the
`meta.json` existence check with a misleading message. Two consequences: the CLI contract is wrong (a
failed read must not exit 0), and every e2e arrange step that gates on exit code alone is weaker than it
looks. Tracked as story 13.

## 6. Regression scope per stage

| Stage | Targeted filter | Full suite required |
|---|---|---|
| 1 | `Category=Unit&Module=McpServer` | no — reflected attributes and a coverage test only; no DI, no `clio/Common/**` |
| 2, 3, 5, 7 | `Category=Unit&Module=McpServer` | **yes** — `BindingsModule.cs` / `clio/Common/**` touched |
| 4 | `Category=Unit&Module=McpServer` + ClioRing contract + NativeAOT publish | **yes** — `BindingsModule.cs` touched to register the relay filter on both dispatch seams (`:1160`, `:165`) |
| 8 | `Category=Unit&Module=McpServer` + ClioRing contract + NativeAOT publish | **yes** — the terminal-stage protocol (ADR §3.3) touches the supervisor and the relay in `clio/Common/**` |
| 6 | `Category=Unit&Module=McpServer` + `clio.mcp.e2e` | no — cohort routing is metadata only; no DI or `clio/Common/**` change |
| 9 | `Category=Unit&Module=McpServer` + `clio.mcp.e2e` | no — file gates are local to the baseline/meta and cache paths |
| 10 | `Category=Unit&Module=McpServer` + `clio.mcp.e2e` | **yes** — deletes DI-registered machinery (the per-tenant monitor, `McpReadResponseDeadline` + gate, session-container pinning) from `BindingsModule.cs` and removes `CwdLock` from `clio/Common/**`; the deletions also span well over 3 modules |
| Folded-in | `Category=Unit&(Module=Command\|Module=McpServer)` | no |

## 7. Exit criteria for the feature

Every one is asserted, not observed:

1. The wedge scenario's call D succeeds **with a backend request issued** (TC-E-601).
2. The environment recovers as soon as the backend does, without a restart (TC-E-604).
3. No MCP-reachable path waits on Creatio without a bound — including the upload/download/install family,
   which is bounded by the kill rather than by a timeout parameter.
4. A transport or auth failure never surfaces as a domain answer (TC-U-F01).
5. Identity is preserved end to end under every auth mode (TC-E-302, TC-E-501).
6. Parent death leaves no orphan (TC-E-201).
7. ClioRing contract and NativeAOT publish green (TC-C-401, TC-C-801).
