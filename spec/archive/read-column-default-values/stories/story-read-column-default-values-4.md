# Story 4: Comparison Matrix + Keep/Adopt/Hybrid Decision + ADR Update (Phase A)

**Feature**: read-column-default-values
**FR coverage**: FR-04
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: ready-for-dev
**Size**: M (half day)
**Phase**: A — investigation (documents only; SM-01 counter = **empty code diff**)
**Depends on**: story-read-column-default-values-2, story-read-column-default-values-3 (A4 needs A2+A3 evidence)

---

## As a

architect

## I want

a comparison matrix scored against the fixed decision framework (D1–D7) with exactly one chosen read-path option, every rejection reasoned, each confirmed gap mapped to its DRAFT-AC item, and the ADR status updated

## So that

Phase B implementation (or its evidence-backed cancellation) starts from a closed, decision-grade record without re-investigation

---

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-04)** — Given the comparison matrix in
  `spec/read-column-default-values/read-column-default-values-comparison.md`, when
  the decision is recorded, then the Decision section contains **ADR-ready inputs**:
  exactly one chosen option (keep / adopt OData / hybrid), every rejected option
  with its rejection reason, and references to the evidence-matrix rows (including
  environment-matrix rows) that drove the choice.
- [ ] **AC-02 (decision framework)** — Given the matrix, when reviewed, then it is
  scored against **exactly** criteria D1–D7 from the ADR (no post-hoc criteria
  without an ADR update), and the ADR decision rule is applied: keep the
  designer-service read path unless OData `$metadata` strictly dominates on D1
  without regressing D2/D3/D4.
- [ ] **AC-03 (N-03, SM-03)** — Given the gap section, when the predicate verdict
  from story 3 is fail, then each confirmed gap maps to its DRAFT-AC item by ID
  (no display value on readback → DRAFT-AC-05; unvalidated Const GUID on write →
  DRAFT-AC-06; new gaps → new DRAFT-AC-N). If the verdict is pass, FR-05/FR-06/FR-07
  are recorded as closed "not needed" with evidence and Phase B is struck.
- [ ] **AC-04 (open questions closed)** — Given the doc, when reviewed, then:
  D7 (enrichment transport: OData data endpoint vs DataService `SelectQuery`) is
  resolved from D2/D3 evidence; OQ-04 (enrichment default-on vs opt-in) is resolved
  using FR-03 timing evidence; OQ-01 is either answered or the PRD fallback is
  applied (decision on empirical probe alone + explicit risk note).
- [ ] **AC-05 (ADR closure)** — Given the recorded decision, when this story
  completes, then `spec/adr/adr-read-column-default-values.md` is updated:
  Status → Accepted, final option recorded, D7 resolved; the A-02 / breaking-change
  note (dangling-GUID bypass, RELEASE.md) is addressed if FR-03 evidence triggered it.
- [ ] **AC-ERR (SM-01 counter)** — Given `git diff` for this story's PR, when
  inspected, then it contains **only** files under `spec/` — zero production-code
  changes.

## Implementation Notes

Documents-only story. Deliverables:

- `spec/read-column-default-values/read-column-default-values-comparison.md` (new)
- `spec/adr/adr-read-column-default-values.md` (status/decision update)
- `spec/prd/prd-read-column-default-values.md` (update only if FR-05/06/07 close as
  "not needed" — per PRD conditional clause)
- `spec/sprint-status.yaml` — flip Phase B stories 6–8 from `deferred` to
  `ready-for-dev` if the gap is confirmed, or annotate them as struck if not

Matrix dimensions per FR-04: capability, fidelity for the lookup case,
auth/permission requirements, version coverage (incl. environment-matrix and AC-ERR
rows from story 2), performance, maintenance cost. Inputs: story 2 probe doc,
story 3 scenario doc, OQ-01 answer or fallback.

Key file: `spec/read-column-default-values/read-column-default-values-comparison.md` (new)
Pattern to follow: ADR Decision Framework section (D1–D7 table + decision rule)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| None | Documents-only story — no production code, no tests. Verification = AC review against D1–D7 and the N-03 mapping | — |

## Definition of Done

- [ ] Comparison doc exists, scored against D1–D7, exactly one chosen option, all rejections reasoned with evidence-row references
- [ ] Gap→DRAFT-AC mapping complete (or "not needed" closure with evidence)
- [ ] D7, OQ-04 resolved; OQ-01 answered or fallback risk note recorded
- [ ] ADR updated: Status → Accepted, final option + D7 recorded
- [ ] sprint-status.yaml Phase B gate updated per the decision
- [ ] `git diff` contains only `spec/**` files (SM-01 Phase A counter)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing: n/a (documents-only)
- Notes:
