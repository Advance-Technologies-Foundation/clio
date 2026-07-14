# Story 1: Document Current Read Path (Phase A)

**Feature**: read-column-default-values
**FR coverage**: FR-01
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Phase**: A — investigation (documents only; SM-01 counter = **empty code diff**)
**Depends on**: none

---

## As a

architect (consumer of the investigation evidence)

## I want

the current default-value read path documented end to end — commands, MCP tools, service endpoint, DTO shape, and mapping code — with file/line references

## So that

the FR-04 comparison and any follow-up design work against verified facts instead of re-investigating the code

---

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-01)** — Given the investigation doc
  `spec/read-column-default-values/read-column-default-values-current-path.md`,
  when the Architect reads it, then it documents the exact HTTP endpoint clio calls
  for default-value readback
  (`ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem`) and the full
  DTO-to-`default-value-config` mapping, each claim backed by file/line references.
- [ ] **AC-02** — Given the doc, when reviewed, then it covers the whole chain:
  `get-entity-schema-column-properties` verb / MCP tool →
  `GetEntitySchemaColumnPropertiesCommand` →
  `RemoteEntitySchemaColumnManager.GetColumnProperties()` →
  `RemoteEntitySchemaDesignerClient.GetSchemaDesignItem()` → HTTP POST via
  `IApplicationClient`, plus the `DefValue` DTO fields
  (`ValueSourceType`, `Value`, `ValueSource`, `SequencePrefix`,
  `SequenceNumberOfChars`) and the `CreateDefaultValueConfig` mapping to sources
  `None | Const | Settings | SystemValue | Sequence`.
- [ ] **AC-03** — Given the doc, when reviewed, then the known lookup-`Const` weak
  spot is recorded explicitly: raw-GUID-only readback, no display value, no existence
  validation, `ValidateDefaultValueConfig` blocking `Const` only for binary-like types.
- [ ] **AC-ERR (SM-01 counter)** — Given `git diff` for this story's PR, when
  inspected, then it contains **only** files under `spec/` — zero production-code
  changes.

## Implementation Notes

Documents-only story. Deliverable (repo feature-doc naming convention):
`spec/read-column-default-values/read-column-default-values-current-path.md`

Code locations to reference (from ADR story A1):

- `clio/Command/GetEntitySchemaColumnPropertiesCommand.cs` — CLI/MCP entry
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs:158-191` — `GetColumnProperties`
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs` — `GetSchemaDesignItem`
- `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs:503` — `CreateDefaultValueConfig`
- `clio/Command/EntitySchemaDesigner/EntitySchemaReadModels.cs:65-89` — DTO shapes
- Prior art: `spec/entity-schema-default-values/entity-schema-default-values-plan.md`
  (shipped 8.0.2.47 contract this feature extends)

Key file: `spec/read-column-default-values/read-column-default-values-current-path.md` (new)
Pattern to follow: evidence-doc style of `spec/entity-schema-default-values/*.md`

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| None | Documents-only story — no production code, no tests. Verification = AC review of the evidence doc + empty code diff check | — |

## Definition of Done

- [ ] `spec/read-column-default-values/read-column-default-values-current-path.md` exists and satisfies AC-01..AC-03
- [ ] Every code claim carries a file/line reference that resolves against current `master`
- [ ] `git diff` contains only `spec/**` files (SM-01 Phase A counter)
- [ ] No production code, no new CLI flags, no test changes
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing: n/a (documents-only)
- Notes:
