# Story 4a: Entity-schema Creator Write Path + Cultures Array + NormalizeTitleLocalizations Signature

**Feature**: user-profile-language-detection
**FR coverage**: FR-03, FR-04
**AC coverage**: AC-02, AC-06, AC-08
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**ADR**: [adr-user-profile-language-detection.md](../adr/adr-user-profile-language-detection.md)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: story-user-profile-language-detection-1
**Blocks**: none
**ADR resolutions**: M-1, M-2, Mi-2, NEW-1 (EntitySchemaTool/UpdateEntitySchemaCommand callers)

---

## As a

developer / AI agent

## I want

`create-entity` (and the modify-entity-schema path) to use the resolved profile culture as the effective caption culture instead of host `CurrentCulture`/hardcoded `en-US`

## So that

generated object names, captions and schema cultures match the connected Creatio user's profile language, with `en-US` as the fallback

---

## Acceptance Criteria

- [ ] **AC-02** — Given a resolved profile culture `uk-UA`, when an object/entity is created, then all generated names/labels/captions use `uk-UA` as the effective caption culture (precedence: `--caption-culture` > resolved profile > `en-US`).
- [ ] **AC-06 (parity)** — Given profile culture `== en-US` (or a `Failed` resolution with a usable map), when the entity-creation flow runs, then the produced `caption`/`description`/`title-localizations` payloads are byte-identical to pre-change output (snapshot test).
- [ ] **AC-08** — Given the codebase after the change, when grepping `RemoteEntitySchemaCreator.cs` and `EntitySchemaDesignerSupport.cs` (In files), then no caption-culture value is derived from `CultureInfo.CurrentCulture` and no hardcoded `en-US` caption literal remains except the `DefaultCultureName` fallback constant.
- [ ] **AC-Mi2** — Given a resolved effective culture, when the schema is created, then the schema-level `Cultures` array (`RemoteEntitySchemaCreator.cs:536`) contains the effective creation culture (separate assertion from the caption tests).
- [ ] **AC-M2** — Given `NormalizeTitleLocalizations` is called with `effectiveCultureName`, when it picks `effectiveTitle`, then it uses the supplied culture; when `null`, it falls back to `DefaultCultureName` (`en-US`), never `CurrentCulture`.
- [ ] **AC-M4-SKIP** — Given `--caption-culture` is supplied, when the entity is created, then the `GetApplicationInfo` round-trip is skipped entirely.
- [ ] **AC-M4-NONFATAL** — Given the supplied localization map already contains the needed key, when resolution fails, then creation proceeds (degrades to `en-US` per OQ-04) rather than aborting.

## Implementation Notes

Effective-culture precedence (computed once in `Create`): `--caption-culture` > resolved profile > `DefaultCultureName` ("en-US"). Never read `CultureInfo.CurrentCulture` (M-1).

Files to modify:
- `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs` — **add `string? effectiveCultureName = null` to `NormalizeTitleLocalizations`** (definition L173, M-2); replace the L188 `GetCurrentCultureName()` with `effectiveCultureName ?? DefaultCultureName`. Keep `GetCurrentCultureName()` for READ/display helpers. `DefaultCultureName` ("en-US") stays as the documented creation fallback.
- `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs` — inject `ICurrentUserCultureResolverFactory`; compute effective culture once in `Create`; replace L107 `GetCurrentCultureName()` (write path) with the effective culture; pass through `CreateColumn` (`CultureName =`, L264); set `Cultures = [effectiveCulture]` at L536 (Mi-2 — schema-level array, not a caption key); honor `--caption-culture`. Update the two `NormalizeTitleLocalizations` callers at L109, L231 to pass the effective culture.
- `clio/Command/UpdateEntitySchemaCommand.cs` (L171) — **NEW-1**: `NormalizeTitleLocalizations` caller on the modify path; pass the effective culture (otherwise silently keeps `en-US` via the `null` default).
- `clio/Command/McpServer/Tools/EntitySchemaTool.cs` (L78, L462) — **NEW-1**: both `NormalizeTitleLocalizations` calls must pass the effective culture (MCP-maintenance policy mandatory). Use the env-aware resolver path.
- `CreateEntitySchemaCommand` options — add `[Option("caption-culture", ...)]` kebab-case override (OQ-03).
- Docs for `create-entity` / `modify-entity-schema` (`help/en`, `docs/commands`, `Commands.md`) — document `--caption-culture` + the profile-culture behavior change (FR-11). Use `document-command`.
- MCP surface for the entity-schema tool — review/update tool args + e2e (CLAUDE.md MCP policy); use `create-mcp-tool` / `test-mcp-tool`.

OQ-04: the effective culture is the caption key only when present in the supplied map; otherwise fall back to `en-US`. Never inject the effective culture as a new map entry. `NormalizeLocalizationMap` stays unchanged (FR-05/AC-03).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | creator uses effective culture (never `CurrentCulture` — M-1); `--caption-culture` precedence + skips resolution (M-4); map-has-key → non-fatal (M-4/OQ-04); `Cultures` array = effective culture (Mi-2); `en-US` retained in maps (FR-05/AC-03); **AC-06 parity snapshot** for `en-US` | `clio.tests/Command/EntitySchemaDesigner/RemoteEntitySchemaCreatorTests.cs` |
| Unit `[Category("Unit")]` | `NormalizeTitleLocalizations` honors `effectiveCultureName`; `null` → `en-US` (not `CurrentCulture`) | `clio.tests/Command/EntitySchemaDesigner/EntitySchemaDesignerSupportTests.cs` |
| Unit `[Category("Unit")]` | `UpdateEntitySchemaCommand` passes effective culture to `NormalizeTitleLocalizations` (NEW-1) | `clio.tests/Command/UpdateEntitySchemaCommandTests.cs` (`BaseCommandTests`) |
| Unit `[Category("Unit")]` | `EntitySchemaTool` passes effective culture in both calls (NEW-1) | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` |
| E2E `[Category("E2E")]` | entity-schema MCP create/update with effective culture (manual — not in CI) | `clio.mcp.e2e/` (existing entity-schema e2e) |

NSubstitute for the resolver factory; AAA + `because` + `[Description]`. Use `BaseCommandTests<TOptions>` for command tests.
Test naming: `Create_ShouldUseEffectiveCulture_WhenProfileResolved`, `Create_ShouldSkipResolution_WhenCaptionCultureSupplied`, `Create_ShouldProduceIdenticalPayload_WhenProfileCultureIsEnUs`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `--caption-culture` is kebab-case
- [ ] `NormalizeTitleLocalizations` signature changed + ALL its callers updated (M-2 / NEW-1: `RemoteEntitySchemaCreator:109,231`, `UpdateEntitySchemaCommand:171`, `EntitySchemaTool:78,462`)
- [ ] No `CurrentCulture` / hardcoded `en-US` in the In files except `DefaultCultureName` fallback (AC-08 grep)
- [ ] `Cultures` array set to effective culture with a dedicated test (Mi-2)
- [ ] `en-US` remains present in `title-localizations`/`description-localizations` (FR-05/AC-03)
- [ ] AC-06 parity snapshot test locks `en-US` byte-identical output
- [ ] MCP tool/prompts reviewed + e2e updated (mandatory per CLAUDE.md)
- [ ] Docs updated for `--caption-culture` + behavior change (FR-11)
- [ ] Unit tests added with `[Category("Unit")]`; `BaseCommandTests<TOptions>` for command tests
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
