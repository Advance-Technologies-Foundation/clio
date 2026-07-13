# Story 5: Jira ENG-91318 Closure + Epic Surfacing (Phase A)

**Feature**: read-column-default-values
**FR coverage**: FR-08
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: ready-for-dev
**Size**: S (< 2h)
**Phase**: A — investigation (documents only; SM-01 counter = **empty code diff**)
**Depends on**: story-read-column-default-values-4

---

## As a

developer (ticket owner of ENG-91318)

## I want

the investigation outcome — the keep/adopt/hybrid decision with links to the comparison doc and evidence — recorded as a comment in Jira ENG-91318 and surfaced to epic ENG-85256

## So that

the ticket loop is closed and other teams in the epic can reuse the evidence-backed decision instead of re-running the investigation

---

## Acceptance Criteria

- [ ] **AC-01 (FR-08)** — Given the completed story-4 decision, when this story
  closes, then Jira ENG-91318 carries a comment containing: the chosen option
  (keep / adopt OData / hybrid), a one-paragraph rationale, and repo links to
  `read-column-default-values-comparison.md` and the evidence docs (stories 1–3).
- [ ] **AC-02 (epic surfacing)** — Given the ENG-91318 comment, when posted, then
  the outcome is surfaced to epic ENG-85256 (epic comment or link per the epic's
  convention), including the answer to the ticket's core question — "where does the
  agent get the GUID" (FR-03 step 2 evidence).
- [ ] **AC-03 (OQ-01 precondition)** — Given story 4's output, when this story
  starts, then the OQ-01 answer (or its documented absence + risk note) is already
  recorded in the comparison doc — this story does not close before that is true.
- [ ] **AC-04 (Phase B signal)** — Given the comment, when reviewed, then it states
  explicitly whether Phase B implementation is triggered (gap confirmed) or struck
  (readback already machine-verifiable), so the epic sees the conditional scope
  status.
- [ ] **AC-ERR (SM-01 counter)** — Given `git diff` for this story's PR (if any repo
  change is needed at all), when inspected, then it contains **only** files under
  `spec/` — zero production-code changes.

## Implementation Notes

Process/communication story — primary deliverable lives in Jira, not the repo.
If a repo touch is needed (e.g. recording the Jira comment permalink in the
comparison doc), it stays inside `spec/read-column-default-values/`.

- Jira comment target: ENG-91318; epic: ENG-85256.
- Link targets: `spec/read-column-default-values/read-column-default-values-comparison.md`,
  `-current-path.md`, `-odata-probe.md`, `-e2e-scenario.md`,
  `spec/adr/adr-read-column-default-values.md` (Status: Accepted).

Key file: n/a (Jira); optional permalink note in
`spec/read-column-default-values/read-column-default-values-comparison.md`
Pattern to follow: ADR Implementation Plan, story A5 row

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| None | Process story — no code, no tests. Verification = Jira comment + epic surfacing exist and AC links resolve | — |

## Definition of Done

- [ ] ENG-91318 comment posted with decision + evidence links
- [ ] Epic ENG-85256 surfacing done; Phase B trigger status stated
- [ ] OQ-01 status confirmed recorded in the comparison doc before closure
- [ ] Any repo change limited to `spec/**` (SM-01 Phase A counter)
- [ ] Story status updated in `spec/sprint-status.yaml`

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing: n/a (process story)
- Notes:
