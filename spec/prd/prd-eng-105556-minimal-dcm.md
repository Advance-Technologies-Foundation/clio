# PRD: create-dynamic-case - a minimal DCM from a lookup column, plus the record-page progress bar

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-11-10
**Jira**: ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" (epic ENG-92704 "Create BP via AI Toolkit")
**Research, design and plan**: [`spec/eng-105556-minimal-dcm/`](../eng-105556-minimal-dcm/README.md); the agreed decisions are the recommended options of its decisions file, with the additions below.

---

## Problem Statement

An agent that builds a Creatio application through clio cannot give an object a stage lifecycle. The clio
guidance has no DCM (dynamic case) article and no DCM tool; DCM appears only in one guidance sentence that
wrongly says a case carries its own compile obligations. Users who ask "track requests by their Status and
show the stages on the request page" get either nothing or a business process that drives the column, which
the Academy recommends against.

A minimal case is small: an interpreted process schema whose stages point at the values of a lookup column,
two settings rows the 7.x case designer needs, and one `crt.EntityStageProgressBar` on the Freedom UI record
page. Every platform member it needs is public, but the shape has silent traps (no stage connections freezes
the bar, a null colour crashes it, a missing settings link row stops the designer, a same-name package binding
drops another case's rows), so an agent cannot assemble it reliably from generic tools.

## Goals

- [ ] **G1 - One call.** One MCP call creates the case for an object + lookup column in a target package and
      adds the progress bar to the object's record page. **SM-01**: on the E2E sample object one call returns
      `outcome: created`, and the stored case, settings rows, bindings and page fragment match the expected
      shape (TC-I-02, TC-I-05).
- [ ] **G2 - Safe by construction.** A refusal, a `dry-run` and an equivalent re-run write nothing; a failure
      after the first write is compensated or reported with the exact leftover list. **SM-02**: every refusal
      code of the design (section 3.2) is covered by a unit test that asserts no write.
- [ ] **G3 - Correct obligations.** The tool reports that the case, the page and the settings data need no
      compile and no restart, per artifact. **SM-03**: `obligations.compileRequired = false` and
      `restartRequired = false` on every success, and no note contains the compile marker phrase.
- [ ] **G4 - Discoverable.** A guidance article routed from `routing` tells the agent when to use the tool,
      how stages are ordered, and what is not supported yet.

## Users

- AI agents building or extending Creatio apps through clio MCP (AI Toolkit).
- Developers who call the tool through `clio-run-destructive` and review its plan with `dry-run`.

## Scope

### In scope

1. CrtProcessBuilder package: a ninth `ProcessDesignService` operation `BuildCase` (gate, request contract,
   lookup read, stage plan, dependency plan, settings rows and bindings, DCM save last, read-back,
   compensation).
2. clio: the long-tail MCP tool `create-dynamic-case` (validate pass, record-page resolution and checks,
   append dry run, dependency add, build pass, progress-bar append), the rebundled archive with its security
   pins, the route, the `[RequiresPackage]` floor, docs, knowledge records, unit tests, the MCP E2E fixture.
3. clio-knowledge: article `processes/dynamic-case.md` (itemId `process-dynamic-case`), routing rows, the
   corrected `process-modeling` sentence.
4. CAADT prompt and Playwright spec; the SVN `AutoTestClioMcp` seed object for the E2E.

### Out of scope

- Steps inside stages, stage conditions, alternative stages, transition rules, stage permissions, colours per
  stage.
- Modifying, deactivating, deleting or versioning an existing case; changing an existing `SysDcmSettings` row.
- Classic, mobile and mini pages; typed record pages without `page-schema-name`.
- Creating or binding lookup values; any entity change (including the stage column's default value).
- A CLI verb, a second MCP tool, a feature toggle, any core change, any dependency on CrtGenAI.

## Acceptance Criteria

| AC | Done when |
|---|---|
| AC1 one call creates the case | `create-dynamic-case` with object + lookup column + package creates the DCM in the package with one stage per lookup value in the reported order (`StageRecordId` = row Id, `#0C8701`, n*(n-1) connections, enabled, active version), the `SysDcmSettings` + `SysDcmSchemaInSettings` rows and both bindings in the package; `dry-run`, an equivalent re-run and every refusal write nothing |
| AC2 opens in the designer | the case opens in the DCM designer by URL with the stages listed, no error box and no reset stage; a designer re-save keeps one binding per row |
| AC3 progress bar | the record page carries the two inserted items in the default design package; after F5 the bar shows the stages; a click changes the lookup value and a field change moves the bar |
| AC4 runtimes and obligations | compile/restart not needed and reported per artifact (`compileRequired:false`, `restartRequired:false`, `reloadOpenPages:true`); .NET 8 stated as "expected" until measured on a .NET 8 stand |
| AC5 tests | package suite green on dev-nf (dev-n8 once a net-core reference build is available); clio full unit suite; KB test project; MCP E2E by hand with Ignored = 0; CAADT prompt + spec |
| AC6 guidance | `process-dynamic-case` published, routed from `routing.md` (Business processes + Pages), says when/how and what is not supported yet; `process-modeling.md` no longer lists DCM among compile obligations |

Edge and error cases (each refused before any write with a stable `errorCode`): a non-lookup column, a column
missing from the runtime schema, an unknown or ambiguous stage value, an incomplete explicit `stages` list, a
lookup with fewer than 2 or more than 20 values, an enabled non-equivalent case on the object, a conflicting
settings row, a typed / mini / Classic record page.

## Decisions added during grilling (2026-11-10)

- **Equivalence before refusal.** When an enabled case exists on the object, the lookup values are read and the
  stages planned before the tool decides `already-present` vs `case-exists`; equivalent = same object, column
  and ordered `StageRecordId` list, link row present.
- **No filter column.** A settings row has no filter column when `Filters` is null, empty or
  `[{"columnUId":null}]`.
- **Page obligation and designer URL.** clio adds the `page` obligation artifact and builds `designerUrl`; the
  server answers the `dcm` and `settings-data` artifacts.
- **Forbidden fragment keys** include `changeToAppropriateEntityStageSchema` (the fourth wiring the runtime
  preprocessor creates).
- **Order of work.** PRD and ADR first; package, then clio-knowledge, then clio, then CAADT.

## Non-functional Requirements

- Nothing is written before every check of the call passes; the case schema is saved last.
- Rights are checked before any read (no name probing): General user, `CanManageDcm`, `CanManageSolution`,
  append on both settings tables.
- One MCP index line; the tool description is ASCII and names the article.
- .NET Framework and .NET 8 run the same code path; .NET 8 behaviour is reported as expected until measured.

## Dependencies

- A CrtProcessBuilder build that has `BuildCase`, rebundled into clio (`rebundle-process-builder.ps1`).
- The clio-knowledge generation that carries `process-dynamic-case`, published before the clio release.
- A read-write .NET Framework stand for the STOP gate (designer open, bindings, rights, residue) and a .NET 8
  stand for the .NET 8 run.

## Risks

| Risk | Mitigation |
|---|---|
| The case saved through the object model fails to open in the designer, or bindings land in another package | STOP gate after the experimental package cut; fallback: the designer save route |
| Default stage order is business-wrong | `orderSource` and captions always reported; `dry-run` preview; guidance tells the agent to pass `stages` |
| Residue after a failed save | schema saved last; leftover probe and cleanup order in `failed-leftover` |
| .NET 8 unmeasured | "expected" wording until the .NET 8 run |
| The rebundle makes every gated process tool refuse environments on the previous archive | version claimed above every deployed build; release note |
