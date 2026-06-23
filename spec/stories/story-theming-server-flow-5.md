# Story 5: Guidance — flip "No-code / server flow" to available

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-15
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

> **Depends on Story 4** (the six `*-theme-by-environment` tool names the guidance section points an agent at).
> An **edit**, not a rewrite, of the shipped `ThemingGuidanceResource.Guide`: flip the "No-code / server flow
> — not yet available in clio" line to available, add a "No-code / server flow" body section, and route to it
> from "Which flow". Keep the shared sections and the single-source-of-truth token-catalog pointer untouched
> (CM-03). No `GuidanceCatalog` change — the `["theming"]` entry already resolves to this guide.

---

## As a

AI coding agent choosing between the workspace/dev flow and the no-code/server flow

## I want

`get-guidance theming` to describe the now-available server flow (create / update / delete on an environment) and
route me to it from "Which flow"

## So that

a no-code agent holding only a registered environment + credentials discovers and follows the server flow without
a workspace, while clio never restates the design-token catalog

---

## Acceptance Criteria

- [ ] **AC-01** — Given `get-guidance theming`, when it resolves, then `Success=true` and the article URI is
  unchanged (`docs://mcp/guides/theming`); no `GuidanceCatalog` change is required (the `["theming"]` entry
  already resolves to `ThemingGuidanceResource.Guide`). (FR-15, AC-11, D6.)
- [ ] **AC-02** — Given the "Which flow" section, when read, then the line
  `- No-code / server flow — not yet available in clio.` is **replaced** with a line that describes the
  now-available server flow and routes to a new body section (e.g. "use it when you have only a registered
  environment and credentials (no workspace/package) — see 'No-code / server flow'"). (FR-15, AC-11, D6.1.)
- [ ] **AC-03** — Given a new "No-code / server flow" body section after the existing "Workspace / dev flow"
  section, when read, then it covers: prerequisites (a registered environment + `CanCustomizeBranding` license +
  `CanManageThemes` operation); create with `create-theme-by-environment` (css-class-name + inline
  cssContent required; `--caption` optional → derived from css-class-name when omitted; `--id` optional → auto-generated and returned; `--package-name` optional → omitted means the
  `CurrentPackageId` system setting); restyle with `update-theme-by-environment` (full overwrite by id; no package
  parameter — cannot re-home); delete with `delete-theme-by-environment` (by id; **not idempotent** — deleting an
  unknown id is an error); confirm with `list-themes-by-environment`. (FR-15, AC-11, D6.2.)
- [ ] **AC-04** — Given the delete step, when read, then it cross-references the existing default-theme caveat
  ("If you delete the theme that is currently the default…") already in the "Get / set the default theme" section
  — it does not duplicate that caveat. (D6.2.)
- [ ] **AC-05** — Given the shared sections, when compared to the shipped guide, then "Source of truth —
  @creatio-devkit/theming", "List themes", and "Get / set the default theme" are **unchanged**, and the new
  section does **not** restate the `--crt-*` token catalog or authoring rules — it stays a thin pointer (CM-03 /
  single source of truth, per ENG-90636 C1). (FR-15, AC-11, CM-03.)
- [ ] **AC-ERR** — Given the existing `get-guidance theming` discovery test, when run after the edit, then it
  stays green (resolution unbroken — RR-02) and the extended assertions confirm the server-flow section is
  present and the token catalog is not restated.

## Implementation Notes

A surgical **edit** of the shipped resource — do not rewrite the guide (RR-02: the guidance-resolution and
discovery tests are shared with ENG-90636 and must stay green).

**Key file (modify): `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs`** (ADR D6, FR-15)
1. Under **"Which flow"**: replace `- No-code / server flow — not yet available in clio.` with a line that
   describes the now-available server flow and routes to the new body section.
2. Add a **"No-code / server flow"** body section after the existing "Workspace / dev flow" section, with the
   five bullets in AC-03 (prerequisites; create / restyle / delete via the `*-theme-by-environment` tools; confirm
   with `list-themes-by-environment`) and the cross-reference to the default-theme caveat (AC-04).
3. Keep **"Source of truth — @creatio-devkit/theming"**, **"List themes"**, and **"Get / set the default theme"**
   unchanged. **Do not restate** the token catalog or authoring rules (CM-03).

**No `GuidanceCatalog.cs` change** — the `["theming"]` entry already exists and resolves to the same
`ThemingGuidanceResource.Guide` (D6).

Review the rest of the MCP surface per AGENTS.md: this story only edits a Resource; the tool descriptions
(Story 4) and the capability map (Story 6) are the other `theming` MCP touch-points. State "MCP reviewed" in the
PR.

Pattern to follow: the shipped `ThemingGuidanceResource.Guide` structure (the "Which flow" + "Workspace / dev
flow" + shared sections it already carries from ENG-90636); the sibling Story 4
(`story-theming-clio-devflow-4.md`) that introduced this resource and its discovery test.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `get-guidance theming` resolves (`Success=true`, URI `docs://mcp/guides/theming`); the server-flow section is present and names `create-theme-by-environment` / `update-theme-by-environment` / `delete-theme-by-environment` / `list-themes-by-environment`; "Which flow" routes to it | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) |
| Unit `[Category("Unit")]` | the token catalog is **not** restated (no `--crt-*` token names/values embedded) — guards CM-03; the shared "Source of truth" / "List themes" / "Get / set the default theme" sections survive | `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` (extend) |
| E2E `[Category("E2E")]` (manual, NOT in CI) | `get-guidance theming` discovery against the real `clio mcp-server` returns the updated text incl. the server-flow section | `clio.mcp.e2e/` (extend the existing theming discovery test) |

- MCP work is incomplete without the `clio.mcp.e2e` discovery assertion (AGENTS.md MCP test requirement). Flag:
  MCP E2E is NOT in CI — manual only.
- `[Category("Unit")]` (never `[Category("UnitTests")]`); naming `MethodName_ShouldBehavior_WhenCondition`
  (e.g. `GetGuidance_ShouldDescribeServerFlow_WhenTopicIsTheming`,
  `GetGuidance_ShouldNotRestateTokenCatalog_WhenTopicIsTheming`).
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] "Which flow" no longer says "not yet available"; routes to the new "No-code / server flow" section (D6.1)
- [ ] Server-flow body section added (prerequisites + create/restyle/delete via `*-theme-by-environment` + confirm with `list-themes-by-environment`; default-theme caveat cross-referenced) (D6.2)
- [ ] Shared sections unchanged; token catalog **not** restated (CM-03)
- [ ] No `GuidanceCatalog` change (D6)
- [ ] `GuidanceGetToolTests` extended; existing discovery test stays green (RR-02)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] `clio.mcp.e2e` theming discovery assertion extended (flag: not in CI — manual)
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file (and states "MCP reviewed" per AGENTS.md MCP policy)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- MCP E2E (manual) run:
- Notes:
