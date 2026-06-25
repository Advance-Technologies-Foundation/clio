# ADR: Backend command-driven process designer (clioprocessbuilder)

**Status**: Accepted (retrospective — feature built & shipped; this ADR formalizes the decisions)
**Author**: Architect (retrospective record)
**Jira**: [ENG-90883](https://creatio.atlassian.net/browse/ENG-90883) (Approach 1 of [ENG-91447](https://creatio.atlassian.net/browse/ENG-91447); follow-ups [ENG-91842](https://creatio.atlassian.net/browse/ENG-91842))
**Confluence**:
- [Research: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4702928908) — three-approaches investigation; §3 records the Approach 1 + package decision (v22)
- [Backend designer — architecture & delivery options](https://creatio.atlassian.net/wiki/spaces/TER/pages/4752572418) — clio ↔ `clioprocessbuilder` split, request-flow sequence diagram, placement rationale (package vs core vs clio-only)
- [Backend designer — capability status (implemented & verified)](https://creatio.atlassian.net/wiki/spaces/TER/pages/4769087495) — per-capability matrix (works / partial / not yet built)
- [Backend designer vs UI automation — head-to-head](https://creatio.atlassian.net/wiki/spaces/TER/pages/4750180427) — Approach 1 vs Approach 3 comparison
**Handoff docs**: [process-design-service-state.md](../process-design-service/process-design-service-state.md), [backend-designer-manual-qa.md](../backend-designer/backend-designer-manual-qa.md)
**Code**: GHE `engineering/cli-process-builder` (package `clioprocessbuilder`) — PRs [#7](https://creatio.ghe.com/engineering/cli-process-builder/pull/7), [#9](https://creatio.ghe.com/engineering/cli-process-builder/pull/9); clio side on `feature/ENG-90883-approach1-backend-designer`
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
    verbs["CLI verbs and MCP tools/prompts:<br/>create-business-process, modify-business-process,<br/>describe-process, list-user-tasks"]
    validator["R1–R17 graph validator<br/>(validate-process-graph)"]
    validator -. "pre-flight" .-> verbs
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
- **clio layer (Layer 2)** — owns intent/MCP/orchestration: `ServiceUrlBuilder.KnownRoute` entries, `Command<TOptions>` verbs (`create-business-process`, `modify-business-process`, `list-user-tasks`, `describe-process`), MCP tools/prompts/guidance, and the R1–R17 `IProcessGraphValidator` (common-core) used as pre-flight.

### Service surface
- `BuildProcess({name, caption, packageName, elements[], flows[], parameters[], mappings[]})` — declarative descriptor in; builds the lane, materializes elements, connects flows, adds parameters and mappings, auto-lays-out, saves.
- `ModifyProcess({name|uid, operations[]})` — **operation-list model**, applied in order over an editable design instance, saved once; any op failure aborts (nothing saved).
- `DescribeProcess({name|uid})` and `ListUserTasks()` — structured read-back / palette catalog.

### Internal architecture (package) — principle only
The package is a thin WCF transport (`ProcessDesignService`) over a domain orchestrator that owns only operation sequencing, the security gate, and error-to-response handling, delegating each concern (graph build/edit, schema lifecycle, parameters, mappings, modify operations, user-task catalog, describe, layout) to a single-purpose, constructor-injected collaborator — so new element kinds and operations **extend the collaborators, not the orchestrator** (per-element handler strategy). The concrete collaborator interfaces, handlers and the DI composition root are the **source of truth in the `engineering/cli-process-builder` repository**, and the architecture rationale + request-flow sequence diagram live in the Confluence [architecture & delivery options](https://creatio.atlassian.net/wiki/spaces/TER/pages/4752572418) page; this ADR records only the **boundary contract** (the service surface above) and the cross-cutting decisions below — not the package's internal class structure (which would drift, since the code lives in another repo).

### Key design choices recorded
1. **Security gate = `CanManageProcessDesign` + General user**, mirroring the platform's own `ProcessSchemaManagerService.Publish`. The cliogate `CanManageSolution` operation is broader and omits the connection-type check, so it is intentionally **not** used. (Tightened in PR #7.)
2. **End element = `ProcessSchemaTerminateEvent`** (the non-deprecated class the designer itself places), not the legacy `ProcessSchemaEndEvent`. Runtime-identical (both → `ProcessTerminateEvent`), but avoids persisting a deprecated class. (PR #7.)
3. **User-task specialization** — set the element `ManagerItemUId = taskSchema.UId` **only** when the task has a dedicated palette element (`HasDedicatedPaletteElement` ⇒ present in `SysProcessUserTask`); otherwise keep the generic "User task" container. A blanket-set was wrong (no dedicated editor → renders incorrectly).
4. **Signal start (record trigger)** is the supported alternative to a client `crt.SaveRecordRequest` save-handler. `EntitySignal` is a **single** `EntityChangeType` value (the designer keys its dropdown by a single value; a combined flag renders empty); `save` ⇒ `Updated`.
5. **Mappings** are written as `Source=Script` with the process-parameter `GetMetaPath()` token (`[#…[Parameter:{uid}]#]`); a new `SourceValue` is **assigned** (its setter auto-syncs `schema.Mappings`), and `Source` is set **before** `Value`.
6. **Auto-layout** is a single topological re-layout pass before each save (start leftmost, end/terminate strictly rightmost), not per-element positioning.
7. **Read path** prefers the runtime instance (`FindInstanceBy*`); falls back to `DesignSchema` + `GetDesignInstance` for FSD/uncompiled processes.

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

- **Positive**: a headless, intent-first BP build/modify/describe capability; clean separation (clio orchestration/validation vs package serialization); an orchestrator that stays small as element kinds grow; designer-faithful output (verified live on `krestov-test` and over REST).
- **Test strategy**: the package is unit-tested as a Creatio configuration unit test suite (~93% line / 86% method, 144 tests). Because `Terrasoft.Core.Tests` is **not shipped** to package projects, the platform `ProcessSchemaBaseTestCase` patterns are **copied** (a local `ProcessDesignTestSupport`), not referenced. See [[clioprocessbuilder-unit-test-patterns]] (agent memory) for the substitution techniques.
- **Genuine E2E boundary**: `ProcessSchemaManager.CreateSchema` / `SaveSchema` / `DesignSchema` and friends are **non-virtual** → unmockable; the create/save/design-session lifecycle is verified at the API E2E layer (against a live stand), exactly as the platform's own tests do.
- **FSD / persistence caveat**: in file-design mode, `BuildProcess` saves to the file system only (the designer sees it) — the process is **not** in `VwProcessLib` and not runtime-runnable until an FS→DB load + publish. On non-FSD environments `SaveSchema` writes to the DB and the process is immediately runnable.
- **Round-trip caveat (describe → build)**: describe's `type` is the runtime .NET class name (`ProcessSchemaUserTask`, …), which `build`/`modify` do **not** consume — they take descriptor tokens (`usertask`, `endevent`, …). Ids, flows, parameters, `userTaskName` and `signal` round-trip; for the element kind, describe additionally emits a `buildType` token (the round-trippable counterpart) so the read-back graph can be fed back into build. The full token mapping across the three commands — `create`/`modify` descriptor `type` ↔ describe `buildType` ↔ `validate-process-graph` diagram-js data-id, plus the note that the validator vocabulary is a superset — is published in [`describe-process.md` → "Element type vocabulary"](../../clio/docs/commands/describe-process.md#element-type-vocabulary-round-trip-mapping). (Per PR #8 review M2.)
- **Extensibility caveat (flows vs elements)**: the "new element kind = one handler + one DI line" property holds for **elements**. **Flows are different** — only plain sequence flows are buildable; conditional/default flows and gateways require contract changes (`ProcessFlowDescriptor` already reserves optional `kind`/`condition`, and a non-sequence kind is rejected until implemented) plus branch-aware layout. (Per PR #8 review M3.)
- **Feature gating (deliberate asymmetry — PR #715 review B2)**: the **MCP** tools are gated behind `[FeatureToggle("process-designer")]` (the AI-facing surface stays hidden until the feature matures), while the **CLI** verbs are intentionally **public** and documented (the supported surface for power users). This is a conscious decision — not the `project-context.md` default of gating *all* surfaces in lock-step — recorded here so the asymmetry is intentional, not an oversight. Revisit (gate the CLI too, or ungate MCP) when the feature ships on by default.
- **Trade-offs / open items**: package delivery/install wiring (ship `.gz` like `cliogate`) and reusing the R1–R17 validator as BuildProcess pre-flight are still open (tracked in the state doc "Next / open"); Phase-2 modify ops (`setElement`, parameter/mapping edit ops) are TODO; the deploy loop on the dev stand is manual (compile/restart over MCP time out).

## Notes

This ADR is **retrospective**: it records decisions already made ([Research §3](https://creatio.atlassian.net/wiki/spaces/TER/pages/4702928908) — approach + placement, v22) and implemented, to give the clio repo a single in-tree architectural reference alongside the `spec/process-design-service/` and `spec/backend-designer/` handoff docs. The authoritative, continuously-updated state lives in [process-design-service-state.md](../process-design-service/process-design-service-state.md).
