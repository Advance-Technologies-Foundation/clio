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
| R3 | Notification reordering breaks ordered replay | **measured**: the SDK's handler dispatch reorders `0..5` into `[5,4,2,3,0,1]` |
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
| Full unit suite | `--filter "Category=Unit"` | **required** whenever `BindingsModule.cs` or `clio/Common/**` is touched (Stages 2, 3, 5, 7) |
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

### Stage 2 — process supervisor

| ID | Tier | Assertion |
|---|---|---|
| TC-U-201 | Unit | Concurrency cap admits N, queues N+1, and never drops a call |
| TC-U-202 | Unit | Stale-worker cleanup is **identity-checked** — a reused PID belonging to a stranger is not killed |
| TC-E-201 | E2E | **SIGKILL the parent while a worker has a descendant of its own; both disappear** (R4) |
| TC-E-202 | E2E | Budget expiry kills the worker and its descendants; the parent answers with a bounded error |
| TC-M-201 | Manual/measured | Windows child spawn cost and Job Object containment (**OQ-1** — Stage 2 cannot close without this number) |
| TC-M-202 | Manual/measured | Memory/CPU ceiling for concurrent children; produces the supported maximum (**OQ-2**) |

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
| TC-E-601 | E2E | **The wedge scenario (§4) — the shipping C# port of the lab harness.** Asserts on `/counters` deltas: D issues ≥ 1 request and succeeds |
| TC-E-602 | E2E | Flag **off** ⇒ byte-identical behaviour to today for every cohort tool (no accidental default switch) |
| TC-E-603 | E2E | Cohort tools produce identical results through the worker and in-process (`get-page`, `list-pages`, `list-app-sections`, `get-schema`, `get-related-page-addon`, SQL/OData) |
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
| TC-E-801 | E2E | `deploy-creatio` is bounded by **terminal stage**, not a generic kill; a mid-deploy budget expiry never leaves a half-installed environment |
| TC-C-801 | ClioRing | Full contract suite + **Windows x64 NativeAOT publish** green (a JIT-only pass is explicitly insufficient) |

### Stage 9 — interprocess file gates

| ID | Tier | Assertion |
|---|---|---|
| TC-E-901 | E2E | Two concurrent workers writing `.clio-pages/{schema}/meta.json` produce a consistent file; neither write is silently lost (R7) |
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

## 6. Regression scope per stage

| Stage | Targeted filter | Full suite required |
|---|---|---|
| 1 | `Category=Unit&Module=McpServer` | no |
| 2, 3, 5, 7 | `Category=Unit&Module=McpServer` | **yes** — `BindingsModule.cs` / `clio/Common/**` touched |
| 4, 8 | `Category=Unit&Module=McpServer` + ClioRing contract + NativeAOT publish | no (unless DI touched) |
| 6, 9, 10 | `Category=Unit&Module=McpServer` + `clio.mcp.e2e` | no |
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
