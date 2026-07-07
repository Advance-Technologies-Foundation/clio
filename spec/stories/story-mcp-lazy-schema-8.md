# Story 8: Security — split clio-run safe vs clio-run-destructive

**Feature**: mcp-lazy-schema
**FR coverage**: ADR "Security consequence" (auto-approve aggregation), Open Decision 3
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: HIGH (security) — wrong split lets a host auto-approve every destructive command
**Blocked by**: story-mcp-lazy-schema-4 (clio-run), story-mcp-lazy-schema-0 (split decision)

---

## As a

clio MCP host operator who may "always allow" a tool

## I want

`clio-run` to NOT aggregate destructive commands behind one auto-approvable surface — destructive commands routed to a distinct, never-auto-approved tool

## So that

allowing the safe executor does not silently grant `delete-entity-schema`, `application-delete`, etc. without host confirmation (per-tool `Destructive=true` visibility is otherwise lost)

---

## Acceptance Criteria

- [ ] **AC-01** — Given Story 0 chose the split, when implemented, then non-read-only-but-non-destructive commands run via `clio-run` and destructive commands run via a distinct surface (`clio-run-destructive`), per the Story 0 decision.
- [ ] **AC-02** — Given `clio-run`, when registered, then it is NEVER `ReadOnly`/auto-approve (host still prompts), and `clio-run-destructive` is marked `Destructive=true`.
- [ ] **AC-03** — Given a destructive command (e.g. `delete-entity-schema`, `application-delete`), when invoked through `clio-run` (safe), then it is rejected/redirected to `clio-run-destructive` — destructive commands cannot execute via the safe surface.
- [ ] **AC-04** — Given the destructive-command set, when defined, then it derives from each command's existing `Destructive=true` metadata (single source — not a hand-divergent list).
- [ ] **AC-05** — Given read-only operations, when classified, then they stay in the flat core (granular auto-approve preserved), NOT routed through either clio-run surface.
- [ ] **AC-ERR** — Given a command of unknown destructiveness (metadata missing), when classified, then it fails closed to the destructive surface (safe default), and a test guards the gap.

## Implementation Notes

Key files:
- `ClioRunTool.cs` (Story 4) + new `ClioRunDestructiveTool.cs` (`[McpServerToolType]`, gated by Story 1 feature-key).
- Destructiveness source: existing per-tool `Destructive`/`ReadOnly` flags on tool classes — reuse, do not re-declare.
- `ICommandOptionsRegistry` (Story 3) — extend with a destructive classification, or a sibling classifier service.

Pattern: fail-closed (project-context.md feature-toggle "fail closed" philosophy applied to safety classification). Single source for destructiveness.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | safe surface rejects destructive commands; destructive set derives from metadata; unknown→destructive (fail closed); read-only excluded from both; clio-run never ReadOnly | `clio.tests/Command/McpServer/ClioRunSecuritySplitTests.cs` |
| Integration `[Category("Integration")]` | destructive command executes only via destructive surface | `clio.tests/Command/McpServer/ClioRunDestructiveExecutionTests.cs` |
| E2E `[Category("E2E")]` | host sees clio-run-destructive as Destructive=true / not auto-approved (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings; both tools gated by Story 1 feature-key
- [ ] `clio-run` never ReadOnly; destructive surface marked Destructive (asserted)
- [ ] Destructive set derives from single metadata source (no divergent list)
- [ ] Fail-closed for unknown destructiveness (test-guarded)
- [ ] Security review sign-off recorded (per AGENTS.md parallel-agent code review — security lens)
- [ ] MCP e2e added (mandatory) — flagged NOT in CI
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
