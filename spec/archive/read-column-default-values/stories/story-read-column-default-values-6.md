# Story 6: [CONDITIONAL] Readback Enrichment — display-value + record-resolution (Phase B)

**Feature**: read-column-default-values
**FR coverage**: FR-05 (DRAFT-AC-05)
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Jira**: ENG-91318 (epic ENG-85256)
**Status**: deferred — **GATED**: do not start until story 4 confirms the gap (FR-03 predicate verdict = fail) AND resolves D7 + OQ-04 AND OQ-06 is answered. If story 4 closes FR-05 as "not needed", strike this story with evidence.
**Size**: L (full day)
**Phase**: B — conditional implementation
**Depends on**: story-read-column-default-values-4 (gap confirmation + D7 + OQ-04); precondition OQ-06 (display-value semantics: primary display column, culture/localization, `imageLookup` → `SysImage`)

---

## As a

AI no-code agent (epic ENG-85256)

## I want

`get-entity-schema-column-properties` to return, for a lookup column with a `Const` default, the record GUID **and** the referenced record's display value — or an honest `record-resolution` marker when resolution is impossible

## So that

I can confirm my default-value mutation succeeded in a single machine-verifiable call, without issuing ad-hoc follow-up queries

---

## Acceptance Criteria

- [ ] **AC-01 (DRAFT-AC-05)** — Given a lookup column with a `Const` default, when
  `get-entity-schema-column-properties` is called, then `default-value-config`
  returns the record GUID **and** (the referenced record's display value OR a
  `record-resolution` marker): `no-access` when the read on the referenced entity
  is denied at schema level; `not-found-or-no-access` when the lookup query returns
  no row (deleted vs row-level-hidden are indistinguishable — markers must not
  claim more than the query proves).
- [ ] **AC-02 (fail-soft)** — Given any enrichment failure (permission denial,
  empty result, unexpected error), when readback runs, then the command does NOT
  fail: it degrades to GUID + marker (unexpected errors additionally emit a
  `WriteWarning`) — no regression vs today's GUID-only readback (SM-01 counter).
- [ ] **AC-03 (non-lookup untouched)** — Given a column whose default source is
  `Settings`, `SystemValue`, or `Sequence` (or a non-lookup `Const`), when readback
  runs, then `display-value` and `record-resolution` are null/absent and the
  released 8.0.2.47 contract is byte-for-byte unchanged (PRD non-goal).
- [ ] **AC-04 (kebab-case JSON)** — Given the enriched config, when serialized,
  then the new properties are exactly `display-value` and `record-resolution`
  (kebab-case, mirroring the existing model convention); no new CLI flags exist.
- [ ] **AC-ERR** — Given a referenced schema whose display column cannot be
  determined, when readback runs, then enrichment degrades to GUID +
  `not-found-or-no-access` marker with a warning — clio never prints a stack trace
  and never exits non-zero for an enrichment failure.

## Implementation Notes

All from the ADR Phase B plan — enrichment transport per the **D7 resolution from
story 4** (provisional: OData data endpoint
`odata/{ReferenceSchema}({guid})?$select={displayColumn}` via `IApplicationClient`;
fallback: DataService `SelectQuery`). Enrichment default-on vs opt-in per the
**OQ-04 resolution from story 4** (design default: enrichment-on, fail-soft).

Files to create:

- `clio/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolver.cs` —
  `ILookupDefaultDisplayValueResolver` + implementation; never throws for expected
  failures:

```csharp
internal interface ILookupDefaultDisplayValueResolver
{
    LookupDefaultResolution Resolve(string referenceSchemaName, Guid recordId, RemoteCommandOptions options);
}

internal sealed record LookupDefaultResolution(string? DisplayValue, string? RecordResolution);
```

Files to modify:

- `clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueConfig.cs` — add
  `[JsonPropertyName("display-value")] string? DisplayValue` and
  `[JsonPropertyName("record-resolution")] string? RecordResolution` (init-only,
  null for non-lookup sources).
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` — in
  `GetColumnProperties` (line 158): when `source == Const` and
  `column.ReferenceSchema != null`, invoke the resolver and enrich; constructor-
  inject `ILookupDefaultDisplayValueResolver`; `PrintColumnProperties` prints the
  new fields.
- `clio/BindingsModule.cs` — register `ILookupDefaultDisplayValueResolver`
  (DI policy: no `new` for behavior classes; CLIO001-004 clean).

Display-column discovery: referenced schema's `primary-display-column-name` is
already exposed by the designer read path (`EntitySchemaReadModels.cs:45`); resolve
once per referenced schema per command execution (in-memory cache — same pattern as
`EntitySchemaDefaultValueSourceResolver._settingsCache`). Fail-soft rule: catch
**specific** exception types from the `IApplicationClient` path (no bare
`catch (Exception)`); 403 → `no-access`, empty result → `not-found-or-no-access`.

A-04 shortcut: if story 3 found display metadata already present in the designer
DTO, this story shrinks to a pure mapping change (resolver unnecessary) — confirm
against the story-3 evidence before coding.

Key file: `clio/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolver.cs` (new)
Pattern to follow: `EntitySchemaDefaultValueSourceResolver` (DI, caching, GUID-first precedent)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Resolver marker mapping (found / 403 → `no-access` / empty → `not-found-or-no-access` / unexpected error → warn + marker); enrichment only for lookup-`Const`; null fields for `Settings`/`SystemValue`/`Sequence`; per-execution display-column cache | `clio.tests/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolverTests.cs` + existing entity-schema fixtures |
| Integration `[Category("Integration")]` | n/a — no file-system/DB surface | — |
| E2E `[Category("E2E")]` | Covered by story 8 (`clio.mcp.e2e` — NOT in CI, manual run before merge) | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` on every
assertion + `[Description]` on every test; NSubstitute mocks.
Pre-commit filter: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

## Definition of Done

- [ ] Gate evidence linked: story-4 comparison doc confirms the gap; D7, OQ-04, OQ-06 resolutions referenced
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] No new CLI flags; JSON properties kebab-case (`display-value`, `record-resolution`)
- [ ] All HTTP via `IApplicationClient` — no raw `HttpClient`
- [ ] No bare `catch (Exception)`; fail-soft verified by unit tests
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Existing entity-schema unit tests stay green (SM-01 Phase B counter, part 1)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
