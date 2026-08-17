# ADR: move MCP execution into short-lived worker processes

- **Status:** Accepted (Stage 0 design artifact — no production code depends on it yet)
- **Date:** 2026-08-17
- **Jira:** [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262) (parent: ENG-95286 — Migration tool from Classic UI to Freedom UI)
- **Supersedes:** nothing. **Amends:** `adr-read-only-mcp-response-deadline.md` (its mechanism is scheduled for
  deletion at Stage 10), `adr-mcp-durable-invocation.md` (adjacent dispatch seam — routing must cover both, see §9).
- **Reproduction lab:** branch `spike/eng-95262-lab` (preserved spike, deliberately not a merge candidate).

## 1. Context

One Creatio request that never gets an answer makes a long-lived `clio mcp-server` permanently unusable for
that environment. This is reproduced deterministically, not inferred.

Three mechanisms compose:

1. **No transport bound.** `IApplicationClient.ExecutePostRequest` / `ExecuteGetRequest` default to
   `Timeout.Infinite` (`clio/Common/IApplicationClient.cs:21-33`). Almost no read call site overrides it;
   `SelectQueryHelper.ExecuteSelectQuery` (`clio/Package/SelectQueryHelper.cs:29`) is the exception.
2. **An untimed monitor.** Environment-scoped tools run inside `lock (McpToolExecutionLock.GetLock(tenantKey))`
   — a plain monitor with no acquisition timeout (`clio/Command/McpServer/Tools/BaseTool.cs:59-70, :201-205`).
3. **A deadline that bounds the answer, not the work.** `McpReadResponseDeadline` races `Task.Run` and
   abandons it; the abandoned read keeps its thread **and the monitor**
   (`clio/Command/McpServer/McpReadResponseDeadline.cs`).

The result is not a slow environment — it is a dead one. Every later call for that environment is cut at the
120 s read deadline **without issuing an HTTP request at all**, and it stays dead after the backend recovers.

### 1.1 Why this ADR has no PRD

The Jira issue is the PRD. It carries the requirements, the measured evidence, the decision, the rejected
alternatives, eleven implementation constraints, and the definition of done — all authored before this ADR
and all reproduced below where they are load-bearing. Writing a `spec/prd/prd-*.md` would be transcription,
not analysis, so the BMAD Phase-1 gate is recorded as **satisfied by the issue**, not skipped.

### 1.2 Evidence (measured; raw runs in the issue comments)

| Measurement | Result |
|---|---|
| Field run `wf_3509d34b-193` (8 agents, 115.6 min) | MCP clio: 44 calls / **60.2 min waiting** / avg 82 s / 15 timeouts. clio CLI: 42 calls / 1.7 min / avg 2.4 s / **0 timeouts**. Same stand, same minutes. |
| Same operation head-to-head | `get-page`: MCP 16 calls, avg 90.2 s, 11 timeouts — CLI 18 calls, avg 2.3 s, 0 timeouts |
| Deterministic reproduction (stub + real `clio mcp-server`) | Locked tool: A 12 s / 1 backend request; B and C 12 s / **0**; D with a **healthy** backend 12 s / **0** |
| Lock coverage *(issue measurement — **not** re-derived; §1.4 re-derives it per tool instead)* | 44 of 123 tool files never take the monitor; every tool in the field's 120 s cascade is one of the locked ones |
| Platform hypothesis | **Refuted.** Creatio does not serialize concurrent requests on one session: 8 concurrent heavy `SelectQuery` = 2.27 s on one shared session vs 2.67 s on eight logins |
| `ForceUseSession` | measured **no-op** for cookie-authenticated DataService traffic on the test stand |
| Costs | clio process start ~0.27 s; child `clio mcp-server` spawn + `initialize` ~0.65 s, **which subsumes the warm login** (p50 0.468 s measured on its own — the child authenticates during `initialize`, so the two are concurrent phases of one 0.65 s window, not sequential additions) |

### 1.3 Two secondary defects, folded in

- **A structural hole in the deadline.** `McpReadDeadlineGate` admits
  `!destructive && !progressStreaming && (readOnly || isGetPage)` — the name whitelist is literally just
  `get-page`. Everything else that is neither `ReadOnly` nor `Destructive` is bounded by nothing. One
  `clio-run get-schema` ran 1800 s with no response and no progress until the client aborted.
- **Transport failures masquerade as domain answers.** `PageSchemaMetadataHelper.ExecuteSelectQuery`
  (`clio/Command/PageSchemaMetadataHelper.cs:33-46`) ends in a bare `catch { return (…, false); }`, so an
  HTML login page, a timeout and a 500 all become `"Failed to query SysPackage"` — while the identical
  command through the CLI returns `success:true` in ~1 s. This is a second, unguarded copy of the
  SelectQuery plumbing next to the one ENG-93365 fixed with `ServiceResponseJsonGuard`.

### 1.4 Census reconciliation (re-measured on `origin/master` @ `3fc50bf99`, 2026-08-17)

The issue was written against `82947ba0c` (2026-08-13). The census reproduces; the catalog simply grew by
35 commits' worth of tools in four days. This matters because Stage 1's coverage test asserts over exactly
this set.

| | issue (2026-08-13) | master `3fc50bf99` (2026-08-17) |
|---|---|---|
| `[McpServerTool]` declarations | 185 | **189** |
| `ReadOnly = true` | 63 | **65** |
| `Destructive = true` | 84 | **87** |
| neither — **bounded by nothing** | 38 | **37** |

Method: brace-scoped C# parse with comments and string literals blanked, per-class `const string`
resolution for `Name = ToolName`. A naive `grep` over-counts by ~16 (multi-line attributes, `[McpServerTool]`
inside doc comments and inside an exception message in `McpToolInvokerRegistry.cs`). The method is stated in
full in the inventory so the number can be re-derived rather than taken on trust; Stage 1's coverage test
replaces the ad-hoc parse with one that lives in the build.

Four structural facts were re-verified against master rather than trusted from the issue:

- **Exactly two operation registries exist** — `ICompileOperationRegistry` (`BindingsModule.cs:738`) and
  `IRestartOperationRegistry` (`BindingsModule.cs:742`). `install-process-builder` and `create-app-section`
  have none. (The issue cited lines 736/740; the registrations moved, the fact holds.)
- **Lock coverage re-derived per tool** (the issue counted files): 115 of 189 tools reach the per-tenant
  monitor, 72 take no lock at all, and 2 take the narrow `configuration-build` reservation instead.
- **Sampling has exactly two callers** — `update-page` and `sync-pages`, both through
  `PageBodySamplingService` (`clio/Command/McpServer/Tools/PageBodySamplingService.cs:130`).
- **Constraint 10 survives, with a changed citation.** `BuildCacheKey` is no longer
  `options.Environment ?? settings.Uri`: ENG-94529 made it `(options.Environment ?? DefaultIdentifier) | uri`
  (`ToolCommandResolver.cs:361-379`). A registered *name* and an explicit *URI* for the same target still
  produce two different keys, because the name branch yields `myenv|http://x` and the URI branch yields
  `<default>|http://x`. The normalisation work stands.

## 2. Decision

**Keep the MCP contract; move the execution boundary.**

Every environment-touching tool call is executed by a **short-lived child `clio mcp-server`**; the parent
process only routes. The child speaks MCP, so the parent relays `tools/call` and the response verbatim — no
new wire format, no per-command `--json` work, no change to tool names, schemas, or the guidance layer.

The budget is enforced by **killing the child**, not by asking the transport to stop. This is the property
the `UploadFile` / `DownloadFile` / `install-application` class requires: those have no timeout parameter at
all, and a process kill needs no cooperation from them.

### 2.1 Why this and not the alternatives

| Alternative | Why rejected |
|---|---|
| Fresh session per call | 0.5 s per call for isolation the kill already provides |
| In-process bounded admission | Six blockers found by red-team, every one a consequence of sharing mutable state across calls in one address space |
| MCP Tasks (spec extension) | Two incompatible spec generations; SDK 1.4.1 carries the obsolete one; no client in the official matrix advertises the extension |

### 2.2 What the prototypes proved

Measured against the same stub and the same harness as the reproduction:

- **The wedge is gone.** A/B/C each issue their own backend request and are killed at the parent budget;
  **D succeeds in 0.8 s** where today it returns 12 s / 0 requests, permanently.
- **The budget needs no transport cooperation.** Children ran with clio's *default* 120 s read deadline and
  still died at the parent's 12 s budget.
- **Cost is ~0.7 s per call** (0.72–0.78 s healthy, p50 0.78 s; ~0.68 s of it child spawn + `initialize`).
  **The warm login is inside that 0.68 s, not added to it** — the child authenticates during `initialize`.
  The end-to-end measurement is what settles this: a sequential login would put the total at ~1.1 s, and the
  measured p50 is 0.78 s. §1.2's separate 0.468 s figure is the login phase measured on its own, not a
  second cost to sum. So the cost model stands at **+31 s for 44 calls** (~+140 s for a 200-call agent run),
  not +49 s.
  Eight concurrent calls: 1.47 s wall, 8/8 OK. On the observed workload (44 MCP calls in 116 min) that is
  **+31 s against 38–60 min of timeouts**.
- **Long operations work with a sticky child.** `compile-creatio` returned in-progress at 8 s, and three
  `compile-status` polls answered `running` from the same worker in 0.00–0.02 s.
- **The relay holds on the shipping SDK** (`ModelContextProtocol` 1.4.1) — with one caveat that became a
  hard rule; see §3.2.

### 2.3 What this deletes

Unlike the in-process alternative, this decision *removes* machinery rather than adding it. At Stage 10:
the universal per-tenant monitor, `McpReadResponseDeadline` + `McpReadDeadlineGate`, `CwdLock`,
session-container pinning, and the `CLIO_MCP_READ_DEADLINE_SECONDS` contract.

## 3. Implementation rules (binding)

Each rule below has already broken a naive version of this plan. They are numbered as in the issue;
rule 12 is new, from the relay spike.

1. **Full-duplex relay, not call/response forwarding.** `update-page` / `sync-pages` call
   `server.SampleAsync` (`PageBodySamplingService.cs:130`); a child whose client is the parent silently
   degrades semantic review to `Skipped=true`. ClioRing reads **raw** notifications (`_meta.clioStageEvent`,
   exact progress token, `(runId, sequence)` buffering) — deserialising and rebuilding them breaks it.
2. **The relay must own the child's transport read loop.** See §3.2.
3. **`mcp-http` credentials never travel in tool arguments.** Passthrough credentials live in the parent's
   `HttpContext`; the resolver rejects argument-borne credentials as smuggling. Sticky workers are scoped by
   authenticated session/principal **plus** normalised target **plus** credential fingerprint — status tools
   are credential-scoped today, so sharing by environment alone is a cross-client boundary violation.
   See the credential threat model inventory.
4. **Process lifetime ≠ response budget.** `deploy-creatio` and `uninstall-creatio` are synchronous,
   destructive and progress-streaming, and ClioRing waits for the authoritative terminal stage. A generic
   45–60 s kill could leave a half-installed environment. The signalling protocol is specified in §3.3 —
   without it `terminal-stage` is a label, and TC-E-801 could only prove that one implementation happens to
   pass.
5. **Only two operation registries exist** (compile, restart). `install-process-builder` and
   `create-app-section` have none, and restart-by-credentials is deliberately unreportable, so "reap on
   terminal status" cannot manage three of the four long-running modes. Workers need a **private completion
   signal**.
6. **Containment, not EOF.** Windows Job Object with kill-on-close; Unix process-group containment plus
   parent-death signalling; identity-checked stale-worker cleanup. The prototype leaked one orphan when the
   parent was killed mid-operation.
7. **Routing cannot be derived from an existing property.** `IMcpToolInvokerRegistry` exposes only
   `ReadOnly` / `Destructive` / retry-safety; `McpCoreToolProfile` is residency, not execution. New
   reflected metadata is required, with a coverage test. The routing key must be resolved **after
   unwrapping `clio-run`** — the long-running tools are non-resident and agents reach them through it.
8. **Separate address spaces do not isolate files.** `.clio-pages/{schema}/meta.json` is read-modify-write
   with swallowed I/O failures; the browser-session cache is shared under the clio home directory. DbHub is
   already cross-process safe (`.clio.lock`, `FileShare.None`).
9. **The router must sit after the destructive-confirmation seams**, or unmatched writes bypass host gating.
10. **Session identity must be normalised** so a registered name and an explicit URI for one target are one
    key (`ToolCommandResolver.cs:361-379`; see §1.4).
11. **Worker startup must not run host bootstrap** (telemetry flush/drain, catalog refresh) and must inherit
    the parent's **frozen** enabled-tool generation, so feature flags cannot disagree mid-session. A sticky
    worker must **keep** clio's own `CLIO_MCP_RESPONSE_DEADLINE_SECONDS` (its in-progress envelope is what
    returns the call); an ordinary worker must **not** inherit a read-deadline override, since the parent
    enforces the budget by killing.
12. **(New, from the relay spike.) Notifications must not be forwarded through the SDK's client
    notification handlers.** See §3.2.

### 3.1 Relay properties — measured on SDK 1.4.1, which clio NO LONGER SHIPS

> **Evidence expiry, found 2026-08-17.** Every measurement in §3.1 and §3.2 was taken against
> `ModelContextProtocol` **1.4.1**. `Directory.Packages.props` now pins **2.2.0** (commit `e6c95ec9c`,
> "Upgrade MCP SDK to 2.2 hybrid transport") — a major bump that explicitly changed transports. So the two
> PASSes below are *encouraging but stale*, and **rule 12 rests on a FAIL observed in a version we do not
> use**: the concurrent-dispatch reordering may be fixed in 2.2.0, or the API that would let a relay own the
> read loop may have moved. Stage 4 must **re-run all three measurements on 2.2.0 first** and land them as
> standing tests. If the reordering no longer reproduces, rule 12 is retired on evidence rather than kept
> out of caution; if it does, it is kept with a current citation. Until then no relay code is written.


- **Server→client requests relay correctly — PASS.** The child's `sampling/createMessage` reached the
  parent's `SamplingHandler`, was forwarded via `parentServer.SampleAsync(...)` to the **real** client, and
  the client's answer came back into the child's tool result. This was the single biggest risk to the
  architecture; it is cleared.
- **`_meta` and progress-token fidelity — PASS.** Nested `_meta.clioStageEvent` content and the exact
  `progressToken` survive the round trip byte-identically — what ClioRing correlates on.

### 3.2 Notification ordering — the one FAIL, and the rule it produces

The child emitted sequences `0..5` sequentially on one stdout; the client received `[5, 4, 2, 3, 0, 1]`.
Adding a single-consumer FIFO queue in the parent did **not** fix it (`[2, 0, 1, 3, 4]` on the retry).

**What that proves — and what it does not.** A single-consumer FIFO only corrects order when its *producer*
is serialised; racing producers fill it out of order and it preserves that order faithfully. The two runs
disagreeing (`[5,4,2,3,0,1]` vs `[2,0,1,3,4]`) is itself the evidence: a deterministic source would have
reproduced the same permutation. So the conclusion is not "at or before dispatch" — it is that **the SDK
dispatches notification callbacks concurrently, and any path routing through the SDK dispatch layer is
subject to non-deterministic races regardless of FIFO depth.** Owning the read loop is correct because it
takes messages off the wire serially, before SDK dispatch is involved at all — not because it moves the
FIFO closer to the source. An implementer who reads this as a queue-placement problem and tries another
handler-layer fix will fail for a reason no amount of buffering addresses.

Therefore the relay **must own the child's transport read loop**, so forwarding inherits the pipe's natural
order. ClioRing itself tolerates reordering (it buffers by `(runId, sequence)`), but the relay must not be
the component that introduces it — other clients have no such buffer, and ordered replay is part of the
stage-event contract. This is an acceptance criterion for Stage 4, not a nice-to-have.

### 3.3 The `terminal-stage` protocol (binding, Stage 8)

Rule 4 says the parent waits for the authoritative terminal stage. That is only enforceable once three
things are fixed: what carries the signal, how long the parent waits for it, and what happens when it never
arrives.

**Channel.** The existing stage-event stream, not a new one. The child already emits
`notifications/progress` carrying `_meta.clioStageEvent` with `(runId, sequence)` — the same events ClioRing
correlates on (rule 1), relayed raw. A terminal stage is a stage event whose `status` is one of the
terminal values (`Completed` / `Failed` / `Cancelled`) for the run's root `runId`. No named pipe, no second
IPC path: a private channel would be a second contract to keep in sync with the one ClioRing already reads,
and the relay is required to be full-duplex for these tools anyway.

**Parent-side bound.** Two separate timers, and neither is a total-operation kill:

| Timer | Bound | On expiry |
|---|---|---|
| Terminal-stage wait | no fixed total; the operation may run as long as it streams | — |
| **Stage-event silence** | configurable, default **300 s** with no stage event of any kind | treat as a lost child (below) |
| Post-terminal exit grace | **30 s** between the terminal stage and child exit | kill the child; the operation itself already terminated, so this is safe |

The silence timer, not an operation timer, is what bounds `terminal-stage`. It is the one bound that cannot
truncate a legitimately long deploy: a healthy deploy streams stages continuously, and a child that has gone
30 s past its own terminal stage has nothing left to lose.

**Failure actions — the parent never guesses that a deploy finished.**

- **Child exits without a terminal stage** (crash, kill, non-zero exit): the call fails with an explicit
  *indeterminate* error naming the last stage reached, the environment is marked **possibly half-installed**,
  and the parent does **not** retry. Retry-on-ambiguity is how a half-installed environment becomes two.
- **Silence timer expires:** same indeterminate outcome, and the child is killed only *after* the error is
  reported, so its last stage is captured first.
- **Terminal stage arrives, child then hangs:** the exit grace applies; the tool result is the terminal
  stage, not an error.

The `BudgetPolicy = terminal-stage` value therefore means "bounded by silence and by the terminal event",
never "unbounded". TC-E-801 asserts the mid-deploy case; TC-E-802 asserts the lost-child case, which is the
one that distinguishes this protocol from a generic kill.

## 4. Execution metadata (Stage 1 contract)

Routing needs metadata that does not exist today. Six fields, reflected per tool, with a catalog coverage
test that fails when any enabled canonical tool is unclassified or a starter/status pair disagrees:

| Field | Values | Decides |
|---|---|---|
| `Location` | `in-process` \| `worker` | whether the call is relayed at all |
| `Lifetime` | `per-call` \| `sticky` | whether the worker survives the response |
| `OperationFamily` | `none` \| `configuration-build` \| `restart` \| `app-section-create` \| `deploy` | which sticky worker a status poll must reach; which shared reservation applies |
| `BudgetPolicy` | `none` \| `parent-kill (default)` \| `parent-kill (extended)` \| `terminal-stage` | how the parent bounds the call |
| `RequiresClientRequests` | `none` \| `sampling` \| `progress` \| both | whether the relay must be full-duplex for this tool |
| `SharedFileResource` | `none` \| `.clio-pages` \| `browser-session-cache` \| `configuration-build` … | which interprocess file gate Stage 9 must install |

The per-tool assignment for all 189 tools is the execution-metadata inventory. **The inventory is the
worklist, not the verdict**: its `Location` column is derived by a documented heuristic and every row is
confirmed (or corrected) when the attribute is actually applied in Stage 1 — which is exactly what the
coverage test forces.

## 5. Rollout

Staged, expanding by cohort.

**No feature toggle** (decided 2026-08-17, branch owner). The work is developed and tested on
`feature/ENG-95262-mcp-worker-execution-boundary`, and the branch *is* the test environment — a toggle
defaulting to off would mean the branch's own unit and e2e runs exercise the OLD path, so the thing being
built would never be the thing being verified. Cohort membership is therefore expressed as **data, not a
flag**: a tool routes to a worker because its `Location` metadata says `worker` (Stage 1). That gives the
same control at finer grain, with no second switch to keep in step, and it is substitutable in DI for tests.

Two consequences worth stating, because they are what a toggle would otherwise have covered:

- **A/B comparison is per-tool, not per-binary.** TC-E-603 compares a tool's result through the worker
  against the same tool with `Location = in-process`, which is a metadata substitution in the test, not a
  runtime flag flip.
- **The merge-time default is an open decision, not settled here** (OQ-5). Whether master ships this
  default-on or gated is decided when the branch is proposed for merge, against evidence this branch
  produces. Nothing in Stages 1-9 forecloses either choice.

| Stage | Content |
|---|---|
| 0 | **This ADR**, stories, test plan, three inventories |
| 1 | Execution metadata + catalog coverage test |
| 2 | Process supervisor: concurrency cap, resource accounting, cross-platform containment, stale-worker cleanup |
| 3 | Worker mode: no host bootstrap, frozen tool generation |
| 4 | Transparent full-duplex relay: capabilities, `SampleAsync`, raw notifications, cancellation, ordering |
| 5 | HTTP credential channel + per-client sticky isolation |
| 6 | First cohort routed to workers: retry-safe stdio reads (`get-page`, `list-pages`, `list-app-sections`, `get-schema`, `get-related-page-addon`, SQL/OData) |
| 7 | Sticky supervision: private completion signal; move the shared `configuration-build` reservation to the parent |
| 8 | Long synchronous / streaming commands (deploy, uninstall), gated by ClioRing contract tests + Windows x64 NativeAOT publish |
| 9 | Interprocess file gates for the concrete shared artifacts |
| 10 | Expand by cohort, then **delete** the universal monitor, `McpReadResponseDeadline` + `McpReadDeadlineGate`, `CwdLock`, session-container pinning, and the `CLIO_MCP_READ_DEADLINE_SECONDS` contract |

**Still out of bounds on this branch** — the original prohibition list minus the toggle clause, which the
no-toggle decision above replaces. Each of these remains a separate, later decision:

- proxy `mcp-http` traffic (Stage 5 builds the credential channel; enabling the HTTP path is not Stage 6);
- proxy destructive / deploy / uninstall / sticky operations ahead of their own stages (7, 8);
- **delete any existing deadline or guard** — Stage 10, and only after every cohort has moved. This one is
  an ordering constraint, not a policy, and dropping the toggle does not relax it;
- change ClioRing behaviour.

**Folded in, independent of stage** (each is a production fix and carries its own story):

- Route `PageSchemaMetadataHelper.ExecuteSelectQuery` through `ServiceResponseJsonGuard` and drop its bare
  `catch`.
- Fix `MobilePageConversionGuideTool`, which calls `McpToolExecutionLock.GetLock(SharedFallbackKey)` at
  three sites (`:111`, `:339`, `:531`) after resolving a real tenant and never calls the balancing
  `MarkAvailable`. Because `GetLock` pins the lock-provider mapping in-use
  (`McpToolExecutionLock.cs:157-159`), that mapping is pinned permanently.

## 6. Consequences

**Accepted costs**

- ~0.7 s added latency per proxied call, against 38–60 min of timeouts on the observed workload.
- One clio process per concurrent call. Eight parallel children was fine on a laptop; the supported maximum
  is an open question (§8).
- A supervisor, a worker mode and a relay are new code to own — offset by the machinery deleted at Stage 10.

**Gained**

- A stalled call cannot affect any other call for the same environment, and the environment recovers as
  soon as the backend does.
- The unbounded class (37 tools bounded by nothing, plus the whole upload/download/install family) becomes
  bounded by construction rather than by whitelist.
- The MCP contract, tool names, schemas and guidance layer are untouched, so ClioRing and every agent client
  see no difference.

**Unchanged deliberately** — the MCP wire contract, tool names and schemas, and the guidance layer.

## 7. Definition of done (feature-level)

- A stalled call cannot affect any other call for the same environment, and the environment recovers as soon
  as the backend does — **asserted on backend request counters, not timings**.
- No MCP-reachable path can wait on Creatio without a bound, including the upload/download/install family.
- A transport or auth failure never surfaces as a domain answer.
- The CLI-first exception in
  [creatio-ai-app-development-toolkit#93](https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/93)
  can be reverted — that PR names this work as its own reversal condition. Its two prohibitions (never
  hand-roll a transport to clio; a timeout means switch transport, not retry) stay regardless.

## 8. Open questions (carried, not blocking Stage 0)

| # | Question | Owner stage |
|---|---|---|
| OQ-1 | Windows child spawn cost and Job Object containment — everything measured so far was macOS | 2 |
| OQ-2 | Memory/CPU ceiling for concurrent children; the supported maximum needs a number | 2 |
| OQ-3 | Cost on a machine where the curated-knowledge bootstrap actually runs its budgeted startup path | 3 |
| OQ-4 | Whether `create-app-section` gets a real operation registry or only a private completion signal | 7 |
| OQ-5 | Whether master ships this default-on or gated — decided at merge proposal, on evidence from this branch | merge |

## 9. Relationship to adjacent ADRs

- **`adr-mcp-durable-invocation.md` (ENG-93370) — adjacent seam; routing must cover BOTH dispatch paths.**
  There are two, and they are not the same place:

  | Path | Seam | Fires for |
  |---|---|---|
  | matched tool | `filters.AddCallToolFilter(McpToolErrorFilter.HandleCallToolErrors)` (`BindingsModule.cs:1160`) | every registered tool name — including `clio-run`, whose *argument* is the inner command |
  | unmatched name | `WithCallToolHandler` → `McpDurableCallToolHandler` (`BindingsModule.cs:165`) | names the SDK did **not** match; `MatchedPrimitive` is `null` |

  The Stage-4 relay's target is the **matched** path, so it does **not** extend
  `McpDurableCallToolHandler` — that handler is a no-op whenever `MatchedPrimitive` is set. But routing
  installed on the matched path alone would leave a hole: a long-tail tool reached through a deprecated
  alias arrives *unmatched*, so it would execute in-process while its canonical sibling runs in a worker.
  **Both seams must route, and they must agree** — exactly the reason `McpReadDeadlineGate` was made a
  single shared authority for both paths rather than duplicated into each.

  The ordering principle still holds inside each path: **name resolution runs before routing** (which
  canonical tool a name means, via `McpToolCompatibilityCatalog`), then the router decides *where* that
  canonical tool executes. Routing on an unresolved alias would key on the wrong name and miss.
- **`adr-read-only-mcp-response-deadline.md` (ENG-93373) — amended, then retired.** Its gate is the
  structural hole described in §1.3. It stays in force until the worker path covers the same tools, and is
  deleted at Stage 10.
- **`adr-mcp-http-credential-passthrough.md` / `adr-mcp-http-standard-authorization.md`** — rule 3 is
  derived from them, not in tension with them: passthrough credentials stay in the parent's `HttpContext`
  and reach the worker over a channel that is neither tool arguments nor a command line.
- **Prior art reused rather than reinvented:** `BoundedOperationStore<T>` + the compile/restart registries;
  `ServiceResponseJsonGuard` (ENG-93365).

## 10. Artifacts

| Artifact | Path |
|---|---|
| Execution metadata inventory | `spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-execution-metadata.md` |
| Cross-call state inventory | `spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-cross-call-state.md` |
| Credential threat model | `spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-credential-threat-model.md` |
| Test plan | `spec/test-plans/tp-mcp-worker-execution-boundary.md` |
| Stories | `spec/stories/story-mcp-worker-execution-boundary-*.md` |
| Architecture explainer (UA) | `spec/mcp-worker-execution-boundary/mcp-worker-execution-boundary-presentation.html` — standalone page, open it in a browser |
| Reproduction lab | branch `spike/eng-95262-lab` (not a merge candidate) |
