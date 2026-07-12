# Story 6: Section / Application Creation Uses Effective Culture

**Feature**: user-profile-language-detection
**FR coverage**: FR-03, FR-04
**AC coverage**: AC-02, AC-06, AC-08
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: none
**ADR resolutions**: NEW-4 (AC-06 parity spans the section/app caption build)

---

## As a

developer / AI agent

## I want

`create-section` and `create-app` (the section caption build) to use the resolved profile culture instead of host locale / hardcoded `en-US`

## So that

generated section/application captions match the connected user's profile language, with `en-US` as the fallback

---

## Acceptance Criteria

- [ ] **AC-02** — Given a resolved profile culture `uk-UA`, when a section (or app via the section path) is created, then the section caption uses `uk-UA` as the effective caption culture (precedence: `--caption-culture` > resolved > `en-US`).
- [ ] **AC-06 (parity, NEW-4)** — Given profile culture `== en-US` (or `Failed` with a usable map), when the section/app caption build runs, then output is byte-identical to pre-change (parity snapshot test for the section/app caption build).
- [ ] **AC-08** — Given the codebase after the change, when grepping `ApplicationSectionCreateCommand.cs`, then no caption-culture value is derived from `CurrentCulture` and no hardcoded `en-US` literal remains except the `DefaultCultureName` fallback; `ResolveLocalizedCaption` (L423) keeps `en-US` precedence but uses the effective culture when present in the map.
- [ ] **AC-M4** — Given `--caption-culture` is supplied or the supplied map already has the key, when resolution fails, then creation proceeds (skip / non-fatal per M-4) rather than aborting.

## Implementation Notes

`create-app` shares the same section caption build as `create-section` (Decision 4) — one change covers both.

Files to modify:
- `clio/Command/ApplicationSectionCreateCommand.cs` — inject `ICurrentUserCultureResolverFactory`; resolve & pass the effective culture into the caption build; `ResolveLocalizedCaption` (L423) keeps `en-US` precedence but uses the effective culture when present in the supplied map. Add `[Option("caption-culture", ...)]` kebab-case (OQ-03). Honor M-4 gating.
- Docs for `create-section` / `create-app` (`help/en`, `docs/commands`, `Commands.md`) — document `--caption-culture` + behavior change (FR-11). Use `document-command`.
- MCP surface for the section/application tool — review/update + e2e (CLAUDE.md MCP policy).

OQ-04: effective culture is the caption key only when present in the supplied map; otherwise `en-US`. Never inject as a new map entry. `NormalizeLocalizationMap` unchanged (FR-05/AC-03).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | section/app caption build uses effective culture (never `CurrentCulture`); `--caption-culture` precedence + skip (M-4); map-has-key non-fatal; `ResolveLocalizedCaption` keeps `en-US` precedence; **AC-06 parity snapshot for the section/app caption build** | `clio.tests/Command/ApplicationSectionCreateCommandTests.cs` (`BaseCommandTests`) |

NSubstitute for the resolver factory; AAA + `because` + `[Description]`. `BaseCommandTests<TOptions>`.
Test naming: `Execute_ShouldUseEffectiveCulture_WhenProfileResolved`, `Execute_ShouldProduceIdenticalCaption_WhenProfileCultureIsEnUs`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `--caption-culture` is kebab-case
- [ ] No `CurrentCulture` / hardcoded `en-US` in `ApplicationSectionCreateCommand.cs` except `DefaultCultureName` fallback (AC-08 grep)
- [ ] `en-US` remains present in localization maps (FR-05/AC-03)
- [ ] AC-06 parity snapshot test for the section/app caption build (NEW-4)
- [ ] M-4 gating honored
- [ ] No MediatR; resolver via `ICurrentUserCultureResolverFactory`
- [ ] Docs updated for `--caption-culture` + behavior change (FR-11); MCP section/app surface reviewed/updated + e2e (mandatory)
- [ ] Unit tests added with `[Category("Unit")]`; `BaseCommandTests<TOptions>`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
