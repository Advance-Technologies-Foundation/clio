# Story 4: Delegate guidance (`theming`) + npm wiring

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: CAP-03 (MCP guidance entry point that delegates to the npm package), CAP-04 (wire `@creatio-devkit/theming` into the workspace)
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review
**Size**: M (half day)

---

## As a

coding agent orchestrating the theme dev flow

## I want

a thin `theming` MCP guidance entry that routes me to `@creatio-devkit/theming` in `node_modules` (the package is a `new-ui-project` devDependency — it is also the design-token catalog used for component styling)

## So that

I can read the authoritative creation/design-token guides and copy the template from the package itself — clio never restates the token catalog (single source of truth, constraint C1)

---

## Acceptance Criteria

- [ ] **AC-01** — Given `GuidanceCatalog`, when `TryGet("theming", out entry)` is called, then it returns `true` and the entry's resource URI is `docs://mcp/guides/theming`.
- [ ] **AC-02** — Given the `theming` guidance text, when read, then it is an **orchestration pointer only**: ensure `@creatio-devkit/theming` installed → read `node_modules/@creatio-devkit/theming/AI_GUIDES_INDEX.md` → creation + design-token guides + `templates/` → copy `theme.{json,css}.tpl` into `Files/themes/<cssClassName>/` and fill placeholders → `push-workspace` / `push-pkg` → `clear-themes-cache`. It does **NOT** restate the design-token catalog or creation-guide content (constraint C1).
- [ ] **AC-03** — Given the guidance references package guides, when inspected, then it anchors on `AI_GUIDES_INDEX.md` (version-agnostic) as the index. It may additionally name the current canonical guides (`THEMING_CREATION_AI_GUIDE.md`, `THEMING_DESIGN_TOKENS_AI_GUIDE.md`) and `templates/` as direct pointers — those names must be revisited if the package renames its guides (R3).
- [ ] **AC-04** — Given the guidance covers the plain (non-ui-project) package case, when read, then it instructs the agent to drop a minimal `package.json` and run `npm i @creatio-devkit/theming` so `node_modules` exists.
- [ ] **AC-05** — Given a freshly scaffolded `new-ui-project`, when `package.json` is inspected, then `@creatio-devkit/theming` is listed in `devDependencies` (version pinned in `package.json`).
- [ ] **AC-06** — Given the scaffolded `AGENTS.md`, when read, then it has **no** bespoke theme section — theme authoring is delivered via the `get-guidance theming` MCP entry; the existing component-styling pointer (→ `@creatio-devkit/common`) already reaches the `@creatio-devkit/theming` design-token catalog transitively (a theme is not a remote module).

## Implementation Notes

CAP-03 and CAP-04 are independent of the R2 open dependency. The guidance only **points to** the deploy + `clear-themes-cache` steps; it does not need the backend body to be final.

Key file (create): `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs`
- Mirror `clio/Command/McpServer/Resources/WorkspaceUiProjectGuidanceResource.cs` (the `ui-project` guidance) — same `Guide` `static` `TextResourceContents` shape, URI `docs://mcp/guides/theming`.
- Text is the ENG-88812 orchestration prompt **only** (ADR D3). Do NOT embed the token catalog, the creation guide, the fonts guide, or the token-usage policy — those live in `@creatio-devkit/theming` (constraint C1, non-goals).
- Anchor on `AI_GUIDES_INDEX.md` as the index; the current canonical guides are `THEMING_*`-prefixed (the older `THEME_CREATION_AI_GUIDE.md` / `DESIGN_TOKENS_AI_GUIDE.md` in dist are stale — R3). Naming the current `THEMING_*` guides as direct pointers is acceptable provided the index reference remains the authority.

Key file (modify): `clio/Command/McpServer/Resources/GuidanceCatalog.cs`
- Add `["theming"] = Create("theming", "<desc>", ThemingGuidanceResource.Guide)` to the entries dictionary (the catalog is the entry point for `get-guidance theming`).

Key file (modify): `clio/tpl/ui-project/package.json`
- Add `@creatio-devkit/theming` to `devDependencies` with the pinned version (`^0.1000.0`).

`clio/tpl/ui-project/AGENTS.md` — **not modified** (per AC-06 and ADR D3): no bespoke theme section. Theme authoring is delivered via the `get-guidance theming` MCP entry; the existing `@creatio-devkit/common` component-styling pointer already reaches the `@creatio-devkit/theming` design-token catalog transitively.

Theme descriptor is `{id, caption, cssClassName}` with **no `code` field** — satisfied by the package's `theme.json.tpl`; clio authors no descriptor template (constraint C5). Do NOT add a `new-theme` command/tool or any `tpl/themes/*.tpl` (non-goals; that is Story 5's removal verification).

Pattern to follow: `WorkspaceUiProjectGuidanceResource.cs` + its `GuidanceCatalog` entry (`["ui-project"]`).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `get-guidance theming` resolves: `Success=true`, article URI = `docs://mcp/guides/theming`, text routes to `@creatio-devkit/theming` (delegate model) and to the `clear-themes-cache` activation step | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` |
| Unit `[Category("Unit")]` | guidance routes token lookups to the package catalog (`THEMING_DESIGN_TOKENS_AI_GUIDE.md`) instead of embedding token names/values — guards C1 | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` |
| E2E `[Category("E2E")]` (manual, NOT in CI) | `get-guidance theming` discovery against a real `clio mcp-server` resolves the entry and returns the pointer text | `clio.mcp.e2e/` |

- MCP work is incomplete without `clio.mcp.e2e` coverage for the `get-guidance theming` discovery (AGENTS.md MCP test requirement).
- `[Category("Unit")]`; naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] `theming` guidance is a **thin pointer** (no token catalog / creation guide restated); references `AI_GUIDES_INDEX.md`
- [ ] Registered in `GuidanceCatalog`; resolvable via `get-guidance theming` at `docs://mcp/guides/theming`
- [ ] `@creatio-devkit/theming` added to `clio/tpl/ui-project/package.json` devDependencies
- [ ] `clio/tpl/ui-project/AGENTS.md` left without a bespoke theme section (per AC-06 / ADR D3)
- [ ] Plain-package case covered in the guidance (minimal `package.json` + `npm i`)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` `get-guidance theming` discovery test added (flag: not in CI — manual)
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- npm version pinned (or placeholder + R3 note):
- MCP E2E (manual) run:
- Notes:
