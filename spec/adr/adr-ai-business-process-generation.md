# ADR: AI-Assisted Business Process Generation via clio MCP (Approach 3 / Variant A — CDP driving)

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**Created**: 2026-06-12
**Jira**: ENG-90883 (research) — implementation ticket TBD
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

A developer who wants to automate a task today must hand-build the BPMN diagram in the classic
Creatio Process Designer; an AI agent over clio MCP can describe the intent but has no deterministic
way to turn it into a working process. The ENG-90883 research concluded that clio must **not**
generate process metadata directly (serialization belongs to the live designer) — instead clio
exposes deterministic guidance + tools and lets the agent translate intent → BPMN. This ADR designs
the first thin vertical slice: a `process-modeling` guidance resource, an in-memory
`ProcessGraphValidator` (`validate-process-graph`), and a CDP-driven `process-add-element` tool that
appends and saves a "Read data" element in the live Process Designer (diagram-js/bpmn-js inside the
Ext shell). The research also flagged a symmetric **"read & explain" quick win** — reading an
already-built process back into a structured graph the agent can narrate — which this ADR adds as a
fourth, pure-read component (`describe-process`, FR-19) that reuses the existing schema-read parsing
(backend ≈ 0; see "Read-side addendum" below).

The **channel decision is LOCKED**: Variant A drives the live designer over the **existing CDP page
session** that `AuthenticatedBrowserLauncher` establishes — **no Playwright dependency, no headless CI
harness** in this increment. The recipe is feasibility-proven end-to-end on env `krestov-test`
(process `UsrProcess_493d4c9`, "AI PoC Read Contact"), per
`spec/ai-business-process-generation/ai-bp-ui-playbook.md` §6. This ADR designs *within* that channel;
it does not reopen it.

This ADR also **resolves the PRD's five open questions** (OQ-01..OQ-05) with owner-provided answers
(see "Resolved Open Questions" below) and refines the design around them.

---

## Decision

Ship four cooperating components, all pure-DI (no `new` of behavior classes, no MediatR, kebab-case
flags — CLIO001):

1. **`process-modeling` guidance** — a `[McpServerResourceType]` `ProcessModelingGuidanceResource`
   (modeled on `DataBindingsGuidanceResource`) registered in `GuidanceCatalog`, consolidating the
   element catalog + connection rules R1–R17 + the build recipe from the three research docs.
2. **`IProcessGraphValidator` / `ProcessGraphValidator`** — a pure in-memory validator (no I/O) behind
   DI that classifies node `data-id` strings via `ManagerMap.EventType` and emits structured findings
   for R1–R17. Exposed as the **non-environment-sensitive** MCP tool `validate-process-graph`
   (`ReadOnly = true`), via `BaseTool` `InternalExecute(options)`.
3. **`process-add-element`** — a `Command<TOptions>` + a new sibling **`IProcessDesignerDriver`** that
   runs the proven JS recipe over the **same CDP page session** `AuthenticatedBrowserLauncher`
   establishes (CDP `Runtime.evaluate`). It opens/creates the authenticated designer, appends a Read
   data element, configures the source-object lookup, sets a deterministic caption, SAVEs, asserts
   `.djs-validate-outline` absent, detects "Successfully saved", and returns `{code, uId, caption}`.
   Exposed as the **environment-sensitive** MCP tool `process-add-element`, via `BaseTool`
   `InternalExecute<ProcessAddElementCommand>(options)`.
4. **`describe-process`** (read & explain — FR-19, the inverse of generation) — a `Command<TOptions>` +
   a new `IProcessGraphExtractor` that **reuses the existing `ProcessSchemaRequest` parsing**
   (`ProcessModelGenerator`/`ProcessSchemaResponse` in `clio/Command/ProcessModel/`) and **exposes the
   element graph + flows** (today only process-level parameters are surfaced). It returns a structured
   `ProcessDescription` (`elements [{id, dataId/type, label, params}]`, `flows [{source, target, kind}]`,
   process-level `parameters`) — labelled via `ManagerMap.ResolveDataId`/role helper so it is symmetric
   with the validator and guidance. Exposed as the **environment-sensitive** MCP tool `describe-process`
   (`ReadOnly = true`), via `BaseTool` `InternalExecute<DescribeProcessCommand>(options)`. See the
   "Read-side addendum" section for the read-path reuse detail and the v1 filter/mapping limitation.

The reusable CDP `Runtime.evaluate` capability lives in a **sibling** `IProcessDesignerDriver`
(OQ-03), **not** in `IAuthenticatedBrowserLauncher`. The launcher stays focused on
launch + cookie-inject + navigate. To avoid duplicating CDP WebSocket plumbing, the `CdpSendAsync`
frame-pump pattern currently private to `AuthenticatedBrowserLauncher` is extracted into a small shared
helper `ICdpSession` / `CdpSession` that both the launcher and the new driver use.

---

## Resolved Open Questions (owner-provided)

| OQ | Resolution baked into this design |
|----|-----------------------------------|
| **OQ-01** (new vs explicit create) | `process-add-element` **opens/creates a new process when no `--process-id` is supplied**. Opening/creating the designer session is a **distinct internal driver step** (`OpenOrCreateDesignerAsync`), not a separate CLI verb. `Destructive = false` for a new process; the tool reports `Destructive = true` only when `--process-id` is supplied (modifying an existing saved process). |
| **OQ-02** (minimal Read data savable?) | **YES, proven live.** Source object only is enough: read mode defaults to "Read the first record", no filter, columns default to "all". The driver sets **only** the source-object lookup + caption; it does **not** touch extra setup-card fields. A-06 is confirmed; FR-10 scope is unchanged. |
| **OQ-03** (where CDP `Runtime.evaluate` lives) | **Sibling `IProcessDesignerDriver`** over the same CDP page session. Shared CDP plumbing extracted to `ICdpSession`/`CdpSession`; `IAuthenticatedBrowserLauncher` is unchanged in contract. |
| **OQ-04** (readback identity) | The **caller provides a deterministic caption**; the driver sets it on the process before SAVE. Readback filters `VwProcessLib` by `Caption` (via `execute-esq`) and the tool returns `{code (Name, e.g. UsrProcess_xxxx), uId, caption}`. Process **Code is platform-auto-generated**, so the **caption is the deterministic handle**. |
| **OQ-05** (validator input) | `validate-process-graph` accepts the catalog **`data-id` strings directly** (e.g. `"readDataUserTask"`, `"startEvent"`, `"exclusiveGateway"`) — the same identifiers the guidance emits and the designer uses — and maps them internally (reuse/extend `ManagerMap.EventType` classification in `Schema.cs`) to validate against R1–R17. No separate clio-side element enum. |
| **OQ-06** (`describe-process` identity inputs — FR-19) | `describe-process` accepts process identity by **code / UId / caption** (+ `environment-name`). The command/tool enforces **exactly one** identity input (otherwise a user-friendly `Error:`). Code resolves through the existing `VwProcessLib` `Name` lookup (`ProcessModelGenerator.GetProcessIdFromName`); UId resolves directly; caption resolves via `VwProcessLib.Caption`. Output is structured JSON (elements/flows/parameters), never the raw escaped `metaData`. |

---

## Alternatives Considered

### Decision A — Where the CDP `Runtime.evaluate` capability lives (OQ-03)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A1: Extend `IAuthenticatedBrowserLauncher` with `EvaluateAsync` | One type owns the whole browser lifecycle | Conflates "launch + auth handoff" (shipped, stable, used by `open-web-app --authenticated`) with script-eval; risks destabilising the shipped flow (A-02); fat interface | Rejected: violates SRP and risks regression in a shipped command |
| **A2: Sibling `IProcessDesignerDriver` over the same CDP session; shared `ICdpSession` helper** | Keeps launcher focused; driver owns JS-recipe + result/verification reads; reuses `CdpSendAsync` plumbing | One new helper extraction (`CdpSession`) | **Chosen** — matches owner OQ-03 answer; least blast radius on shipped code |
| A3: Separate CDP channel (new WebSocket) for the driver | No coupling to launcher's session at all | Two CDP channels to the same page; duplicate plumbing; navigation/session sync issues | Rejected: A-02 explicitly prefers reusing the existing session |

### Decision B — How the agent feeds the validator (OQ-05)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| B1: clio-side element enum the agent must learn | Compile-time safety in C# | Forces a second vocabulary; guidance and designer use `data-id`; mapping burden on the agent; drift between enum and catalog | Rejected: adds a contract the agent must translate to |
| **B2: Accept catalog `data-id` strings directly; map internally via `ManagerMap.EventType`** | One vocabulary across guidance, validator, and designer; agent emits exactly what it reads | Unknown `data-id` must classify to `Unknown` and surface as a finding, not crash | **Chosen** — matches OQ-05; single source of truth (the catalog) |

### Decision C — Process creation model (OQ-01)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| C1: Require an explicit `process-create` verb first | Clear two-step contract; Destructive semantics trivial | Extra round-trip for the common "fresh process" case; more surface this slice does not need | Rejected for the slice (revisit when multi-element orchestration lands) |
| **C2: `process-add-element` opens/creates a new process when `--process-id` omitted; creation is an internal driver step** | Single call for the common case; `Destructive=false` for new, `true` for existing | Destructive flag becomes input-dependent (handled by the tool) | **Chosen** — matches OQ-01; clean internal `OpenOrCreateDesignerAsync` boundary |

### Decision D — JS-recipe storage (embedded resource vs inline string)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| D1: Inline C# string literals in the driver | No build wiring | Hard to version/diff; mixes a large JS payload into C#; no clean parity path for a future Playwright harness | Rejected |
| **D2: Embed the recipe as a versioned `EmbeddedResource` (`.js`) read at runtime** | Versionable/diffable; one canonical recipe a future Playwright/CI harness can reuse verbatim; keeps C# thin | One `<EmbeddedResource>` csproj entry + an accessor | **Chosen** — recommended by the task; see "JS-recipe storage" below |

### Decision E — Overall channel (LOCKED — recorded for completeness)

| Option | Status |
|--------|--------|
| Variant A — drive the live designer over the existing CDP session, reusing `AuthenticatedBrowserLauncher` + `get-browser-session` auth | **Chosen / LOCKED** (proven on `krestov-test`, `UsrProcess_493d4c9`) |
| Playwright / headless CI harness | Future work (NFR-02); out of scope this increment |
| diagram-js internals API (`Terrasoft.addSchemaItems`) | Fallback for steps that resist trusted input (A-01); not the primary path |

### Decision F — `describe-process` read path (FR-19)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| F1: Re-parse the raw `metaData` from scratch for the graph | Full control over the projection | Duplicates the parsing already done by `ProcessSchemaResponse`; two parsers to keep in sync; re-derives the taxonomy | Rejected: violates the "reuse, do not re-derive" rule (A-03/A-07) |
| **F2: Reuse the existing `ProcessSchemaRequest` parsing + `ProcessSchemaResponse`; project the already-parsed `FlowElements`/`Parameters` into a structured graph via `IProcessGraphExtractor`** | Backend ≈ 0 (full schema already parsed); one vocabulary (`ManagerMap`/`data-id`); symmetric with the validator/guidance | Element-to-element links must be reconstructed from `SourceRefUId`/`TargetRefUId` (A-07) | **Chosen** — the research "read & explain" quick win; reuses shipped parsing |
| F3: Decode filter/mapping expressions (`FilterGroup`/`ParameterExpression`) into plain language in v1 | Richer narration | Heavily-escaped, large surface, brittle; not needed for the structural quick win | **Deferred to v2** — explicit v1 limitation |

---

## Implementation Plan

### Component boundaries

```
                         MCP surface (clio/Command/McpServer)
  ┌───────────────────────────┬───────────────────────────┬──────────────────────────────┐
  │ ProcessModelingGuidance   │ ValidateProcessGraphTool   │ ProcessAddElementTool         │
  │ Resource (resource)       │ (BaseTool, ReadOnly,       │ (BaseTool, env-sensitive,     │
  │ + GuidanceCatalog entry   │  InternalExecute(options)) │  InternalExecute<TCommand>)   │
  │                           │ DescribeProcessTool        │                               │
  │                           │ (BaseTool, ReadOnly,       │                               │
  │                           │  env-sensitive,            │                               │
  │                           │  InternalExecute<TCommand>)│                               │
  └───────────────────────────┴─────────────┬─────────────┴───────────────┬──────────────┘
                                             │                             │
                                  ┌──────────▼─────────┐        ┌──────────▼───────────────┐
                                  │ IProcessGraphValidator      │ ProcessAddElementCommand  │
                                  │ ProcessGraphValidator       │ (Command<Options>)        │
                                  │ (pure in-memory, R1–R17)    │   uses:                   │
                                  │ uses ManagerMap.EventType   │   - IBrowserSessionService│
                                  └─────────────────────────────┤   - IAuthenticatedBrowser │
                                  ┌─────────────────────────────┤     Launcher (launch)     │
                                  │ IProcessGraphExtractor      │   - IProcessDesignerDriver│
                                  │ ProcessGraphExtractor       │   - IProcessGraphValidator│
                                  │ (parsed ProcessSchemaResponse│   - ISettingsRepository   │
                                  │  -> ProcessDescription;     │                           │
                                  │  reuses ManagerMap)         │                           │
                                  │   used by                   │                           │
                                  │   DescribeProcessCommand    │                           │
                                  └─────────────────────────────┴──────────┬────────────────┘
                                                                           │
                                                          ┌────────────────▼─────────────────┐
                                                          │ IProcessDesignerDriver            │
                                                          │ ProcessDesignerDriver             │
                                                          │  (CDP Runtime.evaluate, recipe)   │
                                                          │  uses ICdpSession + recipe .js    │
                                                          └────────────────┬──────────────────┘
                                                                           │ reuses
                                                          ┌────────────────▼─────────────────┐
                                                          │ ICdpSession / CdpSession          │
                                                          │ (extracted CdpSend frame-pump;    │
                                                          │  AuthenticatedBrowserLauncher      │
                                                          │  also consumes it)                │
                                                          └───────────────────────────────────┘
```

**Boundary rules**
- `IAuthenticatedBrowserLauncher` contract is **unchanged** (it still launches + injects + navigates).
  Its private `CdpSendAsync`/`ReceiveTextAsync`/`FindPageTargetAsync`/`ReadDevToolsPortAsync` are moved
  behind `ICdpSession`; the launcher is refactored to consume the shared helper (no behavior change —
  covered by its existing E2E and any added unit tests around `CdpSession`).
- `ICdpSession` owns: resolve DevTools port, find the page WebSocket target, open the WS, `SendAsync`
  one CDP command and drain frames to the matching id (the existing CDP error handling kept verbatim),
  plus a typed `Runtime.evaluate` wrapper returning the JSON result. Loopback-only (`127.0.0.1`).
- `IProcessDesignerDriver` owns the **recipe orchestration**: open/create designer, wait for
  `.djs-shape`, dismiss stray popups, overlay-select the source shape, untrusted context-pad append,
  configure the source-object lookup, set caption, SAVE, assert `.djs-validate-outline` absent, detect
  "Successfully saved", read back identity from the page. It executes JS via `ICdpSession.EvaluateAsync`
  using the embedded recipe; it does **not** know about MCP or CLI options.
- `IProcessGraphExtractor` owns the **read-side projection** (FR-19): given a parsed
  `ProcessSchemaResponse`, project the already-parsed `FlowElements`/`Parameters` into a
  `ProcessDescription` (`elements`/`flows`/`parameters`). Pure in-memory, no I/O — it does **not** know
  about MCP or CLI options; `DescribeProcessCommand` owns the schema read (reusing the existing route +
  `IApplicationClient`) and passes the parsed object in.
- `ProcessAddElementCommand` orchestrates: resolve env → validate planned graph (abort on error) →
  obtain forms-auth session (`IBrowserSessionService`) → launch authenticated browser
  (`IAuthenticatedBrowserLauncher`) → drive (`IProcessDesignerDriver`) → return `{code, uId, caption}`
  or a user-friendly `Error:`.
- `DescribeProcessCommand` orchestrates: resolve env → resolve process identity (code/UId/caption →
  the existing `VwProcessLib` lookup / `ProcessSchemaRequest` route via `IApplicationClient`) → parse
  via `ProcessSchemaResponse.FromJson` → project via `IProcessGraphExtractor` → emit structured JSON or
  a user-friendly `Error:`.

### Key interfaces / contracts

```csharp
// --- Shared CDP session helper (extracted from AuthenticatedBrowserLauncher) ---
// clio/Common/BrowserSession/ICdpSession.cs
public interface ICdpSession : IAsyncDisposable {
    // Connects to the page target's WebSocket for the running browser on the given loopback port.
    Task ConnectAsync(int devToolsPort, CancellationToken ct = default);
    // Sends one CDP command and returns the matching result frame (errors throw, as today).
    Task<JsonElement> SendAsync(string method, object @params, CancellationToken ct = default);
    // Convenience wrapper over Runtime.evaluate that returns the awaited JSON result value.
    Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise = true,
        CancellationToken ct = default);
}

// --- Process designer driver (the new CDP Runtime.evaluate capability — OQ-03) ---
// clio/Common/ProcessDesigner/IProcessDesignerDriver.cs
public interface IProcessDesignerDriver {
    // Opens (or creates, when processId is null) the authenticated designer over an already-launched
    // browser session and drives the proven Read-data recipe. Pure orchestration over CDP; no MCP/CLI.
    Task<ProcessAddElementResult> AddReadDataElementAsync(ProcessAddElementRequest request,
        CancellationToken ct = default);
}

// Data carriers (records — DTOs may use `new`):
public sealed record ProcessAddElementRequest(
    EnvironmentSettings Environment,
    int DevToolsPort,            // the port AuthenticatedBrowserLauncher's browser is listening on
    string? ProcessId,           // null => create a new process (OQ-01)
    string ReadObject,           // e.g. "Contact" (OQ-02: source object only)
    string Caption);             // deterministic caption the caller supplies (OQ-04)

public sealed record ProcessAddElementResult(
    bool Success,
    string? Code,                // platform-generated Name, e.g. "UsrProcess_493d4c9" (OQ-04)
    string? UId,
    string Caption,
    string? Error);              // user-friendly "Error: ..." on failure (no stack trace)

// --- Validator (OQ-05: accepts data-id strings) ---
// clio/Command/ProcessModel/IProcessGraphValidator.cs
public interface IProcessGraphValidator {
    ProcessGraphValidationResult Validate(ProcessGraph graph);
}

public sealed record ProcessGraphNode(string Id, string Type); // Type = catalog data-id (OQ-05)
public enum ProcessFlowKind { Sequence, Conditional, Default }
public sealed record ProcessGraphEdge(string Source, string Target, ProcessFlowKind FlowKind);
public sealed record ProcessGraph(IReadOnlyList<ProcessGraphNode> Nodes,
    IReadOnlyList<ProcessGraphEdge> Edges);

public enum ProcessGraphSeverity { Error, Warning }
public sealed record ProcessGraphFinding(
    ProcessGraphSeverity Severity, string RuleId, string Message,
    string? NodeId = null, ProcessGraphEdge? Edge = null);
public sealed record ProcessGraphValidationResult(
    bool HasErrors, IReadOnlyList<ProcessGraphFinding> Findings);

// --- Read-side extractor (FR-19: describe-process) ---
// clio/Command/ProcessModel/IProcessGraphExtractor.cs
public interface IProcessGraphExtractor {
    // Projects an already-parsed ProcessSchemaResponse into a structured graph (no I/O).
    ProcessDescription Extract(ProcessSchemaResponse schema, string culture);
}

public sealed record ProcessDescriptionElement(
    string Id, string DataId, string Type /* role */, string Label,
    IReadOnlyList<ProcessDescriptionParam> Parameters);
public sealed record ProcessDescriptionFlow(
    string Source, string Target, ProcessFlowKind Kind); // reuse the same flow-kind vocabulary
public sealed record ProcessDescriptionParam(string Name, string Type, string Direction, string? Caption);
public sealed record ProcessDescription(
    IReadOnlyList<ProcessDescriptionElement> Elements,
    IReadOnlyList<ProcessDescriptionFlow> Flows,
    IReadOnlyList<ProcessDescriptionParam> Parameters);
```

#### `data-id` → role classification (OQ-05 / FR-06)

The catalog `data-id` strings are diagram-js/bpmn types, not `managerItemUId` GUIDs, so the validator
needs a thin `data-id → ManagerMap.EventType` map placed next to `ManagerMap` in
`clio/Command/ProcessModel/Schema.cs` (extend, do not re-derive — A-03):

```csharp
// clio/Command/ProcessModel/Schema.cs — add to ManagerMap (or a sibling ProcessElementCatalog)
public static EventType ResolveDataId(string dataId) => dataId switch {
    "startEvent"            => EventType.StartEvent,
    "startEventSignal"      => EventType.StartSignalEvent,
    "startEventTimer"       => EventType.StartTimer,
    "startEventMessage"     => EventType.StartMessageEvent,
    "endEvent"              => EventType.EndEvent,          // Simple end and Terminate share the data-id
    "exclusiveGateway"      => EventType.ExclusiveGateway,
    "parallelGateway"       => EventType.ParallelGateway,
    "inclusiveGateway"      => EventType.InclusiveGateway,
    "eventBasedGateway"     => EventType.EventBasedGateway,
    // *UserTask / formulaTask / scriptTask / webService / callActivity => activity roles
    "readDataUserTask" or "addDataUserTask" or "changeDataUserTask" or "deleteDataUserTask"
        or "userTask" or "activityUserTask" /* … all *UserTask … */ => EventType.UserTask,
    "formulaTask"           => EventType.FormulaTask,
    "scriptTask"            => EventType.ScriptTask,
    "webService"            => EventType.WebServiceTask,
    "callActivity"          => EventType.SubProcess,
    var i when i.StartsWith("intermediateCatchEvent") => EventType.IntermediateCatchSignalEvent, // role: intermediate
    var i when i.StartsWith("intermediateThrowEvent") => EventType.IntermediateThrowSignalEvent,
    _ => EventType.Unknown
};
```

The validator then collapses `EventType` into the five **roles** the rules need (Start / End / Activity
/ Gateway / Intermediate), so adding a new `data-id` only requires one map entry. An `Unknown`
classification produces a finding (not a crash). `describe-process` reuses the same `EventType` → role
collapse for element labels (FR-19); for elements read from the schema, `ManagerMap.Resolve(ManagerItemUId)`
already yields the `EventType`, so no extra lookup is needed beyond the role collapse.

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/McpServer/Resources/ProcessModelingGuidanceResource.cs` | `[McpServerResourceType]` guidance article (FR-01/02/03/04). Content consolidates the three research docs. |
| `clio/Command/ProcessModel/IProcessGraphValidator.cs` | Validator interface + graph/finding records (FR-05). |
| `clio/Command/ProcessModel/ProcessGraphValidator.cs` | Pure in-memory R1–R17 impl, uses `ManagerMap` role map (FR-05/06/07). |
| `clio/Command/McpServer/Tools/ValidateProcessGraphTool.cs` | `BaseTool`, `ReadOnly=true`, non-env, `InternalExecute(options)` (FR-08). |
| `clio/Command/McpServer/Prompts/ValidateProcessGraphPrompt.cs` | Prompt guiding the agent to validate before driving (MCP policy). |
| `clio/Command/ProcessDesigner/ProcessAddElementCommand.cs` (+ `ProcessAddElementOptions`) | `Command<TOptions>`; orchestrates validate → launch → drive → readback identity (FR-09/11/13/14/15). |
| `clio/Common/ProcessDesigner/IProcessDesignerDriver.cs` | Sibling driver interface (OQ-03, FR-09/10). |
| `clio/Common/ProcessDesigner/ProcessDesignerDriver.cs` | Recipe impl over `ICdpSession.EvaluateAsync` (FR-10). |
| `clio/Common/ProcessDesigner/ProcessAddElementRequest.cs` / `ProcessAddElementResult.cs` | Driver DTOs (records). |
| `clio/Common/ProcessDesigner/Recipes/read-data-element.recipe.js` | **Embedded** versioned JS recipe (Decision D2). |
| `clio/Common/ProcessDesigner/ProcessDesignerRecipes.cs` | Reads the embedded recipe by name (thin accessor). |
| `clio/Common/BrowserSession/ICdpSession.cs` / `CdpSession.cs` | Shared CDP plumbing extracted from `AuthenticatedBrowserLauncher`. |
| `clio/Command/McpServer/Tools/ProcessAddElementTool.cs` (+ `ProcessAddElementArgs`) | `BaseTool`, env-sensitive, `InternalExecute<ProcessAddElementCommand>(options)` (FR-12). |
| `clio/Command/McpServer/Prompts/ProcessAddElementPrompt.cs` | Prompt aligned to the tool contract (MCP policy). |
| `clio/Command/ProcessModel/IProcessGraphExtractor.cs` (+ `ProcessDescription`/element/flow/param records) | Read-side projection contract (FR-19). |
| `clio/Command/ProcessModel/ProcessGraphExtractor.cs` | Pure in-memory `ProcessSchemaResponse` → `ProcessDescription` projection; reuses `ManagerMap` (FR-19). |
| `clio/Command/DescribeProcessCommand.cs` (+ `DescribeProcessOptions`) | `Command<TOptions>`; resolve identity → read schema (reuse `ProcessSchemaRequest`) → extract → emit JSON (FR-19, OQ-06). |
| `clio/Command/McpServer/Tools/DescribeProcessTool.cs` (+ `DescribeProcessArgs`) | `BaseTool`, env-sensitive, `ReadOnly=true`, `InternalExecute<DescribeProcessCommand>(options)` (FR-19). |
| `clio/Command/McpServer/Prompts/DescribeProcessPrompt.cs` | Prompt: read then narrate using the **existing** `process-modeling` resource (FR-19, MCP policy). |
| `clio/help/en/process-add-element.txt` | CLI `-H` help (FR-18). |
| `clio/docs/commands/process-add-element.md` | GitHub docs (FR-18). |
| `clio/help/en/describe-process.txt` | CLI `-H` help (FR-18). |
| `clio/docs/commands/describe-process.md` | GitHub docs (FR-18). |
| `clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs` | One `[Category("Unit")]` test per R-rule error/warning (FR-16). |
| `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs` | Graph extraction from `clio.tests/Examples/ProcessSchema/*.json` fixtures; `ResolveDataId`/role mapping (FR-16/FR-19). |
| `clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs` | Argument mapping + finding shape (FR-16). |
| `clio.tests/Command/McpServer/ProcessAddElementToolTests.cs` | Argument mapping + safety-flag/Destructive semantics (FR-16). |
| `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` | Argument mapping + safety flags (ReadOnly/non-destructive/idempotent); env-aware path selection (FR-16/FR-19). |
| `clio.tests/Command/ProcessDesigner/ProcessAddElementCommandTests.cs` | `BaseCommandTests<ProcessAddElementOptions>`; validate-abort, error classes (FR-16). |
| `clio.tests/Command/DescribeProcessCommandTests.cs` | `BaseCommandTests<DescribeProcessOptions>`; identity guard + not-found `Error:` (FR-16/FR-19/AC-ERR). |
| `clio.tests/Command/McpServer/ProcessModelingGuidanceResourceTests.cs` | Guidance-catalog registration of `process-modeling` (FR-16). |
| `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs` | MCP E2E for `validate-process-graph` (FR-17). |
| `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs` | Live build-and-readback `Start → Read data → End` (FR-17; **not in CI**). |
| `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` | Live read of a known process → non-empty elements/flows/parameters (FR-17; **not in CI**). |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Common/BrowserSession/AuthenticatedBrowserLauncher.cs` | Refactor to consume `ICdpSession` (move `CdpSendAsync`/`FindPageTargetAsync`/`ReadDevToolsPortAsync` into `CdpSession`). **No contract/behavior change.** Optionally expose the chosen `DevToolsPort` so the driver can attach to the same browser. |
| `clio/Common/BrowserSession/IAuthenticatedBrowserLauncher.cs` | If the driver needs the port: change `LaunchAsync` to return a small `LaunchResult { int DevToolsPort }` (or add a `LaunchAndKeepOpenAsync`). Keep the existing `--authenticated` flow working. |
| `clio/Command/ProcessModel/Schema.cs` | Add `ManagerMap.ResolveDataId(string)` + role helper (FR-06, OQ-05). No change to existing GUID map. Reused by both the validator and `describe-process`. |
| `clio/Command/McpServer/Resources/GuidanceCatalog.cs` | Register `["process-modeling"] = Create("process-modeling", "...", ProcessModelingGuidanceResource.Guide)` (FR-01). |
| `clio/BindingsModule.cs` | Register `IProcessGraphValidator`, `IProcessDesignerDriver`, `ICdpSession`, `ProcessAddElementCommand`, `IProcessGraphExtractor`, `DescribeProcessCommand` (DI — CLIO001). |
| `clio/Program.cs` | Add `ProcessAddElementOptions` and `DescribeProcessOptions` to the verb `Types[]` array and the matching `Resolve<...>(opts).Execute(opts)` arms. |
| `clio/clio.csproj` | Add `<EmbeddedResource Include="Common/ProcessDesigner/Recipes/read-data-element.recipe.js" />` (Decision D2). |
| `clio/Commands.md` | Add `process-add-element` and `describe-process` rows/sections (FR-18). |
| `docs/McpCapabilityMap.md` | Add `validate-process-graph` (ReadOnly), `process-add-element` (env-sensitive), and `describe-process` (env-sensitive, ReadOnly) with safety flags; note `generate-process-model`/`execute-esq` reused unchanged (FR-18, AC-11). |
| `spec/sprint-status.yaml` | Add the slice stories (story-writer phase). |

### DI registrations (`clio/BindingsModule.cs`)

```csharp
// Pure in-memory validator — Transient is fine (stateless).
services.AddTransient<IProcessGraphValidator, ProcessGraphValidator>();

// Pure in-memory read-side projection (describe-process) — Transient (stateless).
services.AddTransient<IProcessGraphExtractor, ProcessGraphExtractor>();

// Shared CDP session helper (extracted). Transient: one per connection, IAsyncDisposable.
services.AddTransient<ICdpSession, CdpSession>();

// Sibling process-designer driver (OQ-03). Transient.
services.AddTransient<IProcessDesignerDriver, ProcessDesignerDriver>();

// The new commands (constructor injection; no `new`).
services.AddTransient<ProcessAddElementCommand>();
services.AddTransient<DescribeProcessCommand>();

// Already registered (reused, unchanged):
//   IChromiumLocator / ChromiumLocator          (line 206)
//   IAuthenticatedBrowserLauncher / Authenticated… (line 207)
//   GetBrowserSessionCommand                     (line 563)  → IBrowserSessionService is its collaborator
//   IProcessModelGenerator / ProcessModelGenerator (read path reused by describe-process)
// MCP tools/resources/prompts are picked up by WithToolsFromAssembly / WithResourcesFromAssembly /
// WithPromptsFromAssembly — no explicit registration needed.
```

### Program.cs wiring (CLI verbs)

`process-add-element` and `describe-process` are real CLI verbs (they have options and can run outside
MCP), so:

```csharp
// 1) add to the verb Types[] array (near GenerateProcessModelCommandOptions, line 210):
typeof(ProcessAddElementOptions),
typeof(DescribeProcessOptions),

// 2) add the execution arms (near line 403):
ProcessAddElementOptions opts => Resolve<ProcessAddElementCommand>(opts).Execute(opts),
DescribeProcessOptions opts => Resolve<DescribeProcessCommand>(opts).Execute(opts),
```

`validate-process-graph` is **MCP-only** (no CLI verb, no environment) — it is not added to `Program.cs`.

### CLI flag specification (FR-11 — kebab-case, CLIO001)

```csharp
[Verb("process-add-element", Aliases = ["pae"],
    HelpText = "Append and configure a process element in the live Creatio Process Designer (CDP)")]
public class ProcessAddElementOptions : EnvironmentOptions {
    [Option("element-type", Required = true, HelpText = "Element to add. Slice supports only: read-data")]
    public string ElementType { get; set; }

    [Option("read-object", Required = true, HelpText = "Object to read data from, e.g. Contact")]
    public string ReadObject { get; set; }

    [Option("process-id", Required = false,
        HelpText = "Existing process id to modify. Omit to create a new process.")]
    public string ProcessId { get; set; }

    [Option("process-caption", Required = false,
        HelpText = "Deterministic caption set on the process; used for readback. Auto-generated when omitted.")]
    public string ProcessCaption { get; set; }

    [Option("headed", Required = false, Default = true,
        HelpText = "Run the browser headed (default true). Headless is unverified for this slice.")]
    public bool Headed { get; set; } = true;
}

// FR-19 / OQ-06 — describe-process (read & explain). Exactly one identity input required.
[Verb("describe-process", Aliases = ["dp"],
    HelpText = "Read an existing Creatio process and return a structured graph (elements/flows/parameters)")]
public class DescribeProcessOptions : EnvironmentOptions {
    [Option("process-code", Required = false, HelpText = "Process code (VwProcessLib Name) to describe.")]
    public string ProcessCode { get; set; }

    [Option("process-uid", Required = false, HelpText = "Process UId to describe.")]
    public string ProcessUid { get; set; }

    [Option("process-caption", Required = false, HelpText = "Process caption to describe.")]
    public string ProcessCaption { get; set; }

    [Option("culture", Required = false, Default = "en-US",
        HelpText = "Culture used to resolve localized element labels and captions.")]
    public string Culture { get; set; } = "en-US";
}
```

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--element-type` | string | Yes | Slice accepts only `read-data`; any other value → `Error:` (FR-10). |
| `--read-object` | string | Yes | Source object for the Read data lookup, e.g. `Contact` (OQ-02). |
| `--process-id` | string | No | Existing process id to modify; omit to create a new process (OQ-01). |
| `--process-caption` | string | No | (process-add-element) Deterministic caption set on the process; readback handle (OQ-04). Auto-generated when omitted. |
| `--headed` | bool | No (default `true`) | Headed launch is the only supported mode this increment (NFR-02). |
| `--process-code` / `--process-uid` / `--process-caption` | string | One required | (describe-process) Process identity; exactly one must be supplied (OQ-06). |
| `--culture` | string | No (default `en-US`) | (describe-process) Localized labels/captions. |
| `-e` / `--environment` | string | Yes (from `EnvironmentOptions`) | Standard environment selector (reused). |

All long-names are kebab-case. No camelCase options are introduced, so **no hidden aliases** are needed.

### MCP surface (per the MCP maintenance policy)

**Tool 1 — `validate-process-graph`** (`ValidateProcessGraphTool : BaseTool<ValidateProcessGraphOptions>`)
- Safety: `ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`.
- **Non-environment-sensitive** (pure in-memory) → constructs `ProcessGraph` from args, calls
  `IProcessGraphValidator.Validate`, returns a typed `ProcessGraphValidationResult`-shaped response.
  Because it does not run a `Command`, it uses the direct path (inject `IProcessGraphValidator`); it
  does **not** need `IToolCommandResolver`. (If routed as a degenerate command it would be
  `InternalExecute(options)`, but the validator is a service, so direct injection is cleaner.)
- Args (kebab-case JSON property names): `nodes` (`[{id, type}]`), `edges`
  (`[{source, target, flow-kind}]`). `type` = catalog `data-id` (OQ-05).

**Tool 2 — `process-add-element`** (`ProcessAddElementTool : BaseTool<ProcessAddElementOptions>`)
- Safety: `ReadOnly = false, Idempotent = false`. `Destructive` is set per the **new-vs-existing**
  rule (OQ-01): the tool reports/treats it as `Destructive = false` for a new process and
  `Destructive = true` when `--process-id` is supplied (modifying an existing saved process). The
  static `[McpServerTool]` attribute carries the conservative default; the tool description documents
  the existing-process case. (If the attribute cannot vary at runtime, declare `Destructive = true`
  statically and state in the description that it is non-destructive for a fresh process — matches FR-12.)
- **Environment-sensitive** (env name + browser session) → `InternalExecute<ProcessAddElementCommand>(options)`
  (resolves a fresh command for the current MCP call's environment — MCP env-aware rule).
- Args (kebab-case): `environment-name` (Required), `element-type` (Required, slice: `read-data`),
  `read-object` (Required), `process-id` (optional), `process-caption` (optional), `headed` (optional).
- Returns `{success, code, uId, caption, error}` so the caller/E2E can read it back (FR-14, OQ-04).

**Tool 3 — `describe-process`** (`DescribeProcessTool : BaseTool<DescribeProcessOptions>`, FR-19)
- Safety: `ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false` — it only reads.
- **Environment-sensitive** (reads via `IApplicationClient` against the env) →
  `InternalExecute<DescribeProcessCommand>(options)` (MCP env-aware rule; pattern mirrors
  `GenerateProcessModelTool`, but ReadOnly/non-destructive).
- Args (kebab-case): `environment-name` (Required), one of `process-code` / `process-uid` /
  `process-caption` (exactly one Required — OQ-06), `culture` (optional).
- Returns the structured `ProcessDescription` (`elements`/`flows`/`parameters`) as JSON — **not** the
  raw escaped `metaData`. v1 does not interpret filter/mapping expressions.

**Prompts** — `ValidateProcessGraphPrompt`, `ProcessAddElementPrompt`, and `DescribeProcessPrompt`
(aligned to tool contracts). `DescribeProcessPrompt` instructs the agent to read a process then narrate
the returned graph using the **existing** `process-modeling` resource (no new resource).

**Resource** — `process-modeling` guidance (FR-01..04), reachable via `get-guidance --name process-modeling`;
reused by `describe-process` for narration (FR-19).

**Reused readback/parsing tools** — `generate-process-model` and `execute-esq` are **unchanged**; the
`describe-process` read path reuses `ProcessSchemaRequest`/`ProcessModelGenerator` parsing **without
changing them** (it adds a new extractor + command alongside). The change summary/PR must state
**"MCP reviewed, no update required"** for `generate-process-model`/`execute-esq` (FR-18).

**E2E** — `clio.mcp.e2e` coverage for all three new tools is **mandatory** (MCP policy), even though MCP
E2E is **not in CI**; the `process-add-element` E2E needs Chromium + a live forms-auth env, and the
`describe-process` E2E needs a known process on a live env (FR-17).

### Read-side addendum (`describe-process` — FR-19, the research "read & explain" quick win)

`describe-process` is the **inverse** of `process-add-element`: instead of building a process it lets the
agent **understand an already-built one**. It is a near-zero-backend win because the full schema is
**already parsed internally** by the shipped read path:

- Route `ServiceUrlBuilder.KnownRoute.ProcessSchemaRequest` + `clio/Command/ProcessModel/ProcessSchemaRequest.cs`
  (request DTO) → `IApplicationClient.ExecutePostRequest` (exactly as `ProcessModelGenerator.GetProcessSchema`
  does) → `ProcessSchemaResponse.FromJson(json, logger)` deserializes everything:
  `Schema.MetaDataSchema.FlowElements` (each `FlowElement` already exposes `Name`, `UId`,
  `EventType` via `ManagerMap.Resolve(ManagerItemUId)`, `SourceRefUId`, `TargetRefUId`,
  `FlowType` (`FlowTypeSequence`), `Captions`, and `Parameters` (`FlowElementParameter`)), plus
  `Schema.MetaDataSchema.Parameters` (`ProcessParameter`).
- Today `generate-process-model` only surfaces the **process-level parameters**; `describe-process`
  **additionally exposes the element graph + flows** from the same parsed object via a new pure
  `IProcessGraphExtractor` (no I/O): elements (non-flow `FlowElement`s) projected to
  `{id, dataId, type (role), label, params}`, flows (flow `FlowElement`s where `EventType ∈
  SequenceFlow|ConditionalFlow|DefFlow`) projected to `{source (SourceRefUId), target (TargetRefUId),
  kind}` with `FlowTypeSequence`/`EventType` → `kind` (sequence/conditional/default), and the
  process-level parameters preserved verbatim.
- Element types are labelled via `ManagerMap.ResolveDataId`/role helper (FR-06) → **same `data-id`
  vocabulary** as generation, so `describe-process` is symmetric with the validator and the
  `process-modeling` guidance the agent uses to narrate.
- Identity (OQ-06): code resolves through the existing `VwProcessLib` `Name` lookup
  (`ProcessModelGenerator.GetProcessIdFromName`); UId directly; caption via `VwProcessLib.Caption`.
- **v1 LIMITATION (explicit out-of-scope, future work):** deep human-readable interpretation of element
  **filters and mapping** — the heavily-escaped `FilterGroup`/`ParameterExpression` JSON inside
  `FlowElementParameter.SourceValue`/`ConditionExpression` — is **not** decoded. v1 returns structure +
  element types + flows + basic params only. A `describe-process` v2 that decodes filter/mapping
  expressions into plain language is a future increment (tracked alongside parameters/mapping/formulas
  automation).

`describe-process` is **independent of the Variant-A driver** (components 1/6/7): it is a pure backend
read via `IApplicationClient` — no browser, no CDP. It depends only on the `process-modeling` guidance
(so the agent can interpret the result) and `ManagerMap.ResolveDataId` (to label element types).

### JS-recipe storage (Decision D2)

- The proven recipe (ui-playbook §6 steps 1–5) is stored as a single versioned
  `read-data-element.recipe.js` and shipped as an `<EmbeddedResource>`.
- `ProcessDesignerRecipes.Get("read-data-element")` reads it from the assembly manifest stream once
  (cached). `ProcessDesignerDriver` passes recipe fragments to `ICdpSession.EvaluateAsync`.
- The recipe is parameterised only by the **source object** and **caption** (injected as JSON-escaped
  values, never string-concatenated raw — XSS/JS-injection hygiene), matching OQ-02/OQ-04.
- Rationale: versionable/diffable, single source of truth, and the exact artifact a future
  Playwright/CI harness (NFR-02 future work) reuses verbatim — keeping C# thin.

### Driver recipe steps (encodes the proven PoC; ui-playbook §6 + connection rules §"Live cross-check")

1. **Open/create designer** (`OpenOrCreateDesignerAsync`, OQ-01): navigate the already-authenticated
   page to `…?vm=SchemaDesigner#process/<id>` (existing) or `…#process/` (new → bootstraps
   `Start → End`). Poll `document.querySelector('.djs-shape')` (or
   `.entry[data-action="create-serviceTask"]`) with a bounded timeout (NFR-03); fail fast with
   `Error:` if the canvas never renders (black-canvas/cache caveat noted).
2. **Dismiss stray popups**: send Escape if `.djs-popup.diagram-create-popup-menu` is present (NFR-03).
3. **Overlay-select the Start shape**: read its rect via `getBoundingClientRect`, inject a
   `position:fixed; pointer-events:none` overlay, trusted-click through to the diagram-js hit layer.
4. **Append (the one untrusted bit, QA-proven)**: dispatch `dragstart → mousemove(source) →
   mousemove/mouseover/mousemove/mouseup(target)` on
   `.djs-context-pad .entry[data-action="add.serviceTask"]`. `add.serviceTask` **defaults to
   `readDataUserTask`** and auto-inserts onto the `Start→End` flow → `Start → Read data → End`.
5. **Configure setup card** (OQ-02): set only the "Which object to read data from?" lookup to
   `--read-object`; pick the matching `.listview` option. Do **not** set read-mode/filter/columns.
6. **Set caption** (OQ-04): set the caption textbox to `--process-caption` (deterministic handle).
7. **Pre-SAVE validate** (FR-13a): the *command* runs `IProcessGraphValidator` on the resulting planned
   graph (`Start → readDataUserTask → End`) and **aborts on any error** before SAVE.
8. **`.djs-validate-outline` gate** (FR-13b / NFR-05): after the append/auto-connect, assert the
   connection element does **not** carry `.djs-validate-outline`; if present → abort, no SAVE, `Error:`.
9. **SAVE + detect** (FR-14): click SAVE; detect the message-panel **"Successfully saved"** signal.
   Never report success on a missing signal, a CDP error, or a flagged invalid connection.
10. **Read back identity** (OQ-04, FR-14): read the process Code + UId from the page (and/or the
    command confirms via `execute-esq` on `VwProcessLib` filtered by Caption). Return `{code, uId, caption}`.

### Error / recovery handling (non-transactional — NFR-04)

- A single call is **not transactional**. On failure **before SAVE**, the unsaved designer state is
  discarded (browser closed — reuse the launcher's `Kill(entireProcessTree)` cleanup pattern); clio
  reports the failure and leaves no saved partial process. On failure **after SAVE detection**, the
  saved process identity is reported so the caller can inspect/delete it. clio **never** reports
  success on a partial/failed run.
- User-friendly `Error:` per failure class (FR-15, AC-ERR), no stack traces (`--debug` excepted):
  Chromium not found (`ChromiumNotFoundException`); no forms-auth session
  (`CreatioAuthenticationException` / NFR-01 — fail closed for OAuth-only envs); designer never
  rendered (`.djs-shape` timeout, NFR-03); object lookup not found; append/connect rejected
  (`.djs-validate-outline`, AC-09); SAVE failed / validation dialog.
- `describe-process` (FR-19) is read-only and non-transactional by nature: identity not found / process
  missing / unreachable env → user-friendly `Error:` + exit non-zero, no partial structure emitted
  (AC-ERR).
- Render-timing waits and the black-canvas/cache caveat are bounded polls inside the recipe (NFR-03).

### Headed / headless flag

`--headed` defaults to `true`. Headed is the only supported mode this increment: the untrusted
context-pad append drag and the `pointer-events:none` trusted-click overlay are validated **headed**
only (NFR-02). Headless parity is explicitly **future work**; passing `--headed false` is accepted by
the parser but documented as unverified.

### Security

- **CDP loopback only** (NFR-06): the only local egress is `127.0.0.1:<DevToolsPort>` (the launcher
  binds remote debugging to loopback; `CdpSession` connects there). Creatio egress stays via the
  existing session / `IApplicationClient`.
- **No LLM/AI network call** in any slice code path (NFR-06, Goal 1) — verified by absence of such
  dependencies and by E2E network inspection.
- **Secret hygiene** (NFR-07): reuse the browser-session redaction guarantees — cookie **values** never
  appear in logs/MCP payloads/CLI stdout/`--debug` exceptions; **cookie NAMES only** (per
  `AuthenticatedBrowserLauncher`). The recipe must not echo cookie values; injected JS parameters are
  the source object + caption only.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute | One test per R-rule error/warning (R1, R2, R3, R10, R11, R13, R14, R15 errors; R7/R9, R12, R17 warnings) incl. AC-02/03/04/05/06; `Unknown` data-id classification | `clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs` |
| Unit | NSubstitute | `validate-process-graph` arg → `ProcessGraph` mapping; finding serialization shape | `clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs` |
| Unit | NSubstitute | `process-add-element` arg mapping; Destructive semantics (new vs `--process-id`); `read-data` guard | `clio.tests/Command/McpServer/ProcessAddElementToolTests.cs` |
| Unit | `BaseCommandTests<ProcessAddElementOptions>` | validate-abort-before-browser (AC-10); error classes (AC-ERR) with mocked `IProcessDesignerDriver`/`IBrowserSessionService`/`IAuthenticatedBrowserLauncher` | `clio.tests/Command/ProcessDesigner/ProcessAddElementCommandTests.cs` |
| Unit | NSubstitute | `describe-process` graph extraction from `clio.tests/Examples/ProcessSchema/*.json` fixtures (elements/flows/parameters); `ManagerMap.ResolveDataId`/role mapping (AC-13) | `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs` |
| Unit | NSubstitute | `describe-process` arg mapping; safety flags ReadOnly/non-destructive/idempotent; env-aware `InternalExecute<DescribeProcessCommand>` path | `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` |
| Unit | `BaseCommandTests<DescribeProcessOptions>` | identity guard (exactly one of code/uid/caption); not-found → `Error:` + exit non-zero (AC-ERR) with mocked read collaborator | `clio.tests/Command/DescribeProcessCommandTests.cs` |
| Unit | NSubstitute | `process-modeling` registered in `GuidanceCatalog`; resource returns `TextResourceContents` (AC-01) | `clio.tests/Command/McpServer/ProcessModelingGuidanceResourceTests.cs` |
| E2E | `clio.mcp.e2e` | `validate-process-graph` over the real MCP path | `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs` |
| E2E | `clio.mcp.e2e` | Live `Start → Read data → End` build + readback via `generate-process-model`/`execute-esq` on `VwProcessLib` (AC-07/08/09) | `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs` |
| E2E | `clio.mcp.e2e` | Live read of a known process → non-empty elements/flows/parameters (AC-13) | `clio.mcp.e2e/DescribeProcessToolE2ETests.cs` |

All unit tests: `[Category("Unit")]`, `Method_ShouldX_WhenY` naming, AAA + a `because` on every
assertion + `[Description]` on every test. **MCP E2E is NOT in CI** (needs Chromium + live forms-auth
env, e.g. `krestov-test`) — flag this in the test plan (NFR-02, A-05, FR-17).

Targeted regression filters (smart-testing policy): `Module=ProcessModel`, `Module=McpServer`,
`Module=Command`, `Module=Common`. Touching `BindingsModule.cs`/`Program.cs`/`Common/` triggers the
**full unit suite** (rule 4).

---

## Consequences

- **Positive**:
  - First end-to-end AI → live-designer build path, proven feasible (`krestov-test`,
    `UsrProcess_493d4c9`), with deterministic readback (caption → `VwProcessLib`).
  - `validate-process-graph` gives the agent cheap pre-build feedback (R1–R17) and is fully unit-testable
    with no I/O.
  - `describe-process` (FR-19) adds the symmetric "read & explain" capability at near-zero backend cost
    by reusing the shipped `ProcessSchemaRequest` parsing — the agent can now both build and understand
    processes with one shared `data-id` vocabulary.
  - Sibling-driver design (OQ-03) keeps the shipped `AuthenticatedBrowserLauncher` flow stable while
    adding `Runtime.evaluate`; shared `CdpSession` removes duplicate plumbing.
  - One vocabulary (catalog `data-id`) across guidance, validator, designer, and the read-side extractor
    (OQ-05 / FR-19).
  - Embedded versioned JS recipe is the exact artifact a future Playwright/CI harness reuses.
- **Trade-offs / risks**:
  - **R-01 UI fragility** (NFR-05, A-01): the designer is diagram-js/bpmn-js in the Ext shell;
    `data-id`/selector drift across Creatio versions can break the recipe. Mitigated by the
    `.djs-validate-outline` live gate + clio readback gate + the catalog as the single selector source.
  - **R-02 untrusted-append default** (A-01): if `add.serviceTask` stops defaulting to `readDataUserTask`
    or the append affordance changes, the driver appends the wrong element or fails — caught by the
    validate-outline + readback gates; morph-via-`[data-action=setup]` is the documented fallback.
  - **R-03 not transactional** (NFR-04): a mid-run failure can leave an unsaved designer (discarded) or,
    post-SAVE, a saved process the caller must clean up. clio never reports false success.
  - **R-04 headed-only**: no headless/CI parity this increment (NFR-02); MCP E2E not in CI (FR-17).
  - **R-05 launcher refactor**: extracting `CdpSession` touches a shipped path
    (`open-web-app --authenticated`) — mitigated by keeping the contract identical and relying on the
    launcher's existing E2E plus new `CdpSession` unit tests. Triggers the full unit suite per the
    smart-testing rule (`Common/` changed).
  - **R-06 OAuth-only envs unsupported** (NFR-01): fail closed with AC-ERR.
  - **R-07 describe-process structural-only** (A-07, FR-19): v1 reconstructs the graph from parsed flow
    elements and does **not** decode filter/mapping expressions; if a process's behavior is driven mainly
    by complex filters/mappings, the narration is structural only until v2. Mitigated by the explicit v1
    limitation in the PRD/ADR and by reusing the already-tested parsing.
- **Breaking change**: **No** new breaking CLI change. `IAuthenticatedBrowserLauncher.LaunchAsync` MAY
  change its return type (port handoff) — that is an **internal** interface (no public CLI/MCP contract),
  so no `RELEASE.md` migration entry is required, but the change summary must note the launcher refactor.

---

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (`--element-type`, `--read-object`, `--process-id`,
      `--process-caption`, `--headed`, `--process-code`, `--process-uid`, `--culture`) — CLIO001; no
      camelCase, no aliases needed.
- [ ] `IProcessGraphValidator`, `IProcessDesignerDriver`, `ICdpSession`, `ProcessAddElementCommand`,
      `IProcessGraphExtractor`, `DescribeProcessCommand` registered in `BindingsModule.cs` (DI; no `new`
      of behavior classes).
- [ ] `process-add-element` and `describe-process` added to `Program.cs` verb `Types[]` + execution arms;
      `validate-process-graph` is MCP-only (not in `Program.cs`).
- [ ] `process-modeling` registered in `GuidanceCatalog` (canonical name) and reachable via
      `get-guidance`; reused by `describe-process` for narration (no new resource).
- [ ] No MediatR; no raw `HttpClient`; CDP loopback-only; no LLM call in any path.
- [ ] Error messages are user-friendly `Error:` strings (no stack traces unless `--debug`).
- [ ] Cookie VALUES never logged (names only — reuse browser-session redaction).
- [ ] MCP tools carry correct safety flags; `validate-process-graph` ReadOnly; `describe-process`
      ReadOnly + env-sensitive (`InternalExecute<DescribeProcessCommand>`); `process-add-element`
      Destructive semantics per OQ-01; prompts + `docs/McpCapabilityMap.md` updated.
- [ ] `clio.mcp.e2e` coverage added for all three new tools (flagged: not in CI).
- [ ] Docs updated: `help/en/process-add-element.txt`, `docs/commands/process-add-element.md`,
      `help/en/describe-process.txt`, `docs/commands/describe-process.md`, `Commands.md`,
      `docs/McpCapabilityMap.md`; "MCP reviewed, no update required" recorded for
      `generate-process-model`/`execute-esq`.
- [ ] Validator + describe-process reuse `ManagerMap.EventType` (extend with `ResolveDataId`); no
      taxonomy re-derivation; `describe-process` reuses the existing `ProcessSchemaRequest` parsing.
- [ ] JS recipe shipped as a versioned `<EmbeddedResource>`; parameters JSON-escaped, not concatenated.
- [ ] `describe-process` output is structured JSON (elements/flows/parameters), NOT raw escaped
      `metaData`; v1 filter/mapping limitation documented.
- [ ] Existing tests possibly affected: `AuthenticatedBrowserLauncher` E2E (launcher refactor),
      `Schema.cs` `ManagerMap` consumers (`ProcessModelWriterTests`, `SchemaTestFixture`).
