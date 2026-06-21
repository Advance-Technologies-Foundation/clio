# Story 5: Prototype removal verification + docs / capability map

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: Constraints C1/C3/C4 closure; ADR D5 (drop the prototype); SPEC non-goals; docs + MCP capability map for the new surface
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review
**Size**: M (half day)

---

## As a

clio maintainer

## I want

to verify no embedded theme prototype content survives and to document the new `clear-themes-cache` command + `theming` guidance across all required surfaces

## So that

the single-source-of-truth invariant holds (constraint C1) and users / agents can discover the new command through help, docs, and the MCP capability map

---

## Acceptance Criteria

- [ ] **AC-01** — Given the repo, when searched, then none of the prototype embed survives: no `clio/tpl/themes/theme.css.tpl`; no embedded `creatio-theme` / `design-tokens` MCP guidance resources; no `DESIGN_TOKENS_AI_GUIDE.md` `EmbeddedResource` / `CopyToOutputDirectory` csproj entries; no `new-theme` stack (command/options/validator, `ThemeCreator`, `IThemeArtifactBuilder`/`ThemeArtifactBuilder`, `ThemeIdentifiers`, `WorkspacePackageProvisioner`, `NewThemeTool`). This branch is fresh off master, so most is expected absent — **verify and remove any residue** (ADR D5).
- [ ] **AC-02** — Given any prototype DI registration was removed, when the build runs, then CLIO005 reports no newly-dead registration (run CLIO005 after removal).
- [ ] **AC-03** — Given `clio clear-themes-cache -H`, when invoked, then `clio/help/en/clear-themes-cache.txt` is shown (verb + alias `flush-themes` + inherited `RemoteCommandOptions`).
- [ ] **AC-04** — Given the GitHub docs, when read, then `clio/docs/commands/clear-themes-cache.md` documents the command (canonical verb name, surgical-vs-full-flush behaviour, native `ThemeService` endpoint + runtime `CanCustomizeBranding`/`CanManageThemes` auth — no `[RequiresPackage]`).
- [ ] **AC-05** — Given `clio/Commands.md`, when read, then `clear-themes-cache` appears in the overview/index; and `clio/Wiki/WikiAnchors.txt` has a `clear-themes-cache` anchor.
- [ ] **AC-06** — Given `docs/McpCapabilityMap.md`, when read, then it lists the new `clear-themes-cache` MCP tool and the `docs://mcp/guides/theming` resource.
- [ ] **AC-07** — Given the docs, when reviewed, then they use the canonical verb `clear-themes-cache` (resolved from `[Verb(..., Aliases=["flush-themes"])]`) in filenames and headings.

## Implementation Notes

This branch is fresh off master, so the prototype (ENG-89624 / ENG-90889) is largely absent — this story is **verify-then-document**, not a big delete. Grep first; remove only residue (ADR D5 list). Run CLIO005 after any removal.

Removal targets to verify absent / remove (ADR D5 / SPEC non-goals):
- `clio/tpl/themes/theme.css.tpl`
- embedded `creatio-theme` + `design-tokens` MCP guidance resources + their csproj `EmbeddedResource` / `CopyToOutputDirectory` entries for `DESIGN_TOKENS_AI_GUIDE.md`
- the `new-theme` stack: command/options/validator, `ThemeCreator`, `IThemeArtifactBuilder` / `ThemeArtifactBuilder`, `ThemeIdentifiers`, `WorkspacePackageProvisioner`, `NewThemeTool`

Docs to create:
- `clio/help/en/clear-themes-cache.txt` — CLI `-H` help (use the `document-command` skill).
- `clio/docs/commands/clear-themes-cache.md` — detailed GitHub docs.

Docs to modify:
- `clio/Commands.md` — add `clear-themes-cache` to the overview/index.
- `clio/Wiki/WikiAnchors.txt` — add the `clear-themes-cache` anchor.
- `docs/McpCapabilityMap.md` — add the `clear-themes-cache` tool + `docs://mcp/guides/theming` resource entries.

Use the canonical verb `clear-themes-cache` (not the `flush-themes` alias) in filenames. There is typically a `ReadmeChecker`-style gate that requires per-command docs to land with the verb — if so, parts of AC-03/AC-04 may be satisfied alongside Story 1; this story still owns `Commands.md`, `WikiAnchors.txt`, and `McpCapabilityMap.md`.

Pattern to follow: existing `clio/docs/commands/clear-redis-db.md` / `clio/help/en/clear-redis-db.txt` for the command-doc shape; existing `docs/McpCapabilityMap.md` entries for tool + guidance-resource format.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `ReadmeChecker`-style doc-presence test passes for `clear-themes-cache` (help + docs exist for the new verb), if that gate exists | existing doc/readme checker test fixture |
| Build/analyzer gate | CLIO005 clean after any prototype removal (no newly-dead DI registration) | build (analyzers as errors) |
| Manual review | grep confirms no prototype embed residue (AC-01); `Commands.md` / `WikiAnchors.txt` / `McpCapabilityMap.md` updated | n/a (documentation acceptance) |

- No new behavior code beyond docs + removal — so unit coverage is mostly the existing doc-presence gate plus the CLIO005 analyzer gate.
- If any test is added: `[Category("Unit")]`; naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.

## Definition of Done

- [ ] Prototype embed verified absent / removed (D5); CLIO005 clean afterwards
- [ ] No `tpl/themes/*.tpl`, no embedded theme guidance, no `new-theme` stack remains
- [ ] `clio/help/en/clear-themes-cache.txt` + `clio/docs/commands/clear-themes-cache.md` created (canonical verb)
- [ ] `clio/Commands.md` + `clio/Wiki/WikiAnchors.txt` updated
- [ ] `docs/McpCapabilityMap.md` updated with the tool + `docs://mcp/guides/theming` resource
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] PR description references this story file and states "docs reviewed / MCP reviewed"
- [ ] `.codex/workspace-diary.md` entry appended

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Prototype residue found / removed:
- CLIO005 status after removal:
- Implementation started:
- Implementation completed:
- Notes:
