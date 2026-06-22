# Story 7: [CONDITIONAL] Write-Side Const-GUID Validation + Display-Name Resolution (Phase B)

**Feature**: read-column-default-values
**FR coverage**: FR-06 (DRAFT-AC-06)
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: deferred — **GATED**: do not start until story 4 confirms the gap (FR-03 predicate verdict = fail / A-02 broken-write evidence) AND resolves D7. If story 4 closes FR-06 as "not needed", strike this story with evidence. Note: if FR-03 step 6 showed `SaveSchema` mangles plain-GUID lookup defaults, FR-06 escalates Should → Must and this story's scope grows (flagged in story 4).
**Size**: M (half day)
**Phase**: B — conditional implementation
**Depends on**: story-read-column-default-values-4 (gap confirmation + D7); independent of story 6 (can run in parallel once gated open)

---

## As a

AI no-code agent (epic ENG-85256)

## I want

clio to reject a lookup `Const` default whose GUID does not exist in the referenced table — and optionally accept a display name that clio resolves to a GUID — before saving the schema

## So that

I cannot silently persist a broken default, and I can reference lookup records the way humans do (by display value) instead of hunting for GUIDs

---

## Acceptance Criteria

- [ ] **AC-01 (DRAFT-AC-06)** — Given a GUID supplied as a lookup `Const` default
  that does not resolve in the referenced table, when the mutation is executed,
  then clio prints `Error: default value record '{guid}' not found in
  '{ReferenceSchema}'` and exits non-zero, **without saving the schema**.
- [ ] **AC-02 (empty-table edge)** — Given a lookup just created in the same
  session (possibly still empty), when a `Const` default referencing it is
  validated, then validation against the empty table produces the same honest
  AC-01 error, prompting the agent to seed a record first (FR-03 step 2 evidence).
- [ ] **AC-03 (display-name resolution)** — Given a non-GUID value supplied as a
  lookup `Const` default, when the mutation is executed, then clio resolves it
  against the referenced table's display column following the
  GUID-first-then-alias precedent; an unambiguous match resolves to its GUID, and
  an ambiguous match (incl. case-duplicate display names) is rejected with a clear
  error listing the candidates (existing `RequireSingleMatch` behavior).
- [ ] **AC-04 (no regression)** — Given a valid existing GUID, when the mutation is
  executed, then the save succeeds exactly as today; `Settings`/`SystemValue`/
  `Sequence` resolution paths are unchanged.
- [ ] **AC-ERR (TOCTOU honesty)** — Given the validation passed, when docs/XML docs
  are reviewed, then the point-in-time (TOCTOU) caveat is documented: the record
  may be deleted between validation and save — validation reduces but does not
  eliminate broken-default risk.

## Implementation Notes

ADR-chosen placement (alternative B): extend
`clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueSourceResolver.cs`
`Resolve` to handle `Const` on lookup columns — exactly where `Settings`/
`SystemValue` resolution already lives; DI-injected, interface-backed
(`IEntitySchemaDefaultValueSourceResolver`).

- GUID-first-then-alias precedent already implemented there (lines 72-90): a value
  parsing as GUID is treated as GUID (then validated for existence); otherwise
  display-name resolution.
- Existence/display query transport: per story 4's **D7 resolution** (provisional:
  OData data endpoint via `IApplicationClient`; fallback DataService
  `SelectQuery`). If story 6 lands first, reuse
  `ILookupDefaultDisplayValueResolver` for the existence query instead of
  duplicating transport code.
- Static `EntitySchemaDesignerSupport.ValidateDefaultValueConfig` is NOT the
  placement (rejected: static helper cannot take an injected query service without
  breaking the DI policy).
- Error message is a user-facing constant — no hardcoded inline string scattering;
  user-friendly, no stack traces.
- Breaking-change guard: rejection only affects payloads that were already
  semantically broken (nonexistent GUIDs). If story 4 (A4) recorded that real
  callers depend on saving dangling GUIDs, implement the documented bypass decided
  there and update RELEASE.md.

Key file: `clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueSourceResolver.cs`
Pattern to follow: existing `Settings`/`SystemValue` resolution + `RequireSingleMatch` ambiguity rejection in the same file

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Missing GUID rejected with exact error + non-zero exit + no save; empty-table edge; display-name resolved to GUID; ambiguity rejected with candidates; GUID-first precedence over alias; valid GUID passes; non-lookup sources untouched | existing `clio.tests` entity-schema designer fixtures (e.g. `clio.tests/Command/EntitySchemaDesigner/EntitySchemaDefaultValueSourceResolverTests.cs`) |
| Integration `[Category("Integration")]` | n/a — no file-system/DB surface | — |
| E2E `[Category("E2E")]` | Covered by story 8: write rejection for nonexistent GUID + empty-just-created-lookup edge (`clio.mcp.e2e` — NOT in CI, manual run before merge) | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` on every
assertion + `[Description]` on every test; NSubstitute mocks.
Pre-commit filter: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

## Definition of Done

- [ ] Gate evidence linked: story-4 comparison doc confirms the gap (incl. FR-06 Should/Must escalation status)
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] No new CLI flags; existing `default-value-config.value` / `value-source` contract reused
- [ ] All HTTP via `IApplicationClient`; no bare `catch (Exception)`
- [ ] TOCTOU caveat in XML docs (command docs land in story 8)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Existing entity-schema unit tests stay green (SM-01 Phase B counter, part 1)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
