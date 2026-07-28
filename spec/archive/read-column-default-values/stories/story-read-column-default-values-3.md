# Story 3: Ticket-Case E2E Scenario + Supplementary OQ-03/OQ-05 Evidence (Phase A)

**Feature**: read-column-default-values
**FR coverage**: FR-03
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: ready-for-dev
**Size**: L (full day)
**Phase**: A — investigation (documents only; SM-01 counter = **empty code diff**)
**Depends on**: none (gate: access to a real Creatio environment; recommended after stories 1–2 per ADR ordering A1 ∥ A2 → A3)

---

## As a

QA engineer (and AI no-code agent stakeholder of epic ENG-85256)

## I want

the ticket's lookup-default scenario executed end to end on a real environment with every clio/MCP call and response recorded, ending in a strict pass/fail verdict against the machine-verifiable predicate

## So that

the FR-04 decision and any Phase B scope rest on observed payloads — including the persisted `defValue` shape and the runtime-applied value — not on assumptions A-02/A-04

---

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-03)** — Given a fresh environment, when the FR-03 scenario is
  executed (all six normative steps below), then
  `spec/read-column-default-values/read-column-default-values-e2e-scenario.md`
  records each clio/MCP call, its response, the persisted `defValue` payload, and
  the runtime-applied column value — with a **pass/fail verdict evaluated strictly
  against the machine-verifiable predicate** (PRD Definitions: source + GUID +
  referenced schema name + display value or marker; all four present → pass;
  otherwise fail, listing the missing components).
- [ ] **AC-02 (six normative steps, SM-02 counter)** — Given the recorded scenario,
  when reviewed, then all six steps used **clio commands/MCP tools only** (no manual
  UI step):
  1. Create the lookup entity (`create-lookup` / `create-entity-schema`) — empty table.
  2. **Insert at least one record into the new lookup** (`odata-create` or
     `create-data-binding` + `add-data-binding-row`) and **capture its GUID** —
     record the exact call (the ticket's "where does the agent get the GUID" answer).
  3. Add the lookup column to the target Object
     (`modify-entity-schema-column` / `update-entity-schema`).
  4. Set the lookup-record `Const` default using the GUID from step 2.
  5. Read back via `get-entity-schema-column-properties`; record the persisted
     `defValue` payload.
  6. **Runtime verification**: insert a record into the Object (e.g. `odata-create`)
     **without** supplying the column; read it back and confirm the column received
     the default (metadata persistence alone is insufficient — risk A-02).
- [ ] **AC-03 (N-01)** — Given OQ-03 evidence (designer-UI persisted `defValue`
  shape), when recorded, then it sits in a separate **"Supplementary evidence
  (non-normative)"** section — never among the six steps — so the SM-02
  "clio/MCP only" counter stays honest.
- [ ] **AC-04 (OQ-05 supplementary)** — Given the supplementary section, when
  reviewed, then it records whether `SystemValue` defaults (e.g.
  `CurrentUserContact`) apply to lookup columns, answering OQ-05.
- [ ] **AC-05 (N-02)** — Given the execution, when recorded, then at least one
  environment-matrix row is covered, and the doc states explicitly whether any
  observation (persisted `defValue` shape, runtime application) appeared
  platform-dependent — if yes, the second-row execution trigger is recorded.
- [ ] **AC-ERR (SM-01 counter)** — Given `git diff` for this story's PR, when
  inspected, then it contains **only** files under `spec/` — zero production-code
  changes.

## Implementation Notes

Documents-only story. Deliverable:
`spec/read-column-default-values/read-column-default-values-e2e-scenario.md`

- Record exact request/response payloads, including the persisted `defValue` shape
  (OQ-03: plain GUID string vs structured object with display metadata — this also
  tests assumption A-04: if display metadata is already in the DTO, Phase B story 6
  shrinks to a mapping change).
- Capture rough timing of the readback call — story 4 uses it to resolve OQ-04
  (enrichment default-on vs opt-in, A-05 latency).
- Verdict section must enumerate predicate components (a)–(d) individually.

Key file: `spec/read-column-default-values/read-column-default-values-e2e-scenario.md` (new)
Pattern to follow: ADR Implementation Plan, story A3 row (incl. N-01/N-02 rules)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| None | Documents-only story — no production code, no automated tests. The scenario itself is a recorded manual E2E execution; it becomes the seed for Phase B `clio.mcp.e2e` coverage (story 8) | — |

## Definition of Done

- [ ] `read-column-default-values-e2e-scenario.md` exists with all six steps, calls + responses, persisted `defValue` payload, runtime-applied value
- [ ] Strict predicate verdict recorded (pass/fail + missing components if fail)
- [ ] Supplementary non-normative section covers OQ-03 and OQ-05
- [ ] N-02 platform-dependency statement present
- [ ] `git diff` contains only `spec/**` files (SM-01 Phase A counter)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing: n/a (documents-only)
- Notes:
