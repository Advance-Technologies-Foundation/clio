# Story 5: Page / Client-unit / Resource / Schema-designer-helper Creation Uses Effective Culture

**Feature**: user-profile-language-detection
**FR coverage**: FR-03, FR-04
**AC coverage**: AC-02, AC-06, AC-08
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: none
**ADR resolutions**: M-4 (gating), NEW-4 (AC-06 parity spans this In path)

---

## As a

developer / AI agent

## I want

page, client-unit schema, resource-string and schema-metadata creation to use the resolved profile culture instead of hardcoded `en-US`

## So that

generated page/client-unit captions and resource strings match the connected user's profile language, with `en-US` as the fallback

---

## Acceptance Criteria

- [ ] **AC-02** — Given a resolved profile culture `uk-UA`, when a page / client-unit schema / resource string / schema metadata is created, then the caption culture is `uk-UA` (precedence: `--caption-culture` > resolved > `en-US`).
- [ ] **AC-06 (parity, NEW-4)** — Given profile culture `== en-US` (or `Failed` with a usable map), when these creators run, then output is byte-identical to pre-change (parity snapshot test for THIS path, not only entity-schema).
- [ ] **AC-08** — Given the codebase after the change, when grepping `PageCreateOptions.cs`, `ClientUnitSchemaCreate.cs`, `ResourceStringHelper.cs`, `SchemaDesignerHelper.cs`, then no hardcoded `en-US` caption literal remains except the `DefaultCultureName` fallback constant.
- [ ] **AC-M4-SKIP** — Given `--caption-culture` is supplied, then resolution is skipped entirely.
- [ ] **AC-M4-NONFATAL** — Given the supplied map already has the key, when resolution fails, then creation proceeds (degrades to `en-US`) rather than aborting.

## Implementation Notes

Effective-culture precedence (computed once per creation): `--caption-culture` > resolved profile > `DefaultCultureName` ("en-US"). Never read `CurrentCulture`.

Files to modify:
- `clio/Command/PageCreateOptions.cs` — replace hardcoded `"en-US"` (L213, L229); thread the effective culture + add `[Option("caption-culture", ...)]` kebab-case (OQ-03); `en-US` fallback.
- `clio/Command/ClientUnitSchemaCreate.cs` — replace hardcoded `"en-US"` (L154, L160) with the effective culture; `en-US` stays as the fallback constant.
- `clio/Command/ResourceStringHelper.cs` — `CreateLocalizableEntry`/`CleanAndMerge` (L71) take a `cultureName` argument; `en-US` fallback.
- `clio/Command/SchemaDesignerHelper.cs` — `ApplySchemaMetadata` (L132, L135) takes a `cultureName` argument; `en-US` fallback.
- Inject `ICurrentUserCultureResolverFactory` where the page / client-unit creation command computes the effective culture; thread it into the helpers. Honor `--caption-culture`. M-4 gating applies.
- Docs for `create-page` / `create` (client-unit) (`help/en`, `docs/commands`, `Commands.md`) — document `--caption-culture` + behavior change (FR-11). Use `document-command`.
- MCP surface for any affected tool (page tool) — review/update + e2e (CLAUDE.md MCP policy).

OQ-04: effective culture is the caption key only when present in the supplied map; otherwise `en-US`. Never inject as a new map entry. `NormalizeLocalizationMap` unchanged (FR-05/AC-03).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | page creation uses effective culture; `--caption-culture` precedence + skip (M-4); map-has-key non-fatal; `en-US` fallback; **AC-06 parity snapshot** | `clio.tests/Command/PageCreateOptionsTests.cs` (or `PageCreateCommandTests` via `BaseCommandTests`) |
| Unit `[Category("Unit")]` | client-unit schema create uses effective culture; `en-US` fallback; AC-08 no hardcoded literal | `clio.tests/Command/ClientUnitSchemaCreateTests.cs` |
| Unit `[Category("Unit")]` | `ResourceStringHelper` honors `cultureName` arg; `en-US` fallback | `clio.tests/Command/ResourceStringHelperTests.cs` |
| Unit `[Category("Unit")]` | `SchemaDesignerHelper.ApplySchemaMetadata` honors `cultureName`; `en-US` fallback | `clio.tests/Command/SchemaDesignerHelperTests.cs` |

NSubstitute for the resolver factory; AAA + `because` + `[Description]`. `BaseCommandTests<TOptions>` for command tests.
Test naming: `Create_ShouldUseEffectiveCulture_WhenProfileResolved`, `Create_ShouldProduceIdenticalPayload_WhenProfileCultureIsEnUs`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `--caption-culture` is kebab-case
- [ ] No hardcoded `en-US` caption literal in the four In files except `DefaultCultureName` fallback (AC-08 grep)
- [ ] `en-US` remains present in localization maps (FR-05/AC-03)
- [ ] AC-06 parity snapshot test for THIS path (NEW-4)
- [ ] M-4 gating honored (skip on override; non-fatal on usable map; hard-abort only when neither)
- [ ] No MediatR; resolver via `ICurrentUserCultureResolverFactory`
- [ ] Docs updated for `--caption-culture` + behavior change (FR-11); MCP page surface reviewed/updated + e2e (mandatory)
- [ ] Unit tests added with `[Category("Unit")]`; `BaseCommandTests<TOptions>` for command tests
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
