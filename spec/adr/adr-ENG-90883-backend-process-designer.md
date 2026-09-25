# ADR: Backend command-driven process designer (clioprocessbuilder)

> **Renamed since this ADR was written.** The package is now **`CrtProcessBuilder`** (ENG-94385), and it
> is **bundled into clio and installed with `clio install-process-builder`** — the "package delivery /
> install wiring" this ADR lists as an open trade-off is now closed. The descriptor `UId` and the REST
> service class `ProcessDesignService` were deliberately preserved, so every architectural statement
> below still holds; only the package name changed. One design point *did* change: the package now ships
> as **source only** and the target environment compiles it, so there is no per-framework assembly.
> The delivery decision now has its own ADR — [adr-deliver-process-builder-package.md](adr-deliver-process-builder-package.md),
> which supersedes this ADR's delivery section — with the requirements in
> [spec-deliver-process-builder-package.md](../prd/spec-deliver-process-builder-package.md) and the
> experiment log in [deliver-process-builder-package-plan.md](../deliver-process-builder-package/deliver-process-builder-package-plan.md).
> The GHE repository was renamed too — `cli-process-builder` → **`crt-process-builder`** (ENG-95409),
> which changed nothing but the name: the descriptor `UId`, the REST route and the assembly name are all
> independent of it. The names `clioprocessbuilder` and `cli-process-builder` are left in place below as
> the historical record.

**Jira**: [ENG-90883](https://creatio.atlassian.net/browse/ENG-90883) (Approach 1 of [ENG-91447](https://creatio.atlassian.net/browse/ENG-91447); follow-ups [ENG-91842](https://creatio.atlassian.net/browse/ENG-91842), [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) — the validator on the write path)

**Confluence**:
- [Research: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4702928908) — three-approaches investigation; §3 records the Approach 1 + package decision
- [Backend designer — architecture & delivery options](https://creatio.atlassian.net/wiki/spaces/TER/pages/4752572418) — clio ↔ `clioprocessbuilder` split, request-flow sequence diagram, placement rationale (package vs core vs clio-only)
- [Backend designer — capability status (implemented & verified)](https://creatio.atlassian.net/wiki/spaces/TER/pages/4769087495) — per-capability matrix (works / partial / not yet built)
- [Backend designer vs UI automation — head-to-head](https://creatio.atlassian.net/wiki/spaces/TER/pages/4750180427) — Approach 1 vs Approach 3 comparison

**Companion doc**: [backend-designer-manual-qa.md](../backend-designer/backend-designer-manual-qa.md) (manual QA checklist)

**Code**: GHE `engineering/cli-process-builder` (package `clioprocessbuilder`); clio MCP layer in this repository

**Created**: 2026-06-25

---

## Context

An AI agent (and clio CLI users) must create, edit and read Creatio business processes **without** the Freedom UI visual designer — purely from a declarative intent. Creatio's process model is non-trivial: a process needs a `LaneSet → Lane` with nodes contained in the lane, sequence flows with no container, designer-faithful defaults (`Tag="Business Process"`, `IsCreatedInSvg`, `IsInterpretable`, `SerializeToDB`), user-task parameter synchronization, meta-path mapping tokens, and palette curation via `SysProcessUserTask`. The serializer/persister lives behind `Terrasoft.Core` process APIs (`ProcessSchemaManager`, `ProcessSchema*` element classes), several of which are `public` but version-fragile, and some (`*FromMetaData`) are `internal` and uncallable from a package.

Earlier explorations considered driving the visual designer over CDP (approach 3). That path is brittle (DOM/automation coupling) and cannot run headless/server-side. A durable, server-side, intent-first capability was needed.

## High-level design

```mermaid
flowchart TB
  agent["AI agent / clio user — plain-language intent"]

  subgraph clio["clio (Layer 2, outside Creatio)"]
    verbs["MCP tools / prompts (no public CLI verbs):<br/>create-business-process, modify-business-process,<br/>describe-business-process, list-user-tasks"]
    validator["R1–R20 graph validator<br/>(validate-process-graph)"]
    validator -. "advisory pre-flight,<br/>create-business-process only" .-> verbs
  end

  subgraph pkg["Creatio package: clioprocessbuilder"]
    svc["ProcessDesignService<br/>(thin WCF transport)"]
    orch["ProcessDesigner — domain orchestrator<br/>sequencing · CanManageProcessDesign + General gate · errors"]
    collab["Single-concern collaborators:<br/>graph · schema · parameters · mappings ·<br/>operations · user-task catalog · describe · layout"]
    svc --> orch --> collab
  end

  mgr["Platform ProcessSchemaManager<br/>serialize / save / design session"]
  store[("Schema store:<br/>DB (runtime) · FS (file-design mode)")]

  agent --> verbs
  verbs -- "POST /rest/ProcessDesignService/&lt;Method&gt; (wrapped body)" --> svc
  collab --> mgr --> store
```

_The boundary this ADR governs is the REST edge between **clio** and the **package**; everything to the right of `ProcessDesignService` runs inside Creatio and is the package repo's concern (see "Internal architecture — principle only" below). The platform `ProcessSchemaManager` owns serialization/persistence — on a DB stand a save is immediately runtime-runnable; in file-design mode it is FS-only until an FS→DB load + publish._

## Decision

Deliver process design as a **backend command-driven "non-visual designer"**, packaged as a **cliogate-style Creatio configuration package `clioprocessbuilder`** (a new sibling to `cliogate`), with a clear two-layer split:

- **Package layer (`clioprocessbuilder`, in Creatio)** — owns build/modify/read/serialize via the platform managers. Exposed as a thin WCF service `ProcessDesignService` (`[ServiceContract] : BaseService`) at **`/rest/ProcessDesignService/<Method>`** (wrapped body style; `Build()` prepends `0/` on net472). The service is a transport shell that resolves the domain orchestrator `IProcessDesigner` from a per-request DI scope (`ClioProcessBuilderApp` composition root) and delegates.
- **clio layer (Layer 2)** — owns intent/MCP/orchestration: `ServiceUrlBuilder.KnownRoute` entries, `Command<TOptions>` command classes (`create-business-process`, `modify-business-process`, `list-user-tasks`, `describe-business-process`) exposed **only** as MCP tools (no public CLI verbs — see "Feature gating"), their MCP tools/prompts/guidance, and the R1–R20 `IProcessGraphValidator` (common-core), exposed as `validate-process-graph` and run by `create-business-process` as an **advisory** pre-flight — never as a gate, and not at all on the modify path (see "The validator on the write path" below).

### The validator on the write path (ENG-95244)

- **The server is the gate.** On create, the package's `ValidateStructure` covers clio's R1/R2/R3/R15; `FlowKindRules` covers R11/R13/R14/R18/R19/R20 on create **and** modify; the platform's own process validation runs on both; nothing is persisted on a refusal. The places where clio's rules and the server's part are pinned package-side by `ProcessValidateBuildDivergenceTests`.
- **Create: advisory pre-flight.** Before the POST, `CreateBusinessProcessService` maps `elements[]`/`flows[]` to a `ProcessGraph` (`IProcessDescriptorPreflight`) and writes, as warnings, only the findings the server does **not** report: R8 (a parallel join behind a choice — the instance hangs in Running with no error), R7/R9 with no default branch, R13 off an event, and R17. It never blocks. Errors are not shown, because an error is by the validator's own rule a shape the build refuses and the server's message for it is authoritative; warnings the build also reports (R12 as a notice; the plain-flow R7/R9 as a notice, or as a refusal beside a declared default; R13 with no condition and `UNBUILDABLE` as refusals) carry `ProcessGraphFinding.ReportedByBuild` and are not repeated either. A descriptor the pre-flight cannot read the way the server does (an unknown flow kind, a non-string condition) produces no lines at all rather than a verdict on a different graph.
- **Modify: nothing, deliberately.** `modify-business-process` and `modify-business-process-as-new-version` take operations, not a graph, and a whole-graph check would judge the process being edited rather than the edit: 37 processes shipped in 7.8.0 carry a start event the whole-graph rules reject, so they would become uneditable for any change (`docs/knowledge/ProcessModel/start-event-arity-is-enforced-on-create-not-modify.md`). Per-operation authoring rules belong in `FlowKindRules`, where they already are; a package-side "do not make it worse" guard is a separate, optional item.
- **Buildability has one source.** `ManagerMap.IsBuildable` decides which element kinds create/modify can build; the validator reports every other recognized kind as an `UNBUILDABLE` warning, and `ManagerMapResolveDataIdTests` pins the server's whole token list (`ProcessDesignConstants.ElementTypes`, compared against the bundled archive) as recognized and buildable.

### Service surface
- `BuildProcess({name, caption, packageName, elements[], flows[], parameters[], mappings[]})` — declarative descriptor in; builds the lane, materializes elements, connects flows, adds parameters (optionally with a constant default value) and mappings, auto-lays-out, saves.
- `ModifyProcess({name|uid, operations[]})` — **operation-list model**, applied in order over an editable design instance, saved once; any op failure aborts (nothing saved).
- `DescribeProcess({name|uid})` and `ListUserTasks()` — structured read-back / palette catalog.

### Internal architecture (package) — principle only
The package is a thin WCF transport (`ProcessDesignService`) over a domain orchestrator that owns only operation sequencing, the security gate, and error-to-response handling, delegating each concern (graph build/edit, schema lifecycle, parameters, mappings, modify operations, user-task catalog, describe, layout) to a single-purpose, constructor-injected collaborator — so new element kinds and operations **extend the collaborators, not the orchestrator** (per-element handler strategy). The concrete collaborator interfaces, handlers and the DI composition root are the **source of truth in the `engineering/cli-process-builder` repository**, and the architecture rationale + request-flow sequence diagram live in the Confluence [architecture & delivery options](https://creatio.atlassian.net/wiki/spaces/TER/pages/4752572418) page; this ADR records only the **boundary contract** (the service surface above) and the cross-cutting decisions below — not the package's internal class structure (which would drift, since the code lives in another repo).

### Key design choices recorded
1. **Security gate = `CanManageProcessDesign` + General user**, mirroring the platform's own `ProcessSchemaManagerService.Publish`. The cliogate `CanManageSolution` operation is broader and omits the connection-type check, so it is intentionally **not** used.
2. **End element = `ProcessSchemaTerminateEvent`** (the non-deprecated class the designer itself places), not the legacy `ProcessSchemaEndEvent`. Runtime-identical (both → `ProcessTerminateEvent`), but avoids persisting a deprecated class.
3. **User-task specialization** — set the element `ManagerItemUId = taskSchema.UId` **only** when the task has a dedicated palette element (`HasDedicatedPaletteElement` ⇒ present in `SysProcessUserTask`); otherwise keep the generic "User task" container. A blanket-set is wrong (no dedicated editor → renders incorrectly).
4. **Signal start (record trigger)** is the supported alternative to a client `crt.SaveRecordRequest` save-handler. `EntitySignal` is a **single** `EntityChangeType` value (the designer keys its dropdown by a single value; a combined flag renders empty); `save` ⇒ `Updated`.
5. **Mappings** are written as `Source=Script` with the process-parameter `GetMetaPath()` token (`[#…[Parameter:{uid}]#]`); a new `SourceValue` is **assigned** (its setter auto-syncs `schema.Mappings`), and `Source` is set **before** `Value`.
6. **Auto-layout** is a single topological re-layout pass before each save (start leftmost, end/terminate strictly rightmost), not per-element positioning.
7. **Read path** prefers the runtime instance (`FindInstanceBy*`); falls back to `DesignSchema` + `GetDesignInstance` for FSD/uncompiled processes.
8. **Process-parameter types are an allow-list** — the scalar set (Text, Long text, Integer, Float, Money, Boolean, Date, Date-time, Time, Guid) plus **Lookup** (`referenceSchema`). Other `DataValueType`s (composite, entity / entity-collection, binary-file, color, image, enum, secure-hash text) are rejected with a clear error. The guard is necessary because `DataValueTypeManager` **resolves some of them** (e.g. `Binary` — which would otherwise create an unsupported parameter silently); the rest only surface a raw "not found". These types are deferred (mainly web-service integration).
9. **A process parameter can carry its own value** (an initial/default), using the same `ProcessSchemaParameterValue` source model as element mappings (#5). Only a **constant** (`Source=ConstValue`) is honored today; the descriptor keeps the mapping-style `{value|expression|processParameter}` shape so non-constant defaults are a later, non-breaking addition. A constant **scalar/lookup** value is type-checked at write time by mirroring the platform's runtime parameter-value initializer (invariant `int`/`decimal`/`double`/`bool`/`Guid` parse — the same parse the process engine applies to a `ConstValue`), rejecting e.g. a non-numeric Integer default; **text and Date/Date-time/Time** are left to that runtime initializer (its date / culture handling is feature-flag-governed), so a bad value of those types still surfaces at runtime.
10. **Parameter identity is the UId.** Every reference (mappings, formulas, signature/describe) resolves by UId (#5), so a parameter update **mutates it in place** (preserving the UId) rather than recreating it — no reference rewriting, no dangling refs.
11. **Parameter removal is dependency-validated**, mirroring the visual designer's `canRemoveParameter`: a parameter referenced by another parameter's value, or by an element/task mapping, cannot be deleted — a hard block, not a warning. Usage is found by scanning value sources (Script / Mapping / ConstValue) for the parameter's UId meta-path token (#5); the error names the usage site (which parameter / which element + parameter).

## Alternatives Considered

| Decision point | Option | Status |
|---|---|---|
| Capability shape | Drive the visual designer over CDP (approach 3) | Rejected — brittle DOM/automation coupling, cannot run headless |
| | Backend command-driven service (Approach 1) | **Chosen** — server-side, intent-first, testable |
| Where it ships | Platform core | Rejected — couples the version-fragile internal BP-API to core; slow to ship |
| | Standalone config package `clioprocessbuilder` | **Chosen** — isolates the fragile dependency, ships like `cliogate` |
| Security gate | `CanManageSolution` (cliogate default) | Rejected — broader than process design; omits user-type check |
| | `CanManageProcessDesign` + General user | **Chosen** — matches the platform's own gate |
| End element | `ProcessSchemaEndEvent` (legacy) | Rejected — deprecated; filtered from the palette |
| | `ProcessSchemaTerminateEvent` | **Chosen** — what the designer emits; non-deprecated |
| User-task element | Always set specialized `ManagerItemUId` | Rejected — non-palette tasks have no dedicated editor → misrender |
| | Specialize only when in `SysProcessUserTask` | **Chosen** |

## Consequences

- **Positive**: a headless, intent-first BP build/modify/describe capability; clean separation (clio orchestration/validation vs package serialization); an orchestrator that stays small as element kinds grow; designer-faithful output (verified live and over REST).
- **Test strategy**: the package is unit-tested as a Creatio configuration unit test suite. Because `Terrasoft.Core.Tests` is **not shipped** to package projects, the platform `ProcessSchemaBaseTestCase` patterns are **copied** (a local `ProcessDesignTestSupport`), not referenced. See [[clioprocessbuilder-unit-test-patterns]] (agent memory) for the substitution techniques.
- **Genuine E2E boundary**: `ProcessSchemaManager.CreateSchema` / `SaveSchema` / `DesignSchema` and friends are **non-virtual** → unmockable; the create/save/design-session lifecycle is verified at the API E2E layer (against a live stand), exactly as the platform's own tests do.
- **FSD / persistence caveat**: in file-design mode, `BuildProcess` saves to the file system only (the designer sees it) — the process is **not** in `VwProcessLib` and not runtime-runnable until an FS→DB load + publish. On non-FSD environments `SaveSchema` writes to the DB and the process is immediately runnable.
- **Round-trip caveat (describe → build)**: describe's `type` is the runtime .NET class name (`ProcessSchemaUserTask`, …), which `build`/`modify` do **not** consume — they take descriptor tokens (`usertask`, `endevent`, …). Ids, flows, parameters, `userTaskName` and `signal` round-trip; for the element kind, describe additionally emits a `buildType` token (the round-trippable counterpart) so the read-back graph can be fed back into build. The full token mapping across the three commands — `create`/`modify` descriptor `type` ↔ describe `buildType` ↔ `validate-process-graph` diagram-js data-id, plus the note that the validator vocabulary is a superset — is given in the "Element type vocabulary" subsection below.

### Element type vocabulary

The three process-designer surfaces use **different element-type tokens**, so a value read from one
must be translated before it is passed to another. This table is the canonical mapping for the
element kinds the backend designer can **build** today:

| Element kind | `create`/`modify` descriptor `type` (= describe `buildType`) | describe runtime `type` | `validate-process-graph` node `type` (diagram-js data-id) | Role |
|---|---|---|---|---|
| Start event | `startevent` | `ProcessSchemaStartEvent` | `startEvent` | Start |
| Signal start event | `signalstart` | `ProcessSchemaStartSignalEvent` | `startEventSignal` | Start |
| End event | `endevent` | `ProcessSchemaTerminateEvent` | `endEvent` | End |
| User task (generic) | `usertask` (+ `userTaskName`) | `ProcessSchemaUserTask` | `userTask` (or `<schema>UserTask`) | Activity |
| Read / Modify / Add / Delete data | `readdata` / `changedata` / `adddata` / `deletedata` | `ProcessSchemaUserTask` | `readDataUserTask` / `changeDataUserTask` / `addDataUserTask` / `deleteDataUserTask` | Activity |
| Change access rights | `changeaccessrights` | `ProcessSchemaUserTask` | `changeAdminRightsUserTask` | Activity |
| Perform task | `performtask` | `ProcessSchemaUserTask` | `activityUserTask` | Activity |
| Send email | `sendemail` | `ProcessSchemaUserTask` | `emailTemplateUserTask` | Activity |
| Approval | `approval` | `ProcessSchemaUserTask` | `approvalUserTask` | Activity |
| Open edit page | `openeditpage` | `ProcessSchemaUserTask` | `openEditPageUserTask` | Activity |
| Pre-configured page | `preconfiguredpage` | `ProcessSchemaUserTask` | `preconfiguredPageUserTask` | Activity |
| Formula | `formulatask` | `ProcessSchemaFormulaTask` | `formulaTask` | Activity |
| Exclusive (OR) gateway | `exclusivegateway` | `ProcessSchemaExclusiveGateway` | `exclusiveGateway` | Gateway |
| Parallel (AND) gateway | `parallelgateway` | `ProcessSchemaParallelGateway` | `parallelGateway` | Gateway |
| Sub-process | `subprocess` | `ProcessSchemaSubProcess` | `callActivity` | Activity |

The descriptor column is the server's `ProcessDesignConstants.ElementTypes`, matched case-insensitively
(`ProcessElementFactory`); a camelCase spelling such as `readData` is the same token.

Notes:

- **Casing and vocabulary differ by surface, and the validator bridges them.** `create`/`modify` and
  describe's `buildType` use the collapsed token (`startevent`); the canvas uses the diagram-js data-id
  (`startEvent`). `validate-process-graph` accepts **both** spellings, case-insensitively, through
  `ManagerMap.ResolveDataId`, so a described graph can be validated without translation. The reverse is
  not true — `create`/`modify` accept only the descriptor tokens.
- **The validator vocabulary is a superset, and says so per node.** `validate-process-graph` also
  recognizes the inclusive and event-based gateways, timer/message starts, intermediate events
  (`intermediateCatchEvent…` / `intermediateThrowEvent…`), `scriptTask`, `webService` and the event
  sub-process (`eventSubProcessExpanded`), so it can check hand-authored graphs — and reports each such
  node as an `UNBUILDABLE` warning, because `create` / `modify` refuse those types. `ManagerMap.IsBuildable`
  is the one place that decision lives. Any specific `<schema>UserTask` data-id (suffix `UserTask`)
  validates as a buildable user-task activity.
- **`describe.type` is never consumable.** It is the runtime .NET class name; always round-trip through
  `buildType`, not `type`.
- **Extensibility caveat (flows vs elements)**: the "new element kind = one handler + one DI line" property holds for **elements**. **Flows are different**: all three flow kinds are buildable declaratively (`flows[].kind` with `flows[].condition`, or `flows[].results` for a branch decided by an activity result), but each new kind or predicate slot was a contract change plus a `FlowKindRules` entry plus branch-aware layout, not a handler.
- **Feature gating**: the feature is **MCP-only**. There are **no public CLI verbs** — the MCP tools run the command classes directly via `InternalExecute<TCommand>` (never the `[Verb]`/`Program.cs` dispatch), and the descriptor JSON is an AI-translation target, not a human authoring format, so a CLI verb would carry a doc/help/wiki/test/alias maintenance tail with no consumer (YAGNI). The command classes, services, DI, options classes and MCP surface remain; a CLI verb can be added if a concrete human/CI consumer (e.g. descriptor-as-code provisioning) appears. **Go-live (2026-08-28, ENG-96132):** `[FeatureToggle("process-designer")]` was removed from all five MCP tools and five prompts, and the `process-modeling` guidance article's `requiredFeatures` gate was dropped in `clio-knowledge` (published first) — business process creation now works by default with no feature flag. The surface stays long-tail (non-resident); regression is pinned by `ProcessDesignerGoLiveTests` and the rewritten `InstallProcessBuilderContractToolE2ETests`. The cross-repo ordering is not prose-only: `WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` fails CI if an un-gated tool's description names a guidance article the pinned generation still lists as feature-gated, so an out-of-order publish cannot merge silently.
- **Trade-offs / open items**: package delivery is closed (`install-process-builder`, see the note at the top), and so is the validator's place on the write path (advisory on create, absent on modify — see "The validator on the write path" above). Still open: a descriptor has no key schema, so a misspelled or wrongly-cased optional key is dropped by the server in silence (ENG-95244, strict descriptor keys); `setElement`, element-mapping edit/clear ops, non-constant parameter defaults, and **changing an existing parameter's data type** remain TODO; the deploy loop on the dev stand is manual (compile/restart over MCP time out).

## Notes

This ADR is the in-tree architectural reference for the backend process-designer feature; the continuously-updated capability state lives on the Jira ticket and the Confluence pages linked above.
