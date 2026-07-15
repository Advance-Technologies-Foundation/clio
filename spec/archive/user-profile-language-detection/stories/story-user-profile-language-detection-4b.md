# Story 4b: Column Manager WRITE Path (READ Paths Stay on Host Locale)

**Feature**: user-profile-language-detection
**FR coverage**: FR-03
**AC coverage**: AC-02, AC-08
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: none
**ADR resolutions**: Mi-3, NEW-1 (column manager `NormalizeTitleLocalizations` callers)

---

## As a

developer / AI agent

## I want

`modify-entity-schema-column` (the column WRITE path) to use the resolved profile culture for new captions, while column READ/display paths keep using host locale

## So that

written column captions match the platform language, but console display formatting for the operator is unchanged (no regression)

---

## Acceptance Criteria

- [ ] **AC-02** — Given a resolved profile culture `uk-UA`, when a column caption is written via `UpdateEntitySchemaCommand` (column manager `SetLocalizableValue`, L318/L327), then the new caption uses `uk-UA` as the effective caption culture (precedence: `--caption-culture` > resolved > `en-US`).
- [ ] **AC-Mi3-READ** — Given the column READ/display paths `GetColumnProperties` (L114) and `GetSchemaProperties` (L176), when columns are listed/displayed, then they continue to use `GetCurrentCultureName()` (host locale) — explicitly asserted by a test — because they format output for the operator's console, not platform data.
- [ ] **AC-08** — Given the codebase after the change, when grepping `RemoteEntitySchemaColumnManager.cs`, then the only surviving `GetCurrentCultureName()` references are L114 and L176 (the pinned READ-path allow-list); the WRITE path uses the effective culture.
- [ ] **AC-M4** — Given `--caption-culture` is supplied or the supplied map already has the key, when resolution fails, then the column write does not abort (skip / non-fatal per M-4).

## Implementation Notes

Files to modify:
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs`:
  - **WRITE path** — `SetLocalizableValue` at L318/L327 (used by `UpdateEntitySchemaCommand` caption set): pass the effective culture (resolver-driven, M-1 precedence). Update the `NormalizeTitleLocalizations` callers at L241, L310 to pass the effective culture (NEW-1).
  - **READ/display paths** — L114 (`GetColumnProperties`) and L176 (`GetSchemaProperties`): leave on `GetCurrentCultureName()` (Mi-3). These are the pinned AC-08 allow-list lines.
- Inject `ICurrentUserCultureResolverFactory` into the column manager (or thread the effective culture in from the caller); follow the same precedence as Story 4a. Honor `--caption-culture`.

AC-08 grep allow-list (pinned, Decision 4): `RemoteEntitySchemaColumnManager.cs:114, 176` may keep `GetCurrentCultureName`. Any other `GetCurrentCultureName`/`CurrentCulture`/hardcoded `en-US` in this file is an AC-08 failure.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | WRITE path (`SetLocalizableValue` at L318/L327) uses the effective culture (never `CurrentCulture`); `--caption-culture` precedence + M-4 skip/non-fatal; `NormalizeTitleLocalizations` callers (L241, L310) pass effective culture | `clio.tests/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit `[Category("Unit")]` | READ paths `GetColumnProperties` (L114) / `GetSchemaProperties` (L176) stay on host locale (`GetCurrentCultureName`) — explicit assertion (Mi-3) | `clio.tests/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManagerTests.cs` |

NSubstitute for the resolver factory; AAA + `because` + `[Description]`.
Test naming: `SetLocalizableValue_ShouldUseEffectiveCulture_WhenProfileResolved`, `GetColumnProperties_ShouldUseHostLocale_WhenReadingColumns`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] WRITE path uses effective culture; READ paths (L114, L176) explicitly retained on host locale with a test (Mi-3)
- [ ] `NormalizeTitleLocalizations` callers in this file (L241, L310) pass the effective culture (NEW-1)
- [ ] AC-08 grep: only L114/L176 keep `GetCurrentCultureName` in this file
- [ ] No MediatR; resolver via `ICurrentUserCultureResolverFactory`
- [ ] `--caption-culture` (where surfaced) is kebab-case; M-4 gating honored
- [ ] Unit tests added with `[Category("Unit")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
