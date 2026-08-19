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

### 2.4 Windows measurements (2026-08-17, ts1-core-dev04: Windows Server 2022, 4 cores / 16 GB)

Everything in §2.2 was measured on macOS. These four are the Windows numbers, and the first one contradicts
the cost model stated above.

**Containment — the Job Object approach works, and the assign-after-start race is real.** Two scenarios, a
standalone probe, parent force-terminated (`TerminateProcess`, no cleanup runs):

| Order of operations | Result after the parent is force-killed |
|---|---|
| child created running, **then** assigned to the job | child was in the job (`IsProcessInJob` = true), but the grandchild it spawned before the assignment landed **SURVIVED** |
| `CREATE_SUSPENDED` → `AssignProcessToJobObject` → `ResumeThread` | **whole subtree died** — full containment |

So `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` delivers the guarantee rule 6 asks for, on the condition that the
child is in the job *before it executes a single instruction*. This reproduces the prototype's one leaked
orphan exactly, and it constrains the API: **.NET's `Process.Start` cannot express `CREATE_SUSPENDED`**, so
the supervisor must either P/Invoke `CreateProcess` or assign at creation via
`PROC_THREAD_ATTRIBUTE_JOB_LIST`. "Start it, then assign it" is not an implementation detail — it leaks.

**Spawn cost — 4× the macOS figure.** Child spawn + MCP `initialize`, one warmup discarded, n=8:
**p50 2.763 s** (min 2.519, max 2.904, mean 2.702) against **0.65 s** on macOS.

That moves the per-call tax from ~0.7 s to ~2.8 s on Windows, and the arithmetic in §2.2 with it: the
observed 44-call workload costs **+123 s**, not +31 s. The decision still holds by a wide margin against
38–60 min of timeouts, but the "~0.7 s per call" figure must not be quoted as the cost — it is the macOS
best case. Caveat on the number itself: it was measured invoking `dotnet clio.dll`, the development shape.
Production clio is an apphost (`clio.exe`), which skips muxer resolution, so 2.76 s is an upper bound and
re-measuring against a published apphost is worth doing before Stage 6 sets a default budget.

**Concurrency — the cap is core count, not a constant.** All widths succeeded (no failures, no drops):

| concurrent children | wall | per-call latency min / p50 / max | peak working set |
|---|---|---|---|
| 1 | 2.76 s | 2.74 / 2.74 / 2.74 s | 94 MB |
| 4 (= cores) | 4.53 s | 3.83 / 4.33 / 4.48 s | 334 MB |
| 8 | 8.90 s | 6.08 / 8.18 / 8.89 s | 649 MB |
| 16 | 17.47 s | 8.02 / 15.37 / **16.89** s | 1073 MB |

Wall time grows linearly past the core count, so nothing is gained above ~4 on this box — throughput is flat
and the only thing that changes is per-call latency. Memory is not the binding constraint (1 GB at width 16
of 16 GB available); CPU is.

**This produces a hard design requirement the plan did not state.** At width 16 a call waited **16.9 s** to
reach `initialize` — purely queued behind CPU, with a perfectly healthy backend. A 12 s budget measured from
*enqueue* would have killed it. Therefore: **the budget clock starts when the child is spawned, never when
the call is admitted**, and the concurrency cap is derived from `Environment.ProcessorCount`. Otherwise the
supervisor kills healthy calls for being busy, which is a new failure mode invented by the fix.

**NativeAOT (Stage 8 gate) — CLOSED, green.** `dotnet publish -r win-x64 --self-contained -p:PublishAot=true`
exits **0** in 53.3 s with **zero IL2026/IL3050/IL2104/IL3053 warnings** and zero errors, and produces a
genuine native image: `clio-ring.exe`, 30.8 MB, with **no managed `clio-ring.dll` beside it** — the check that
separates real AOT from an apphost wrapping IL, which a bare exit code would not.

Run on a second Windows host (`A_KRAVCHUK2`, Windows 11 Pro, 32 cores), not on ts1-core-dev04, because
**ts1 cannot install the MSVC toolchain at all**: an ESET TLS filter there re-signs HTTPS with its own CA
(`CN=ESET SSL Filter CA`), the chain does not validate, and the VS installer therefore fails at
`Failed to download the catalog` when reaching `vsblob.vsassets.io`. Not a TLS-version problem — TLS 1.3 was
negotiated and `SchUseStrongCrypto` is already set. Working around it would mean trusting an AV root CA or
excluding Microsoft domains from filtering on a shared stand, which is a security-configuration change and
was deliberately not made. Anything on ts1 that fetches from Azure Edge with a validating client will fail
the same way; run this gate on a host that already carries the C++ workload.

### 2.5 The cost against the FAST cohort, which is the one Stage 6 actually ships

Everything above justifies the spawn cost against operations that take tens of seconds to tens of minutes,
where a 0.7–2.8 s tax rounds to nothing. **Stage 6's cohort is not that.** It is `get-page`, `list-pages`,
`list-app-sections`, `get-schema`, `get-related-page-addon` and the SQL/OData reads — roughly one- to
two-second calls on a healthy stand (§1.2's CLI arm: 42 calls, avg 2.4 s; §1.3: the same command through the
CLI returns in ~1 s). Applying the measured spawn cost to *that* baseline gives a far worse ratio than
anything quoted so far, and the ADR owes the reader the number rather than the reassurance:

| Baseline read | + 0.7 s (macOS) | + 2.8 s (Windows Server 2022, 4 cores) |
|---|---|---|
| ~2 s (§1.2 CLI average) | **+35 %** | **+140 %** |
| ~1 s (§1.3 best case) | **+70 %** | **~+280 %** |

**So state the trade honestly.** On Windows a fast read that took 1 s can take nearly 4 s, every time, for
as long as the tool stays in the worker cohort. That is not a rounding error and it is not hidden by
averages — an agent doing twenty quick reads feels it.

**Three things make it the right trade anyway, and the first is measured rather than argued.**

1. **This cohort's own history is the evidence.** These are exactly the commands agents were forced off MCP
   and onto the CLI. The CLI pays a fresh process start *and* a fresh login on **every** call — and the
   head-to-head measured it at avg 2.3 s with **0 timeouts** for `get-page`, against in-process MCP's avg
   90.2 s with **11 timeouts** in the same minutes on the same stand (§1.2). A per-call process for these
   reads is not a hypothesis about acceptable latency; it is the configuration people already chose while
   the fast path was available to them. The deck states the same thing in one line: this is the price the
   CLI pays always.
2. **The comparison is not "2 s vs 4 s", it is "4 s vs an environment that stays dead until the process
   restarts."** The wedge is not a slow call — it is every later call for that environment cut at the read
   deadline **without issuing an HTTP request at all**, and staying that way after the backend recovers
   (§1). A 140 % tax on a healthy read buys the removal of an unbounded tax on an unhealthy one.
3. **The Windows figure is an upper bound and is expected to fall.** 2.763 s was measured invoking
   `dotnet clio.dll`, the development shape; production clio is an apphost that skips muxer resolution
   (§2.4). Re-measuring against a published apphost before Stage 6 sets a default budget is already noted
   there — this section is a second reason to do it, because the cohort's ratio is where the difference
   shows.

**What this does NOT license.** It does not justify moving *every* read into a worker. The cost is per call
and it is worst where the call is cheapest, so cohort expansion at Stage 10 is a decision to be made against
this table, tool by tool — not a formality once the machinery exists. A read that never wedged anything is a
read that pays this tax for nothing.

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
   destructive and progress-streaming, and ClioRing waits for the authoritative terminal event — which in
   the shipped contract is a stage event with `eventType == "run-completed"` for the run's root `runId`,
   carrying one of `success` / `failure` / `success-with-warnings`; there is **no** `cancelled` outcome, so a
   cancelled run resolves as indeterminate (§3.3). A generic 45–60 s kill could leave a half-installed
   environment. The signalling protocol is specified in §3.3 — without it `terminal-stage` is a label, and
   TC-E-801 could only prove that one implementation happens to pass.
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

### 3.1 Relay properties — RE-MEASURED on SDK 2.2.0 (2026-08-17)

The earlier §3.1/§3.2 numbers were taken on `ModelContextProtocol` 1.4.1, which clio no longer ships. All
three properties were re-run on **2.2.0** (the version `Directory.Packages.props:20` pins), on both target
frameworks, with the 1.4.1 harness as a control. Results:

| Property | 1.4.1 | 2.2.0 | Verdict |
|---|---|---|---|
| Sampling relays to the real client | PASS | **PASS** (121/121 runs) | holds — but see the deprecation below |
| `_meta.clioStageEvent` + `progressToken` fidelity | PASS | **PASS**, byte-identical; a numeric token stays a JSON number | holds |
| Notification ordering through the SDK handler path | FAIL | **FAIL** | **rule 12 stands** |

**Rule 12 is kept on current evidence, not out of caution.** The reordering still reproduces on 2.2.0.

**And the implementation is much cheaper than feared.** Owning the child read loop does **not** require
hand-rolling JSON-RPC or forking the SDK — it is reachable through the public API:

1. `IClientTransport.ConnectAsync(ct)` → `ITransport` (implemented by `StdioClientTransport`)
2. `ITransport.MessageReader` — a `ChannelReader<JsonRpcMessage>`, i.e. the messages in pipe order
3. `ITransport.SendMessageAsync(JsonRpcMessage, ct)`
4. the pattern-matchable `JsonRpcNotification` / `JsonRpcRequest` / `JsonRpcResponse` / `JsonRpcError`
5. `McpJsonUtilities.DefaultOptions` for the payload types, and `McpServer.SampleAsync` to forward upward

The relay therefore **keeps** `StdioClientTransport`'s process spawn, newline framing and serialization, and
skips only `McpClient.CreateAsync` — which is precisely what installs the concurrent notification-dispatch
layer that reorders. What it takes on: the handshake, request-id correlation, and answering child→parent
requests off the read loop. Measured as ~120 lines and **30/30 clean on 2.2.0, 10/10 on 1.4.1**.

This seam is also **byte-identical between 1.4.1 and 2.2.0** in the public API dumps, so it is stable across
the bump rather than a 2.2.0 novelty.

### 3.1a Sampling is deprecated in 2.2.0 — rule 1 now rests on a feature the SDK may remove

Compiling the unchanged harness against 2.2.0 emits four **MCP9005** warnings (zero against 1.4.1):
`ClientCapabilities.Sampling`, `SamplingCapability`, `McpClientHandlers.SamplingHandler` and
`McpServer.SampleAsync` are all `[Obsolete]` — *"the Sampling feature is deprecated as of specification
version 2026-07-28 and may be removed in a future version (SEP-2577)"*.

It still works (121/121 relayed), so rule 1 is implementable today. But the semantic review in `update-page`
/ `sync-pages` is now built on deprecated ground, and the SDK's successor surface already exists:
`InputRequest` / `InputResponse` / `InputRequiredResult` / `InputRequiredException` plus
`McpClient.ResolveInputRequestsAsync`. Carried as **OQ-6**.

### 3.1b The `server/discover` probe, and a trap worth 5 seconds per call

2.2.0 clients probe `server/discover` **before** `initialize` (protocol revision 2026-07-28, SEP-2575).
Measured failure mode: a child that answers the probe with a **success** result of the wrong shape stalls the
handshake for the full `DiscoverProbeTimeout` (default 5 s) and then **hard-fails** with a `JsonException` —
it does **not** fall back to `initialize`. A child answering `-32601` falls back in ~0.05 s.

clio's own worker is a 2.2.0 server and answers the probe correctly, so the happy path is unaffected. The
trap matters for any relay wrapping a non-2.2 child, and it means a malformed discover response is worth
5 s of dead time per call — inside the very budget the parent enforces.

Two more 2.2.0 facts that touch this design:

- **MCP Tasks are gone** (`tasks/get`, `tasks/list`, `tasks/cancel`, the capability family). §2.1 rejected
  them as an option; they are now removed outright, so that decision needs no revisiting.
- **`ping` is not served on protocol 2026-07-28.** A worker liveness probe must not use it. ClioRing already
  moved its health probe to `tools/list` in the same upgrade commit.

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

#### 3.2a The write side needs no gate — and the reason it doesn't is also a retirement rule

*Measured 2026-08-18 by decompiling the shipped assembly
(`~/.nuget/packages/modelcontextprotocol.core/2.2.0/lib/net10.0/ModelContextProtocol.Core.dll`), not
inferred from documentation.*

The reordering above is a **read**-side property, and it was reasonable to ask whether the write side needs a
matching relay-owned send gate. It does not. `StreamClientSessionTransport.SendMessageAsync` already
serialises writes: a `SemaphoreSlim(1, 1)` `_sendLock` is taken for the whole send, and serialization, the
payload write, the newline write and the flush all sit inside it. Two concurrent senders cannot interleave
their bytes on the pipe. **So no relay-side send gate is needed, and adding one would be duplicated
machinery.**

**But the lock guarantees a COMPLETED send, not an ATOMIC one, and that distinction is this feature's own
wedge in miniature.** The same cancellation token is passed to all three awaits inside the lock. Cancel
between the payload write and the newline write — which the budget does, by design — and the lock is
released over an **unterminated line**. The child's reader is line-oriented, so it is still waiting for that
newline.

- For a **per-call** worker this is harmless: the child is killed immediately afterwards and the dangling
  line dies with the pipe.
- For a **sticky** worker it is not. The transport survives, the next writer's JSON is appended to the
  dangling line, and the child receives one corrupt frame and answers nothing. One process, wedged — the
  exact failure this ADR exists to remove, reintroduced by the mechanism that removes it.

**This makes the retirement rule binding rather than tidy: a session whose send did not complete is retired,
never reused.** Any code path that cancels a send on a sticky worker must retire that worker's session, and
"the send threw `OperationCanceledException`" is the signal. Stages 7 and 8 own the sticky pool, so this is
their constraint to honour, not a Stage 4 detail.

#### 3.2b How far a structural guard can enforce rule 12 — corrected 2026-08-18

Rule 12 is guarded structurally today: TC-U-401 walks `Assembly.GetTypes()` restricted to the relay's
namespace and fails if `McpClient` or `McpClientHandlers` appears in any field, property, parameter or
return type (`clio.tests/Command/McpServer/WorkerMcpRelayTests`,
`RelayTypes_ShouldNotReferenceTheSdkClient_WhenInspectedStructurally`, helper `SignatureTypes` — cited by
member name because that file is under active edit and its line numbers moved on 2026-08-18).

An earlier reading called that guard essentially blind, on the grounds that a signature scan cannot see
inside a method body. **That is too pessimistic, and the correction matters because it changes what still
needs building.** A local that crosses an `await` is hoisted by the compiler into a field of the generated
async state machine, and those generated types are nested inside the declaring type — so they carry the
enclosing namespace and `GetTypes()` returns them. The ordinary shape

```csharp
var client = await McpClient.CreateAsync(transport);   // client used after another await
```

therefore leaves a field of type `McpClient` that the existing scan **already catches**.

Two shapes still escape it, and both are escapes from *exact type matching*, not from the idea of a scan:

| Shape | What it leaves in metadata | Why the scan misses it |
|---|---|---|
| `await McpClient.CreateAsync(t)` whose result does not survive an await | `TaskAwaiter<McpClient>` | the check is `forbidden.Contains(pair.Type)` — an exact match, with no walk into generic arguments |
| `_ = McpClient.CreateAsync(t)` | nothing | no local, no field, no signature |

**So closing rule 12 completely needs an IL body scan** (a member-reference walk over method bodies), not a
richer signature scan. Widening the signature check to unwrap generic arguments would close the first row
cheaply and is worth doing; only the second row genuinely requires reading IL. Recording the split here so
that a future implementer neither over-trusts the current guard nor rewrites it believing it catches
nothing.

### 3.2c Admission capacity is a HELD resource, and a sticky lifetime can deadlock on it (recorded 2026-08-18)

This ADR models the concurrency cap against **per-call** workers, where a slot is held for exactly one
answer. Under that model the cap can only ever delay a call. **Stage 7 breaks the assumption**, and the
rule it produces belongs here rather than only in the story, because Stage 10's cohort expansion faces
the same arithmetic.

`WorkerProcessSupervisor.SpawnContainedAsync` acquires a slot from a pool of
`Math.Max(1, Environment.ProcessorCount)` before launching and returns it on lease dispose. A sticky
worker's lease outlives its call by design, so it holds a slot for the whole operation — minutes to an
hour. Stage 7 also requires `compile-status` to reach *that same worker*. If reaching an existing
worker goes through the ordinary spawn path, the poll waits for a slot that is held by the worker it
is trying to reach: **hold-and-wait, not starvation.** It does not resolve under load — the state is
stable until the operation ends unobserved, which the configuration-build reservation's 30-minute
reclaim ceiling bounds at half an hour. On a single-core host the cap is 1, so one sticky worker makes
every later call unreachable, including its own poll.

**The binding rule: admission governs CREATING a worker, never TALKING to one that already exists.** A
call that resolves to a live lease reuses it and takes no slot. The prototype's measured 0.00–0.02 s
poll latency already implies this — a poll that queued for admission could not be that fast — but
"reaches the same worker" does not say it, and an implementer who routes the poll through the spawn
path satisfies the wording and ships the deadlock.

Two consequences follow, and the second is the one that is easy to get subtly wrong:

- Sticky workers draw from **their own pool with its own cap**. `WorkerSlotPool` already anticipates
  this ("a second one with its own cap … an added field and an added `AcquireAsync` call site rather
  than a rewrite of the release path"), and the lease already records which pool to release into.
- The sticky cap must be **strictly less than the total**, leaving per-call work a guaranteed floor.
  Two pools whose caps merely sum to the processor count relabel the exhaustion instead of removing
  it. The sticky cap then also *is* the number of concurrent long operations a host supports — state
  it, and refuse the next one immediately by name rather than after a 60-second queue.

Testing note, because the obvious test passes either way: the discriminating case is a poll issued
while the sticky pool is **saturated**. A poll on an idle host reaches its worker under both the
correct and the deadlocking implementation.

### 3.3 The `terminal-stage` protocol (binding, Stage 8)

Rule 4 says the parent waits for the authoritative terminal stage. That is only enforceable once three
things are fixed: what carries the signal, how long the parent waits for it, and what happens when it never
arrives.

**Channel.** The existing stage-event stream, not a new one. The child already emits
`notifications/progress` carrying `_meta.clioStageEvent` with `(runId, sequence)` — the same events ClioRing
correlates on (rule 1), relayed raw. No named pipe, no second IPC path: a private channel would be a second
contract to keep in sync with the one ClioRing already reads, and the relay is required to be full-duplex
for these tools anyway.

**Detection — corrected 2026-08-18 against the shipped contract.** An earlier draft of this section said a
terminal stage is "a stage event whose `status` is one of the terminal values
(`Completed` / `Failed` / `Cancelled`)". **No such vocabulary exists**, and an implementer matching on it
would write a condition that can never be true. The shipped contract
(`clio/Command/McpServer/Progress/ClioStageEventContract.cs`) is:

| Vocabulary | Members | Where |
|---|---|---|
| `EventTypes` | `manifest` · `stage` · `run-completed` | `:32-42` |
| `StageStatuses` | `running` · `done` · `failed` · `skipped` · `warning` | `:55-71` — **per stage, never terminal** |
| `RunOutcomes` | `success` · `failure` · `success-with-warnings` | `:74-84` |

So the terminal test is: the notification is a progress notification **and**
`_meta.clioStageEvent.eventType == "run-completed"` **and** its `runId` equals the run's root `runId`
(`ClioStageEvent.cs:29-30`, `:105`, `:112`). The outcome is then `runCompleted.outcome`, one of the three
`RunOutcomes`. A per-stage `failed` is **not** terminal — a best-effort run may continue past it, which is
exactly why `warning` and `skipped` exist beside it.

**And the consequence that is not obvious: there is no `cancelled` outcome in the contract at all.** A
cancelled deploy therefore emits **no terminal event**, so it cannot resolve through the terminal route —
it resolves through the *indeterminate* path below, the same as a crashed child. An implementer who goes
looking for a cancellation outcome will not find one, and must not invent one here: adding a fourth
`RunOutcome` is a contract change that ClioRing mirrors (`SchemaVersion`, `:18`), not a local decision.

**Parent-side bound.** Two separate timers, and neither is a total-operation kill:

| Timer | Bound | On expiry |
|---|---|---|
| Terminal-stage wait | no fixed total; the operation may run as long as it streams | — |
| **Stage-event silence** | configurable, default **300 s** with no stage event of any kind | treat as a lost child (below) |
| Post-terminal exit grace | **30 s** between the terminal stage and child exit | kill the child; the operation itself already terminated, so this is safe |

The silence timer, not an operation timer, is what bounds `terminal-stage`. It is the one bound that cannot
truncate a legitimately long deploy: a healthy deploy streams stages continuously, and a child that has gone
30 s past its own terminal stage has nothing left to lose.

**Decided 2026-08-19: there is deliberately no absolute lifetime ceiling, and this is the reasoning.** The
open question the implementation left behind was whether a worker that keeps *emitting* stage events for
ever — a live-locked deploy, not a silent one — should be killed at some outer wall-clock bound. It should
not, and adding one would be a net loss:

- The number would have to be larger than the longest legitimate deploy, which on a slow stand with a large
  bundle is hours, not minutes. A ceiling only ever fires below that number, so the only runs it can reach
  are the ones it is most costly to be wrong about.
- What it would produce on firing is precisely the outcome the rest of §3.3 is built to avoid: a killed
  child mid-stage, an environment marked possibly half-installed, and an operator sent to inspect a deploy
  that was in fact still progressing. Trading an unbounded call for a false half-install is trading a
  visible problem for an invisible one.
- The call is not in fact unattended. A client that gives up sends `notifications/cancelled`, and the
  cancellation path above kills the child and names the last stage reached. The wedge class ENG-95262
  exists for — a call nobody is waiting for that nothing will ever end — needs a caller who has stopped
  listening AND a parent that cannot be told; here the parent is told.
- The blast radius of the remaining case is one worker slot, not the environment. Admission capacity is a
  held resource (§3.2c) and saturation is reported with its own numbers (R-10), so a host losing slots to
  live-locked deploys says so rather than going quiet.

So the honest bound for this family is: handshake, silence, post-terminal grace, and the caller's own token.
A run that streams real progress indefinitely is a defect in the command being run, and it is reported as a
saturated pool — never resolved by killing the deploy on a guess.

**Failure actions — the parent never guesses that a deploy finished.**

- **Child exits without a `run-completed` event** (crash, kill, non-zero exit): the call fails with an
  explicit *indeterminate* error naming the last stage reached, the environment is marked **possibly
  half-installed**, and the parent does **not** retry. Retry-on-ambiguity is how a half-installed
  environment becomes two.
- **Cancellation** takes this same route, by construction rather than by choice — the contract has no
  `cancelled` outcome, so a cancelled run produces no `run-completed` event and is indistinguishable at the
  wire from a child that died. Reporting it as indeterminate is therefore honest, not lazy.

  **Clarified 2026-08-18, after Stage 8 implemented it.** "Resolves through the indeterminate path" is a
  statement about how the run is CLASSIFIED at the wire, not an instruction to synthesise a tool result.
  A client that cancelled sent `notifications/cancelled` and is no longer awaiting a response, so an
  indeterminate *result* would have no recipient. Stage 8 therefore kills the child, emits a warning
  naming the last stage reached, and rethrows `OperationCanceledException` — honouring
  `IMcpWorkerCallDispatcher`'s documented caller-token contract while keeping the half-install
  information recoverable through the channel that still has a reader. The two readings agree on
  substance and differ only in where the information is delivered; recorded here so the code and this
  ADR do not silently disagree.
- **Silence timer expires:** same indeterminate outcome, and the child is killed only *after* the error is
  reported, so its last stage is captured first.
- **`run-completed` arrives, child then hangs:** the exit grace applies; the tool result is the terminal
  outcome, not an error.

The `BudgetPolicy = terminal-stage` value therefore means "bounded by silence and by the `run-completed`
event", never "unbounded". TC-E-801 asserts the mid-deploy case; TC-E-802 asserts the lost-child case, which
is the one that distinguishes this protocol from a generic kill.

**The protocol has one hole that the caller, not the child, opens.** Stage events are forwarded only when
the caller supplied a progress token: `StageEventProgressForwarder.Subscribe` returns an inert subscription
when `progressToken is null`, with the comment "No progress token → the caller did not opt into progress;
forwarding is a pure no-op" (`clio/Command/McpServer/Progress/StageEventProgressForwarder.cs:60-62`, read
2026-08-18). So a client that calls `deploy-creatio` **without** a progress token emits zero stage events —
and a silence-bounded protocol would declare that perfectly healthy deploy a lost child and report it
indeterminate. That is the worst false negative available in this family: it turns a successful deploy into
"possibly half-installed".

A terminal-stage route with no caller progress token therefore **must inject a synthetic progress token on
the child leg**, so the child streams and the parent can see it.

**DECIDED 2026-08-18 (Stage 8): inject the synthetic token on the child leg and CONSUME the resulting
notifications at the relay — do not forward them upward.** This is the one deliberate exception to
rule 1, recorded here as such rather than left to be found in a diff.

The reason is the consumer, not the specification. It is tempting to argue that forwarding progress
under a token the client never supplied is a protocol violation; that was checked against the shipped
SDK (`ModelContextProtocol.Core` 2.2.0) and no such enforcement exists there, so the stronger claim is
NOT made. The verifiable argument suffices on its own: ClioRing always supplies a progress token — its
`ClioStageEventAdapter` correlates on it — so the synthetic path is reached ONLY by a client that
explicitly declined progress. Sending that client a stream it opted out of has no consumer and can only
confuse; suppressing it costs nothing anyone asked for. The child still streams, the parent still sees
every stage, and the terminal event still bounds the call. The exception is confined to the last hop.

**The consequence for the indeterminate result is a cross-repo gap, not a local choice.**
`InstallFormViewModel.DescribeUnstreamedFailure` classifies a no-terminal result in three branches:
`IsError` yields "The install did not complete — clio reported an error"; then a non-zero `exit-code`;
then `success:false` / non-empty `error`. Falling through all three yields "finished without reporting
progress, so I can't confirm it completed".

**None of them is "indeterminate — the environment may be half-installed, do not retry".** The first
three assert the install did NOT complete, which invites exactly the retry this protocol forbids; the
fallback understates a possibly damaged environment. The shipped Ring therefore cannot render this
outcome correctly whatever clio sends, and choosing the least-wrong branch is the whole decision
available on clio's side.

Stage 8 emits `IsError = true` with structured content carrying `success: false`, a non-empty `error`,
and an ADDITIVE `outcome: "indeterminate"` with explicit no-retry guidance. Rationale: the released Ring
then renders its definite-failure message — wrong in wording, safe in effect, because an operator told
the install did not complete inspects the environment, whereas one told nothing assumes success.
`BudgetExpiredErrorClass` must NOT be reused: its shipped guidance says the call is safe to retry, which
for a possibly half-installed environment is the single most damaging instruction available. The
`outcome` field is additive, so no released Ring breaks on it — honouring the "never rely on clio and
Ring being upgraded atomically" rule.

**Follow-up owed to ClioRing, tracked rather than quietly accepted:** Ring needs a fourth branch that
recognises `outcome: "indeterminate"` and says so — possibly half-installed, do not retry, inspect
before reuse. Until it lands, the operator-facing wording for this case is knowingly imprecise, and that
is a stated limitation of Stage 8 rather than a defect in it.

### 3.4 Who drains the worker's standard error (recorded 2026-08-18, as of Stage 6)

The worker's standard error **is** drained — continuously, not read once at the end — and the drain lives in
the **dispatcher** (`McpWorkerCallDispatcher`, nested `WorkerStandardErrorDrain`, bounded by
`StandardErrorTailLimit`), not in the supervisor and not on the lease. *Cited by member name: that file was
under active edit on 2026-08-18 and its line numbers moved within the hour.*

**Draining is liveness, not diagnostics.** A worker that fills its standard-error pipe buffer blocks on the
write and goes silent, which the parent observes as a call that never answers — a hang, attributed to the
stand, caused by clio. The bounded tail is the useful by-product.

**Why it stays in the dispatcher**, stated so the next reader does not "fix" it into the supervisor:

- The **relay never sees the stream.** It is handed a transport built from the worker's standard *input* and
  *output* only; standard error is not part of the MCP channel and must not be, or a stack trace would be
  parsed as a protocol frame.
- The **lease's owner drains it.** The dispatcher is what holds the lease for the duration of a call, so it
  is the component that can start the pump at spawn and stop it before the lease is disposed. Moving the
  drain into the supervisor would mean the supervisor pumping a stream on behalf of a consumer it does not
  otherwise talk to.
- **Promoting the nested drain behind an `IWorkerStandardErrorDrain` interface trips CLIO001.** The analyzer
  treats a `Clio.*` class with a matching `I<Name>` interface as a DI service
  (`Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs:120-130`) and permits `new` on it only
  inside a class whose name — or whose interface's name — ends in `Factory` (`:106-118`). So the "cleaner"
  shape costs a `*Factory` class whose only job is to hand back a per-call pump. It is nested and private
  precisely because it is per-call state, not a service.

**Residual, and it is an ordering constraint rather than a defect.** Exactly **one** lease consumer exists
today — the dispatcher. Stages 7 and 8 add more (a sticky pool, and a deploy child that streams for
minutes), and **each of them must drain too**. A new lease consumer that forgets is not a missing log line;
it is a worker that blocks on a full pipe and is then reported as a stalled backend. Whoever adds the second
consumer should also decide whether the drain becomes shared machinery at that point — with a `*Factory` to
satisfy CLIO001 — rather than a third copy.

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
| ~~5~~ | ~~HTTP credential channel + per-client sticky isolation~~ — **DEFERRED 2026-08-18 (branch owner).** `mcp-http` does not currently work and is a candidate for removal, so building a credential channel for it would be work for a transport that may not survive. The design already anticipated this: the threat model records that on stdio **no secret crosses the channel at all** (the child reads `appsettings.json` itself and receives only the environment *name*), which is why Stage 6's cohort was scoped stdio-only in the first place. **Consequence that must be enforced, not assumed:** the worker path is stdio-only until this stage is revived. A cohort tool routed to a worker over `mcp-http` would need the channel that does not exist, so the transport gate is a correctness requirement of Stage 6, not a nicety. |
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

- **0.7–2.8 s added latency per proxied call** — 0.7 s macOS, 2.8 s Windows Server 2022 (§2.4) — against
  38–60 min of timeouts on the observed workload. Do not quote the 0.7 s alone: it is the macOS best case,
  and on the Stage 6 fast-read cohort the same tax is +35 % to +140 % of the call it wraps (§2.5).
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
| ~~OQ-1~~ | **CLOSED 2026-08-17 on Windows Server 2022 (ts1-core-dev04, 4 cores / 16 GB).** See §2.4. | 2 |
| ~~OQ-2~~ | **CLOSED 2026-08-17.** The cap must be derived from core count, not a constant. See §2.4. | 2 |
| OQ-3 | Cost on a machine where the curated-knowledge bootstrap actually runs its budgeted startup path | 3 |
| OQ-4 | Whether `create-app-section` gets a real operation registry or only a private completion signal | 7 |
| OQ-5 | Whether master ships this default-on or gated — decided at merge proposal, on evidence from this branch | merge |
| OQ-6 | Migration off deprecated sampling (MCP9005 / SEP-2577) onto `InputRequest` / `ResolveInputRequestsAsync` — rule 1 works today but on a feature the SDK may remove | 4 |
| OQ-7 | **9 of the 37 hint-unbounded tools classified `in-process`, so by the cross-field invariant they carry `BudgetPolicy = None` and the parent kill will never bound them.** They are local-only, so nothing waits on Creatio — but `install-toolkit` / `update-toolkit` do network I/O of their own. Decide whether local-but-unbounded needs its own bound, or record why not | 6 |
| OQ-8 | `McpToolSharedFileResource` has no member for the workspace data-binding artifacts (`descriptor.json` / `data.json` / localization, shared by `create-data-binding` and the two row tools) or `.clio-migration/<schema>/manifest.json`. Those rows carry `None` because **no member exists**, not because anyone judged them safe | 9 |
| OQ-9 | **The `mcp-http` question.** Whether `mcp-http` is removed outright or revived. If revived, Stage 5 comes back and the stdio-only gate on the worker path is what must be lifted — deliberately, with the credential channel in place, never by deleting the check | when mcp-http's fate is decided |
| OQ-10 | **The null-result question.** Is a worker's `{"result":null}` a legal empty answer or a worker defect? Traced through the Stage 4a relay: the read loop resolves the pending slot with the response's `Result` verbatim (`WorkerMcpRelay.cs:471-473`), so a null result resolves the awaiter with a null `JsonNode`; `CallToolAsync` then deserialises that node, and deserialising a null node yields `null` rather than throwing (`:224-227`, helper at `:605-612`). The caller therefore receives a **null `CallToolResult`** where it expected either a result or a `WorkerRelayException` — and `ListToolsAsync` takes the identical path. The handshake is already guarded (`:430-436` rejects an `initialize` result with no `protocolVersion`), so this is specifically about tool traffic. Decide the contract before writing a null check: if a null result is illegal, the relay must fault the call with a named relay failure; if it is a legal "no content" answer, the relay must map it to an explicit empty `CallToolResult` and say so, so that "the worker answered nothing" and "the worker answered emptily" stop being the same value | 4 |

**Citation caveat on OQ-10 (2026-08-18).** The `WorkerMcpRelay.cs` line numbers in that row were correct when
it was written against Stage 4a and have since drifted — the file was being edited while this note was added,
and `Deserialize<TResult>` alone moved by more than 130 lines. The *behaviour* described is unchanged; chase
it by member name (`CallToolAsync`, `ListToolsAsync`, the read loop, `Deserialize<TResult>`) rather than by
line, and re-anchor the row when the relay settles.

**Numbering note (2026-08-18).** `OQ-9` was used twice — once for `mcp-http`'s fate and once for the
null-result contract — so the second is now **OQ-10**. Every open question above carries a short bold title
for the same reason: **cite an open question by its content, never by its number alone**, so that a future
duplicate is a naming collision anyone can see rather than an ambiguity nobody notices. Existing references
to the `mcp-http` question keep `OQ-9` and are unaffected; references to the null-result question are the
ones that move.

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
| Architecture explainer (UA) | [`docs/architecture/mcp-worker-execution-boundary.html`](../../docs/architecture/mcp-worker-execution-boundary.html) — a self-contained slide deck; open it in a browser |
| Reproduction lab | branch `spike/eng-95262-lab` (not a merge candidate) |
