# PRD: AI-Assisted Business Process Generation via clio MCP (Approach 3 / Variant A)

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-06-12
**Jira**: ENG-90883 (research) — implementation ticket TBD

---

## Problem Statement

A Creatio developer who wants to automate something today must open the classic
Process Designer and hand-build the BPMN diagram element by element — knowing which
element maps to which intent, how elements legally connect, and how to fill each
setup card. An AI agent connected via clio MCP can describe the intent but has no
deterministic way to turn it into a working process. The 2026-06-12 research
(ENG-90883) concluded the AI must **not** generate process metadata directly
(serialization belongs to the designer); instead clio should expose deterministic
tools and a guidance prompt so the agent translates intent → BPMN and lets the live
Process Designer own the persistence. This increment delivers the first thin vertical
slice of that capability so the workflow can be proven end-to-end against a live
environment.

## Goals

- [ ] Goal 1 — Let an AI agent translate a plain-language automation request into a
  validated BPMN plan **without clio making any LLM call**.
  Success metric **SM-01**: the agent can fetch the `process-modeling` guidance via
  `get-guidance` and, for the slice's covered intents (Simple/Signal/Timer start +
  Read data), produce a node/edge plan that `validate-process-graph` accepts with
  zero errors in a single round. / Counter: clio adds **0** LLM/network calls of its
  own to any third-party model service (verified by absence of such dependencies in
  the slice code and by network inspection during E2E).
- [ ] Goal 2 — Give the agent fast, deterministic pre-build feedback so it never
  draws an invalid graph.
  **SM-02**: `validate-process-graph` returns structured findings (errorId R1–R17)
  for every rule violation listed in the validator spec; a graph that the live
  designer accepts validates clean, and a graph the live designer rejects produces
  at least one matching `error` finding. / Counter: false-positive rate — a graph the
  live designer accepts must **never** be reported as `error` by the validator (only
  `warning` is permitted for advisory rules R12/R17).
- [ ] Goal 3 — Build and persist a real "Read data" element in the live Process
  Designer through deterministic clio driving (Variant A / CDP), verified by readback.
  **SM-03**: `process-add-element` for Read data, run against a fresh process,
  produces a `Start → Read data → End` process that the platform saves
  ("Successfully saved"), and the result is confirmed via `generate-process-model` /
  `execute-esq` readback (a `VwProcessLib` row exists). / Counter: the driver must
  **never** report success when SAVE failed or the designer flagged the connection
  with `.djs-validate-outline` (no false-positive save).
- [ ] Goal 4 — Let the AI **read & explain** an already-built process (the inverse of
  generation; the research's "read & explain" quick win).
  **SM-04**: for any existing process, the agent can call `describe-process` and
  receive a structured graph (elements + flows + process-level parameters) it can
  narrate in plain language using the `process-modeling` guidance — reusing the
  already-parsed schema (backend ≈ 0). / Counter: the response is structured JSON
  (elements/flows/parameters), never the raw escaped `metaData` string.

## Non-goals

- Will NOT make clio call any LLM or AI service. clio ships **guidance + deterministic
  tools** only; intent→BPMN translation is done by the calling agent.
- Will NOT generate or write process schema metadata/XML directly (the research
  explicitly rejects this — serialization is delegated to the designer). clio drives
  the visual designer, it does not author the `.bpmn`/schema payload.
- Will NOT add a Playwright dependency or a headless CI harness in this increment.
  Variant A reuses the existing `AuthenticatedBrowserLauncher` (CDP) only. A
  Playwright/CI productization path is future work.
- Will NOT cover process elements beyond the slice. Out of scope for this increment:
  all `addDataUserTask`/`changeDataUserTask`/`deleteDataUserTask`/`formulaTask`/
  `scriptTask`/`webService`/`callActivity`/user-action tasks, gateways
  (exclusive/parallel/inclusive/event-based), intermediate catch/throw events, and
  process **parameters/mapping/formulas/filters**. The guidance resource may *describe*
  them for context, but only Read data is driven.
- Will NOT implement transactional/rollback semantics across multiple element
  additions. The slice adds one element; multi-element orchestration and a
  `process-save`/`process-undo` story are future work (recovery is best-effort, see NFRs).
- Will NOT support environments where clio cannot obtain a forms-auth browser session
  (carried over from the browser-session-handoff scope — OAuth-only environments are
  unsupported here).
- Will NOT add filter-builder, sort, or column-selection automation on the Read data
  setup card beyond selecting the source object (a thin slice — see FR-08 scope note).
- Will NOT decode element **filters and mapping** in `describe-process` v1 (the
  heavily-escaped `FilterGroup`/`ParameterExpression` JSON). `describe-process` v1
  returns structure + element types + flows + basic params only; deep human-readable
  interpretation of filter/mapping expressions is future work (see FR-19 limitation).

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| developer (via AI agent / MCP) | to describe "read the Contact record" in plain language and have the agent build a working Read-data step in my live Process Designer | I get a runnable automation without hand-drawing BPMN or memorising element types |
| AI agent (MCP client) | to read the `process-modeling` guidance via `get-guidance` | I learn the Creatio element catalog, connection rules, and the build recipe before I act, instead of guessing |
| AI agent (MCP client) | to call `validate-process-graph` on my planned node/edge graph before driving the designer | I catch invalid connections (R1–R17) cheaply and never draw a graph the designer would reject |
| AI agent (MCP client) | to call `process-add-element` to append and configure a Read data element | clio deterministically drives the visual designer (open authenticated → append → configure object → save) so I do not have to puppeteer the canvas myself |
| AI agent (MCP client) | to call `describe-process` on an already-built process and get a structured graph (elements/flows/parameters) | I can explain in plain language what an existing process does — the inverse of generation — without parsing the raw escaped metadata myself |
| developer / QA engineer | to verify the built process via `generate-process-model` / `execute-esq` | I have proof the process exists in the environment, not just a "saved" message from the UI |

## Feature Requirements

### Slice item 1 — `process-modeling` MCP guidance resource

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Add a guidance resource `ProcessModelingGuidanceResource` under `clio/Command/McpServer/Resources/` following the `DataBindingsGuidanceResource` pattern (a `[McpServerResourceType]` with a `TextResourceContents Guide`), and register it in `GuidanceCatalog` under the canonical name **`process-modeling`** so the agent reaches it via `get-guidance --name process-modeling`. | Must |
| FR-02 | The guidance content must consolidate the three research source docs as the single AI-facing reference: the BPMN + Creatio **element catalog** (per-element `data-id`, label, purpose, setup-card field codes, outputs — from `ai-bp-element-catalog.md`), the **connection rules** R1–R17 and the can/can't matrix (from `ai-bp-connection-rules.md`), and the **build recipe** (intent→element→append→morph→configure→connect→validate→save — from `ai-bp-ui-playbook.md` §6). It must state explicitly that clio does not call an LLM and that the agent owns translation. | Must |
| FR-03 | The guidance must instruct the agent to call `validate-process-graph` **before** driving the designer, and to treat the live designer's `.djs-validate-outline` as the final authority over the static validator. | Must |
| FR-04 | The guidance must scope the agent to the supported slice (Simple/Signal/Timer start + Read data) and clearly mark every other element/flow/gateway as "described for context, not yet drivable by clio" so the agent does not attempt unsupported automation. | Must |

### Slice item 2 — `ProcessGraphValidator` + `validate-process-graph` MCP tool

| ID | Requirement | Priority |
|----|------------|---------|
| FR-05 | Implement `ProcessGraphValidator` (C#, behind an `IProcessGraphValidator` interface, registered in `BindingsModule.cs`, constructor-injected — no `new`, no MediatR). Input = a planned graph: nodes `{id, type(data-id)}` + edges `{source, target, flowKind ∈ sequence \| conditional \| default}`. Output = structured findings `{severity (error\|warning), ruleId (R1–R17), message, node/edge}`. | Must |
| FR-06 | The validator must reuse `clio/Command/ProcessModel/Schema.cs` `ManagerMap.EventType` to classify node types (start/end/activity/gateway/intermediate) rather than re-deriving the taxonomy, so the validator and the model reader agree on element classification. | Must |
| FR-07 | The validator must emit the **errors** and **warnings** enumerated in the validator spec: errors for R1 (start incoming / ≠1 outgoing), R2 (end outgoing / end-as-source / edge to-or-from a missing node), R3 (no start or >1 start), R10, R11, R13, R14 (default with no sibling conditional), R15 (orphan / cannot-reach-end); warnings for R7/R9 (diverging exclusive/inclusive missing default), R12 (multiple outgoing sequence = implicit parallel), R17 (`addDataUserTask`→consumer without intervening `readDataUserTask`). Rules whose elements are out of the driving slice are still validated (the agent may plan them). | Must |
| FR-08 | Expose `validate-process-graph` as an MCP tool (`BaseTool`, `ReadOnly = true`, `Destructive = false`, `Idempotent = true`) in `clio/Command/McpServer/Tools/`. It is **not** environment-sensitive (pure in-memory analysis) so it uses the direct `InternalExecute(options)` path. The slice driver (`process-add-element`) must also call the validator internally before SAVE. | Must |

### Slice item 3 — `process-add-element` (Variant-A CDP driver) for Read data

| ID | Requirement | Priority |
|----|------------|---------|
| FR-09 | Implement `process-add-element` as a `Command<TOptions>` + DI tool that drives the **existing** visual Process Designer over CDP. It must reuse and extend `clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs` (launch local Chromium with `--remote-debugging-port`, inject `get-browser-session` cookies, navigate) — **no Playwright dependency**. The extension adds a CDP `Runtime.evaluate` capability to run the proven JS recipe; keep CDP plumbing in the BrowserSession layer behind an interface, not inlined in the command. | Must |
| FR-10 | For this increment the tool supports **one element type: Read data** (`readDataUserTask`). It packages the proven PoC recipe: open the authenticated designer via CDP, poll for `.djs-shape`, select the source shape via the `pointer-events:none` overlay + trusted click, append from the source context pad (`add.serviceTask`, which defaults to `readDataUserTask` and auto-inserts onto the existing flow), then configure the setup card's "Which object to read data from?" lookup with the requested object, then SAVE. | Must |
| FR-11 | Tool inputs (MCP + CLI): the target environment, the target process (a new process or an existing process id), and the **object to read** (e.g. `Contact`). All CLI option long-names must be kebab-case (CLIO001) — e.g. `--read-object`, `--process-id`, `--element-type`. Provide a hidden alias only if renaming an existing option. | Must |
| FR-12 | Expose `process-add-element` as an MCP tool (`BaseTool`, `ReadOnly = false`, `Destructive = false` for a new process / **`Destructive = true` when modifying an existing saved process**, `Idempotent = false`). Because it is environment-sensitive (environment name + browser session), it must use the environment-aware `InternalExecute<TCommand>(options)` path per the MCP `BaseTool` rules, not the startup-time injected command. | Must |
| FR-13 | Before SAVE, the driver must (a) run `ProcessGraphValidator` on the resulting planned graph and abort with a structured error on any `error` finding, and (b) after `connect`/append, assert the connection element does **not** carry `.djs-validate-outline`; if present, revert/abort and report (the designer is the final authority). | Must |
| FR-14 | The tool must verify the save deterministically: it must detect the platform's "Successfully saved" signal AND surface the saved process identity (code/UId) so the caller (and E2E) can read it back via `generate-process-model` / `execute-esq` on `VwProcessLib`. The tool must NOT report success on a missing save signal, a CDP error, or a flagged invalid connection. | Must |
| FR-15 | Failure handling: the command must surface user-friendly `Error:` messages (no stack traces) for each failure class — Chromium not found (reuse `ChromiumNotFoundException`), no forms-auth session available, designer never rendered (`.djs-shape` timeout), object lookup not found, append/connect rejected, SAVE failed/validation dialog. Best-effort recovery only (see NFR-04). | Must |

### Slice item 4 — `describe-process` (read & explain — the inverse of generation)

The ENG-90883 research notes a **"read & explain" quick win** that is symmetric with generation:
before (or instead of) building, an AI agent should be able to **read an already-built process** and
explain what it does. The full schema is **already parsed internally** by the existing read path
(`ProcessSchemaRequest` → `clio/Command/ProcessModel/ProcessModelGenerator.cs` → `Schema.cs`
`ProcessSchemaResponse`/`FlowElement`/`FlowElementParameter`/`ManagerMap`). Today
`generate-process-model` only exposes process-level **parameters**; this slice item **exposes the
element graph + flows** from the same parsed object (backend ≈ 0 — no new I/O path), using the same
`data-id` vocabulary as generation so it stays symmetric with the validator and the `process-modeling`
guidance.

| ID | Requirement | Priority |
|----|------------|---------|
| FR-19 | Add a `describe-process` MCP tool (`BaseTool`, `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`) **and** a `Command<TOptions>` + DI CLI verb that reads an existing process by **code / UId / caption** (+ `environment-name`) and returns a **structured graph** the agent can narrate in plain language using the `process-modeling` guidance (Slice item 1): `elements` `[{id, dataId/type, label, key params}]`, `flows` `[{source, target, kind ∈ sequence \| conditional \| default}]`, and process-level `parameters`. It **reuses** the existing `ProcessSchemaRequest` parsing and the `ManagerMap.ResolveDataId`/role helper (FR-06) to label element types — no new schema-read path, no MediatR, kebab-case flags (CLIO001). Because it reads via `IApplicationClient` against the environment, the MCP tool is **environment-sensitive** → `InternalExecute<DescribeProcessCommand>(options)`. Output is structured JSON (elements/flows/parameters), **not** the raw escaped `metaData`. A prompt aligned to the tool contract reuses the existing `process-modeling` resource (no new resource). **v1 out of scope (future work):** deep human-readable interpretation of element **filters and mapping** (the heavily-escaped `FilterGroup`/`ParameterExpression` JSON in `FlowElementParameter`/`ConditionExpression`) — v1 returns structure + types + flows + basic params only. | Must |

### Cross-cutting (apply to all four slice items)

| ID | Requirement | Priority |
|----|------------|---------|
| FR-16 | Unit tests `[Category("Unit")]` (NUnit / FluentAssertions / NSubstitute, AAA + `because` + `[Description]`, naming `Method_ShouldX_WhenY`): validator rule coverage (one test per R-rule error/warning), tool argument mapping for the new tools, guidance-catalog registration of `process-modeling`, and `describe-process` graph extraction from a sample `ProcessSchemaResponse` (reuse `clio.tests/Examples/ProcessSchema/*.json` fixtures). Command tests prefer `BaseCommandTests<TOptions>`. | Must |
| FR-17 | MCP E2E tests in `clio.mcp.e2e/` for `validate-process-graph`, `process-add-element` (incl. the live build-and-readback of `Start → Read data → End`), and `describe-process` (live read of a known process) — flagged: MCP E2E is **not in CI**; the `process-add-element` E2E requires Chromium + a live forms-auth Creatio env. Per MCP maintenance policy this is mandatory even though it cannot run in CI. | Must |
| FR-18 | Documentation per the command/MCP maintenance policy: `help/en/process-add-element.txt`, `docs/commands/process-add-element.md`, `help/en/describe-process.txt`, `docs/commands/describe-process.md`, entries in `Commands.md`, and `docs/McpCapabilityMap.md` updated for `validate-process-graph`, `process-add-element`, and `describe-process`. State "MCP reviewed" for `generate-process-model`/`execute-esq` (readback/parsing reused, not changed). | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New verb | `process-add-element` | No |
| New flag | `--element-type` (slice: only `read-data` accepted) | No |
| New flag | `--read-object` (object to read, e.g. `Contact`) | No |
| New flag | `--process-id` (existing process id; omit to create a new process) | No |
| New verb | `describe-process` (read an existing process → structured graph) | No |
| New flag | `--process-code` / `--process-uid` / `--process-caption` (process identity; exactly one) | No |
| New flag | `--culture` (localized labels/captions; default `en-US`) | No |
| New flag | `-e` / `--environment` (standard environment selector, reused) | No |
| New MCP tool (no CLI verb) | `validate-process-graph` (in-memory analysis; MCP-only surface, no environment) | No |
| New MCP tool | `describe-process` (env-sensitive read; ReadOnly) | No |
| New MCP resource | `process-modeling` guidance via `get-guidance` | No |
| Reused (unchanged) | `generate-process-model`, `execute-esq` for readback verification; `ProcessSchemaRequest` parsing reused by `describe-process` | No |

All flags: **kebab-case only** (CLIO001 enforced). No camelCase/PascalCase option names.

## Acceptance Criteria

- [ ] AC-01: Given the MCP server is running, when the agent calls
  `get-guidance --name process-modeling`, then it receives the consolidated guidance
  (element catalog + R1–R17 rules + build recipe) and the text states that clio makes
  no LLM call and that the agent owns intent→BPMN translation.
- [ ] AC-02: Given a valid planned graph for `Start → Read data → End`
  (one start, one `readDataUserTask`, one end, two sequence edges), when the agent
  calls `validate-process-graph`, then the result has **zero `error` findings** and
  exit/status is success.
- [ ] AC-03: Given a graph whose start event has an incoming flow, when
  `validate-process-graph` is called, then the result contains an `error` finding with
  `ruleId = "R1"` identifying the offending node/edge.
- [ ] AC-04: Given a graph with a default flow that has no sibling conditional flow,
  when `validate-process-graph` is called, then the result contains an `error` with
  `ruleId = "R14"`.
- [ ] AC-05: Given a graph with an orphan node that cannot reach any end event, when
  `validate-process-graph` is called, then the result contains an `error` with
  `ruleId = "R15"`.
- [ ] AC-06: Given a node graph the live designer accepts, when validated, then the
  validator returns **no `error`** (advisory `warning` for R12/R17 is permitted) —
  no false positives.
- [ ] AC-07: Given a registered forms-auth environment and a fresh process, when the
  agent calls `process-add-element --element-type read-data --read-object Contact`,
  then clio opens the authenticated designer via CDP, appends a Read data element onto
  the Start→End flow, configures the "Which object to read data from?" lookup to
  Contact, SAVEs, and reports success with the saved process code/UId.
- [ ] AC-08 (live readback): Given AC-07 succeeded, when `generate-process-model --code <code>`
  is run against the same environment, then it exits 0 and emits
  `[BusinessProcess("<code>")]`; and `execute-esq` on `VwProcessLib` filtered by the
  process caption returns exactly one row for the new process.
- [ ] AC-09: Given the append/connect produced a connection the designer flagged with
  `.djs-validate-outline`, when `process-add-element` runs, then it does **not** SAVE,
  does **not** report success, and returns an `Error:` naming the invalid connection.
- [ ] AC-10: Given the validator reports an `error` on the planned graph, when
  `process-add-element` runs, then it aborts before driving the designer and returns
  the validator finding(s) — no browser is opened.
- [ ] AC-11: Given the MCP capability map, when the PR merges, then
  `docs/McpCapabilityMap.md` lists `validate-process-graph` (ReadOnly) and
  `process-add-element` with correct safety flags.
- [ ] AC-12: Given `dotnet test --filter "Category=Unit&Module=Command"` (and
  `Module=McpServer`), when run, then all validator-rule, argument-mapping, and
  guidance-registration unit tests pass.
- [ ] AC-13 (read & explain readback narration): Given a registered environment and an
  existing process identified by code / UId / caption, when the agent calls
  `describe-process`, then it receives a structured JSON graph with `elements`
  `[{id, dataId/type, label, key params}]`, `flows`
  `[{source, target, kind ∈ sequence \| conditional \| default}]`, and process-level
  `parameters` — labelled with the same `data-id` vocabulary as generation (via
  `ManagerMap.ResolveDataId`) so the agent can narrate, using the `process-modeling`
  guidance, what the process does. The response is structured JSON, **not** the raw
  escaped `metaData`; v1 does not interpret filter/mapping expressions.
- [ ] AC-ERR: Given an environment for which no forms-auth browser session can be
  obtained, or Chromium is not installed, when `process-add-element` is called, then
  clio prints `Error: {specific message}` (e.g. "Error: a forms-auth browser session
  is required to drive the Process Designer for environment '<env>'" /
  "Error: Chromium not found …") and exits non-zero — no partial/blank designer is
  left and no success is reported. Likewise, given a process code/UId/caption that does
  not exist (or an unreachable environment), when `describe-process` is called, then
  clio prints `Error: {specific message}` (e.g. "Error: process '<code>' not found in
  environment '<env>'") and exits non-zero — no partial/blank structure is emitted.

## Non-functional Requirements

- [ ] NFR-01 (auth dependency): The driver depends on a **forms-auth** browser session
  obtained through the existing `get-browser-session` / `AuthenticatedBrowserLauncher`
  path. OAuth-only environments are unsupported (carried from browser-session-handoff
  scope) and must fail closed with AC-ERR.
- [ ] NFR-02 (local Chromium dependency): Requires a locally installed Chromium located
  by `IChromiumLocator`; absence yields the canonical `ChromiumNotFoundException` → AC-ERR.
  Headed launch is the default for this increment; headless support is noted as future
  work (the QA-proven untrusted append drag and the trusted-click overlay trick are
  validated headed — headless parity is unverified and out of scope).
- [ ] NFR-03 (render timing): The SVG canvas + palette render seconds after the metadata
  card; the driver must poll for `.djs-shape` (or `.entry[data-action="create-serviceTask"]`)
  before interacting and bound the wait with a timeout, failing fast with AC-ERR if the
  canvas never renders. It must also dismiss any stray `.djs-popup.diagram-create-popup-menu`
  (Escape) before the overlay click.
- [ ] NFR-04 (non-transactional recovery): A single `process-add-element` call is **not
  transactional**. If it fails after appending but before SAVE, the unsaved designer
  state is discarded (browser closed); clio reports the failure and leaves no saved
  partial process. If it fails after SAVE detection, the saved process is reported so
  the caller can inspect/delete it. clio must never report success on a partial/failed run.
- [ ] NFR-05 (UI fragility mitigation): The designer is diagram-js/bpmn-js inside the Ext
  shell; element type = SVG CSS class / `data-id`. The driver must (a) target stable
  `data-id`/`data-action` selectors from the element catalog, (b) cross-check every new
  connection against `.djs-validate-outline` (the designer's own live rule engine) rather
  than trusting the static validator alone, and (c) verify via clio readback (AC-08) — never
  trust the UI "Successfully saved" toast as sole proof. Selector drift across Creatio
  versions is an accepted risk (R-03) mitigated by the readback gate and the catalog being
  the single source of selectors.
- [ ] NFR-06 (no LLM, deterministic): clio performs **no** LLM/AI network call in any slice
  code path; all logic is deterministic. The only network egress is the local CDP endpoint
  (`127.0.0.1`, loopback) and the Creatio environment via the existing session/`IApplicationClient`.
- [ ] NFR-07 (secret hygiene): Reuse the browser-session redaction guarantees — cookie
  values never appear in logs, MCP payloads, CLI stdout, or `--debug` exceptions (cookie
  NAMES only, per `AuthenticatedBrowserLauncher`).

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | The PoC recipe (overlay-select + untrusted context-pad append + trusted card fill + SAVE) reproduces deterministically on the target Creatio versions; `add.serviceTask` keeps defaulting to `readDataUserTask`. | If the default element or the append affordance changes, the driver appends the wrong element or fails; mitigated by the `.djs-validate-outline` + readback gates and by morphing via `[data-action=setup]` as a fallback. |
| A-02 | `AuthenticatedBrowserLauncher` can be extended to run `Runtime.evaluate` JS over the existing CDP page session without destabilising the shipped `open-web-app --authenticated` flow. | If the CDP session model can't carry script eval cleanly, the driver needs a separate CDP channel — added implementation cost, no scope change. |
| A-03 | `ManagerMap.EventType` (in `ProcessModel/Schema.cs`) covers the start/end/activity/gateway/intermediate classification needed by the validator for the `data-id` set in the catalog. | If a `data-id` is unclassified, the validator must extend the map; small risk, surfaced as a validator unit-test gap. |
| A-04 | `generate-process-model` + `execute-esq` on `VwProcessLib` are sufficient to prove the process exists (model is empty when there are no process-level parameters, which is acceptable proof for the slice). | If readback can't confirm element-level content, the slice's "done" proof is the `VwProcessLib` row + model exit-0 only (already accepted in the PoC). |
| A-05 | Forms-auth browser-session handoff (shipped) works against the target environment used for E2E (e.g. `krestov-test`). | If not, E2E can't run; flagged as not-in-CI already (FR-17). |
| A-06 | A single Read data element with only the source object configured produces a valid, savable process (no required filter/sort/columns for a minimal valid Read data). | If the platform requires more setup-card fields to save, FR-10 scope expands to set those defaults; surfaced as OQ-02. |
| A-07 | The already-parsed `ProcessSchemaResponse` (`FlowElements` with `SourceRefUId`/`TargetRefUId`/`FlowType`/`EventType` + process-level `Parameters`) carries enough structure to project a useful element/flow graph for `describe-process` without re-parsing the raw metadata. | If element-to-element links can't be reconstructed from the parsed flow elements, `describe-process` would need raw-metadata reparsing — added cost, surfaced as a graph-extraction unit-test gap. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Should `process-add-element` create a new process when `--process-id` is omitted, or require an explicit `process-create` step first? (Affects Destructive flag semantics in FR-12.) | Architect / PM | Before ADR |
| OQ-02 | Does a minimal Read data (object only) always save, or does the setup card require any other field (e.g. read mode) to be set before SAVE succeeds? (Validates A-06.) | Architect / Dev | Before Story 3 |
| OQ-03 | Where should the reusable CDP `Runtime.evaluate` capability live — extend `IAuthenticatedBrowserLauncher`, or introduce a sibling `IProcessDesignerDriver` over the same CDP session? | Architect | ADR |
| OQ-04 | What is the canonical name/identity the tool returns for readback (process Code vs UId vs caption), and does it set the caption deterministically so `execute-esq` can filter on it? | Architect / Dev | Before Story 3 |
| OQ-05 | Should `validate-process-graph` accept the catalog `data-id` strings directly, or a clio-side element enum? (Affects the guidance contract the agent emits.) | Architect | ADR |
| OQ-06 | For `describe-process`, which process-identity inputs are first-class (code / UId / caption), and how is "exactly one required" enforced? (Affects FR-19 CLI/MCP arg contract.) | Architect / Dev | Before Story 8 |

## Dependencies

- Depends on:
  - Browser-session-handoff feature (shipped) — `AuthenticatedBrowserLauncher`,
    `get-browser-session`, `IChromiumLocator`, forms-auth, cookie redaction.
  - `clio/Command/ProcessModel/Schema.cs` `ManagerMap.EventType` (node classification);
    the existing read path `ProcessSchemaRequest` + `ProcessModelGenerator` +
    `ProcessSchemaResponse` (reused by `describe-process`, FR-19).
  - `GuidanceCatalog` + `get-guidance` infrastructure; `BaseTool` MCP execution paths.
  - Reused readback tools: `generate-process-model`, `execute-esq`.
  - Research: ENG-90883 page; `spec/ai-business-process-generation/ai-bp-ui-playbook.md`,
    `ai-bp-element-catalog.md`, `ai-bp-connection-rules.md`; the research "read & explain"
    note (motivates FR-19 / `describe-process`).
- Blocks (future increments this unblocks):
  - Driving the remaining data-operation elements (Add/Modify/Delete data, Formula),
    user-action tasks, gateways, intermediate events.
  - Process parameters/mapping/formulas/filters automation (and the matching
    `describe-process` v2 that decodes filter/mapping expressions into plain language).
  - A Playwright/CI headless harness reusing this exact recipe.
  - A multi-element transactional `process-build` orchestration.
