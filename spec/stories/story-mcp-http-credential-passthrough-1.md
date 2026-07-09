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

- [ ] **Q1** — With clio's current `.WithHttpTransport()` call (no options — `McpHttpServerCommand.cs:66-68`), does the MCP AspNetCore SDK invoke tool handlers on the originating HTTP request's `ExecutionContext` (i.e. are the Streamable HTTP defaults `EnableLegacySse=false` / `PerSessionExecutionContext=false` actually in effect)?
- [ ] **Q2** — Does a singleton `ICredentialContextAccessor` backed by the singleton, `AsyncLocal`-backed `IHttpContextAccessor` (via `AddHttpContextAccessor()`) return the value set in a marker middleware when read back inside a tool invocation, with the host's `ValidateOnBuild=true`+`ValidateScopes=true` (`McpHttpServerCommand.cs:51-60`)? Confirm it does NOT throw at build time.
- [ ] **Q3 (FR-06 concurrency prerequisite)** — Fire two concurrent tool calls; confirm each runs on an **independent async flow** (an `AsyncLocal<T>` set inside invocation A is not observed by invocation B). This decides whether FR-06 capture isolation can use `AsyncLocal` on the singleton `ConsoleLogger`/`DbOperationLogContextAccessor`, or must fall back to a per-invocation capture object resolved from the per-call child container.
- [ ] **Q4** — If Q1/Q2 fail (context returns null or the SDK does not run tools on the request context): what is the fallback seam (the SDK's per-request DI scope), and which downstream steps (4, 7, 10, 19-enforcement) must re-target it?

## Deliverables

- [ ] A findings note appended to the ADR (OQ-04 / RISK #1 section) or `spec/mcp-http-credential-passthrough/context-flow-spike-findings.md` answering Q1–Q4 with evidence (a throwaway marker-middleware + accessor readback assertion, or SDK source/behavior capture).
- [ ] A go/no-go on the `IHttpContextAccessor`-behind-singleton-seam design: either (a) confirmed → stories 4/7/10 build the real `ICredentialContextAccessor`; or (b) failed → record the per-request-DI-scope fallback and flag the re-targeted seam for downstream stories.
- [ ] A concrete verdict on the FR-06 async-flow assumption (Q3) feeding Story 9's capture-isolation approach.
- [ ] Confirm (or write) the step-15e transport-default assertion target so the RISK #1 assumption cannot silently drift.

## Implementation Notes

- Grounding: `clio/Command/McpServer/McpHttpServerCommand.cs` (host, `.WithHttpTransport()` at ~66-68; `ValidateOnBuild`/`ValidateScopes` at ~51-60). `IHttpContextAccessor` is a singleton whose `_httpContextCurrent` is `AsyncLocal<...>`.
- Build only a throwaway `ICredentialContextAccessor` + marker middleware for the spike; do NOT ship production parsing/gating here (those are stories 4/5). Any code kept must satisfy CLIO001/CLIO005 and register via `BindingsModule`.
- Do not use raw `HttpClient`; this spike touches only the HTTP host wiring, not the Creatio client.

## Definition of Done

- [ ] Q1–Q4 answered with evidence
- [ ] Go/no-go recorded for the singleton `ICredentialContextAccessor` seam; FR-06 async-flow verdict recorded for Story 9
- [ ] If the seam fails, the fallback (per-request DI scope) and re-targeted downstream steps are documented
- [ ] ADR RISK #1 / OQ-04 section updated with the outcome
- [ ] No production credential-passthrough code merged from this spike beyond the verified seam scaffold (if kept: CLIO001/CLIO005 clean, DI via `BindingsModule`)

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
