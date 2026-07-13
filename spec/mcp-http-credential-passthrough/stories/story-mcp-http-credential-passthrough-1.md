# Story 1: [SPIKE] Per-request context flow (RISK #1)

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-04 (per-request context transport), resolves OQ-04; probes the FR-06 concurrency prerequisite
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (Implementation Plan step 1; OQ-04)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: S (spike — timebox 1 day)
**Depends on**: —
**Blocks**: ALL downstream code stories (3–15). This spike is BLOCKING — no downstream story may start until it resolves.

---

## As a

architect / senior developer

## I want

to prove that a per-request credential context set by `mcp-http` HTTP middleware is readable back inside MCP tool execution via a singleton `ICredentialContextAccessor` (over `IHttpContextAccessor`), under `ValidateOnBuild`+`ValidateScopes`

## So that

FR-04 (and every credential-passthrough step that depends on flowing the context into `ToolCommandResolver`) is built on a verified seam rather than the RISK #1 assumption, and FR-06's AsyncLocal capture-isolation strategy is known to be sound

---

## Spike Questions (answer all)

- [x] **Q1** — With clio's current `.WithHttpTransport()` call (no options — `McpHttpServerCommand.cs:66-68`), does the MCP AspNetCore SDK invoke tool handlers on the originating HTTP request's `ExecutionContext` (i.e. are the Streamable HTTP defaults `EnableLegacySse=false` / `PerSessionExecutionContext=false` actually in effect)? **YES** — runtime-verified + source-decisive. Defaults confirmed (`EnableLegacySse=false` line 842, `PerSessionExecutionContext=false` line 893); mechanism `set_FlowExecutionContextFromRequests(!PerSessionExecutionContext)` → `ExecutionContext.Capture()` (Core 33709) → `ExecutionContext.Run` (Core 22591).
- [x] **Q2** — Does a singleton `ICredentialContextAccessor` backed by the singleton, `AsyncLocal`-backed `IHttpContextAccessor` (via `AddHttpContextAccessor()`) return the value set in a marker middleware when read back inside a tool invocation, with the host's `ValidateOnBuild=true`+`ValidateScopes=true` (`McpHttpServerCommand.cs:51-60`)? Confirm it does NOT throw at build time. **YES** — runtime-verified; read-back correct, no `ValidateOnBuild` exception (singleton→singleton edge cannot trip `ValidateScopes`).
- [x] **Q3 (FR-06 concurrency prerequisite)** — Fire two concurrent tool calls; confirm each runs on an **independent async flow** (an `AsyncLocal<T>` set inside invocation A is not observed by invocation B). **YES, independent** — runtime-verified (forced overlap via `Barrier(2)`; each invocation read only its own `AsyncLocal`). FR-06 may use `AsyncLocal` on the singleton `ConsoleLogger`/`DbOperationLogContextAccessor`, **provided** the scope is opened inside the tool-execution boundary (`BaseTool.InternalExecute`), not in middleware. No per-invocation capture object required.
- [x] **Q4** — If Q1/Q2 fail … fallback seam? **Not triggered** (Q1/Q2 passed). Fallback documented for completeness: the SDK's per-request DI scope (`HttpContext.RequestServices`) at the endpoint boundary; re-targeting would touch steps 4, 7, 10 and FR-19 enforcement. The step-15e assertion is the drift tripwire.

## Deliverables

- [x] A findings note at `spec/mcp-http-credential-passthrough/context-flow-spike-findings.md` answering Q1–Q4 with evidence (throwaway marker-middleware + accessor readback probe **and** decompiled SDK source capture).
- [x] Go/no-go on the `IHttpContextAccessor`-behind-singleton-seam design: **GO (a) confirmed** → stories 4/7/10 build the real `ICredentialContextAccessor`. No fallback needed.
- [x] Concrete verdict on the FR-06 async-flow assumption (Q3): **AsyncLocal-on-singleton OK** (scope opened inside the tool-execution boundary) → feeds Story 9.
- [x] step-15e transport-default assertion target specified: assert `IOptions<HttpServerTransportOptions>` `EnableLegacySse==false` / `PerSessionExecutionContext==false` / `Stateless==false`; **recommend** additionally pinning `WithHttpTransport(o => { o.EnableLegacySse=false; o.PerSessionExecutionContext=false; })` so the assumption cannot silently drift.

## Implementation Notes

- Grounding: `clio/Command/McpServer/McpHttpServerCommand.cs` (host, `.WithHttpTransport()` at ~66-68; `ValidateOnBuild`/`ValidateScopes` at ~51-60). `IHttpContextAccessor` is a singleton whose `_httpContextCurrent` is `AsyncLocal<...>`.
- Build only a throwaway `ICredentialContextAccessor` + marker middleware for the spike; do NOT ship production parsing/gating here (those are stories 4/5). Any code kept must satisfy CLIO001/CLIO005 and register via `BindingsModule`.
- Do not use raw `HttpClient`; this spike touches only the HTTP host wiring, not the Creatio client.

## Definition of Done

- [x] Q1–Q4 answered with evidence (runtime probe + decompiled SDK source; markers noted per answer)
- [x] Go/no-go recorded for the singleton `ICredentialContextAccessor` seam (**GO**); FR-06 async-flow verdict recorded for Story 9 (**AsyncLocal-on-singleton OK**)
- [x] If the seam fails, the fallback (per-request DI scope) and re-targeted downstream steps are documented (seam did NOT fail; fallback documented for completeness)
- [ ] ADR RISK #1 / OQ-04 section updated with the outcome — **deferred to orchestrator** (recommended update text supplied in the spike return; ADR not edited by the spike agent per task instruction)
- [x] No production credential-passthrough code merged from this spike beyond the verified seam scaffold — **nothing kept in the repo**; the probe is throwaway and lives entirely under the scratchpad (outside the repo), so there is no scaffold to keep CLIO001/CLIO005-clean or register in `BindingsModule`

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: N/A (investigation spike — no repo test project added). Throwaway runtime probe under scratchpad passed all 3 checks (Q1/Q2 read-back, Q2 build-validation, Q3 independent flows).
- Notes:
  - SDK decompiled with `ilspycmd 10.1.0.8386` (`ModelContextProtocol.AspNetCore` / `.Core` / meta-package, all 1.4.0). Required `DOTNET_ROOT=~/.dotnet` for the tool to launch on macOS.
  - Decisive source refs: `PerSessionExecutionContext` default-false + doc contract (AspNetCore 883/885-891); `set_FlowExecutionContextFromRequests(!PerSessionExecutionContext)` (AspNetCore 1898/1947); `ExecutionContext.Capture()` (Core 33709); `ExecutionContext.Run` handler dispatch (Core 22591); loop fires without awaiting `_ = ProcessMessageAsync()` (Core 22593) ⇒ genuine concurrency.
  - Runtime probe (`<scratchpad>/context-flow-probe/`) booted the real SDK host on loopback, drove initialize→session→`tools/call` via the SDK's own `HttpClientTransport`+`McpClient`, two calls with distinct `X-Marker` headers fired without awaiting, `Barrier(2)` forced handler overlap. All pass.
  - **ADR edit intentionally NOT made** by this spike agent; recommended RISK #1 / OQ-04 update text handed to the orchestrator.
  - Findings: `spec/mcp-http-credential-passthrough/context-flow-spike-findings.md`.
