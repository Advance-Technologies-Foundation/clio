# Story 0: Reconcile PR #624, fix default profile and clio-run split decisions

**Feature**: mcp-lazy-schema
**FR coverage**: ADR "Relationship to PR #624", "Open decisions" 1/2/3
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: done (resolved 2026-06-19, Alex)
**Size**: S (< 2h) — coordination + written decision, no code
**Risk**: HIGH — **BLOCKER for all implementation stories (1-11)**
**Type**: decision-story / prerequisite

---

## As a

clio maintainer coordinating two competing MCP-surface redesigns

## I want

a written, signed-off resolution of the three blocking open decisions (PR #624 supersede-vs-build-on, default profile opt-in-vs-default, clio-run safe/destructive split) recorded in the ADR

## So that

no implementation story starts on top of an unreconciled, mutually-incompatible design (#624 anyOf vs this ADR's lazy-schema), and the security/migration scope is fixed before code is written

---

## Acceptance Criteria

- [ ] **AC-01** — Given PR #624 (ENG-90312) and this ADR share a Jira and are mutually incompatible, when the decision is recorded, then the ADR "Relationship to PR #624" section states one of {supersede #624 Phase-2 anyOf / build-on #624 Phase-1 dedup / abandon this ADR} with the #624 author's confirmation referenced.
- [ ] **AC-02** — Given #624's status must change, when the decision is "supersede", then #624 is re-labelled (draft→superseded or closed) and the ADR records the link + date of that status change.
- [ ] **AC-03** — Given Open Decision 2 (default profile), when recorded, then the ADR fixes "opt-in until aliases+inventory land" (or the chosen alternative) as a binding constraint that Story 1 and Story 10 must honor.
- [ ] **AC-04** — Given Open Decision 3 (security split), when recorded, then the ADR fixes whether `clio-run` is split into safe/destructive (Story 8 input) and whether `clio-run` may ever be `ReadOnly`/auto-approve.
- [ ] **AC-05** — Given the feature-key for `IFeatureToggleService` gating must be stable, when recorded, then the ADR names the exact feature-key string Story 1 will use.
- [ ] **AC-ERR** — Given the #624 author cannot be reached or disagrees, when this story closes, then the blocker is escalated in writing (not silently defaulted) and Stories 1-11 remain blocked in sprint-status.yaml.

## Implementation Notes

No production code. Output is edits to `spec/adr/adr-mcp-lazy-schema.md` (Status, "Relationship to PR #624", "Open decisions" sections) plus a coordination record.

Key references:
- ADR "Relationship to PR #624" (lines ~114-122) — the supersede proposal.
- ADR "Open decisions" 1/2/3 (lines ~187-194).
- ADR "Security consequence" — feeds AC-04.
- `clio/BindingsModule.cs:632-655` — `McpFeatureToggleFilter.RegisterEnabledPrimitives` is the single registration seam both designs touch (conflict surface).
- PR [#624](https://github.com/Advance-Technologies-Foundation/clio/pull/624).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit | n/a — no code | — |
| Integration | n/a | — |
| E2E | n/a | — |

This is a documents-only decision story; SM is an empty code diff (spec/** only).

## Definition of Done

- [ ] ADR Status moved from "Proposed" to "Accepted" (or feature parked, recorded)
- [ ] Three open decisions (1/2/3) marked RESOLVED with rationale + date in the ADR
- [ ] #624 status reconciled and linked
- [ ] sprint-status.yaml `blocked_by: [story-mcp-lazy-schema-0]` removed from Stories 1-11 once resolved
- [ ] PR/commit description references this story file

## Dev Agent Record

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: n/a (documents-only)
- Notes: Decisions resolved by Alex (one-by-one):
  1. **#624** — left as a **fallback plan**; NOT superseded, NOT built-on. This ADR
     proceeds independently; `clio-run` built from scratch; no #624 coordination.
     (AC-01/02 satisfied via the "fallback" option rather than supersede/build-on.)
  2. **Default profile** — **AMENDED 2026-06-19 → OPT-IN lazy mode** (was
     core-by-default). Default OFF = full flat catalog (unchanged, zero regression);
     consumers opt in to lazy mode for the −97%. Drops the breaking migration:
     Story 10 (aliases) + Story 11 (×74) DEFERRED. Rationale: the executor delivers
     the token-impact goal regardless of default; core-by-default added a
     disproportionate breaking cost for no extra goal value (AC-03 superseded).
  3. **clio-run split** — **YES**, `clio-run` (safe) + `clio-run-destructive`;
     `clio-run` never `ReadOnly`/auto-approve (AC-04).
  4. **Feature-key** = `mcp-lazy-tools` (amended from `mcp-full-tool-catalog`), OFF by
     default ⇒ full flat catalog (unchanged); ON ⇒ lazy mode (core + executors). AC-05.
  ADR Status → Accepted. sprint-status: Stories 1/2/3/6/9 unblocked → ready-for-dev.
