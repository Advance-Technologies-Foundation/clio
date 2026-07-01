# Story 7: Decommission `@creatio/theming` + styling-migration (R-01) — scaffold dep removal + token-catalog repoint

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D9, R-01 — full removal of the `@creatio/theming` scaffold dependency + its component-styling use
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: deferred
**Size**: M (half day)

> **DEFERRED — producer-gated (R-01).** This story removes `@creatio/theming` from the scaffold
> **and handles its component-styling use** (the earlier "keep the dep" resolution is superseded). The clio-side
> change is **SMALL** (a 1-line dependency removal + ~15–25 lines of scaffold guidance to repoint the `--crt-*`
> token-catalog reference). But "styling use handled" requires the token catalog to have a **surviving online
> home (CDN/Academy)** + the cross-repo **`@creatio-devkit/common` REMOTE_COMPONENT_STYLING** guide repointed —
> the **same producer/external workstream family as R-12**. Removing the dep *before* the catalog is rehomed
> leaves an **author-time documentation gap**. **GATE:** this story stays `deferred` until (a) the `--crt-*`
> token catalog is rehomed to CDN/Academy (producer workstream) **and** (b) the devkit-guide repoint is
> coordinated. **Depends on Story 5** (guidance swap establishes clio's lean theming pointer).

---

## As a

Creatio developer scaffolding a `ui-project` with clio, and an AI agent styling components in it

## I want

`@creatio/theming` removed from the scaffold and the `--crt-*` token-catalog reference repointed to its
surviving online home

## So that

clio stops shipping a coupling to the retired npm package while component styling still has a token-catalog
source — `npm i` succeeds, `--crt-*`-referencing components still render (platform `:root` primitives), and the
scaffold guidance points at the rehomed catalog

---

## Acceptance Criteria

- [ ] **AC-01 (full removal — R-01)** — Given `clio/tpl/ui-project/package.json`, when edited, then
  `"@creatio/theming": "^0.1000.0"` ([line 41](../../clio/tpl/ui-project/package.json)) is **removed entirely**
  (full removal, not a version bump) — the dependency is gone. (D9, R-01.)
- [ ] **AC-02 (token-catalog repoint — R-01)** — Given the scaffold `clio/tpl/ui-project/AGENTS.md`, when edited,
  then its `--crt-*` token-catalog reference (which today resolves transitively through
  `node_modules/@creatio-devkit/common/AI_GUIDES_INDEX.md` → `@creatio/theming`'s
  `THEMING_DESIGN_TOKENS_AI_GUIDE.md`) is **repointed to the catalog's rehomed CDN/Academy home** — the same
  destination as the future `get-theme-tokens`, per the project decision that the token
  catalog does **not** live in clio (unlike the build-theme template, which is now bundled in clio). (R-01.)
- [ ] **AC-03 (scaffold still builds — R-01)** — Given `new-ui-project` is run, when the scaffold is produced
  and `npm i` runs (Node available), then `package.json` has **no** `@creatio/theming` devDependency and
  `npm i` **succeeds**; the Angular build does not break — the `--crt-*` CSS variables are **platform `:root`
  runtime primitives** shipped by Creatio, not by the npm package. (`clio/tpl/ui-project/src/styles.scss` is
  empty; the scaffold's `.ts` files import only from `@creatio-devkit/common`, never `@creatio/theming` —
  verified.) (R-01; TC-I-04.)
- [ ] **AC-04 (`--crt-*` renders — R-01)** — Given a sample component referencing `--crt-*` tokens in the
  scaffolded project, when compiled/rendered, then it resolves the tokens from the platform `:root` without the
  npm package. If a full Angular render is not runnable in CI, the test asserts `styles.scss` is empty and no
  `.ts` imports `@creatio/theming` (only `@creatio-devkit/common`), deferring the live render to the manual
  runbook (TC-M-05). (R-01; TC-I-05.)
- [ ] **AC-05 (scaffold guidance resolves — R-01)** — Given the scaffolded `AGENTS.md`, when followed, then its
  styling path resolves to a **token-catalog source** (the rehomed CDN/Academy pointer), **not**
  `node_modules/@creatio/theming/THEMING_DESIGN_TOKENS_AI_GUIDE.md`. (R-01; TC-I-06.)
- [ ] **AC-06 (cross-repo flag — R-01, external)** — Given the transitive
  `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide carries the pointer into `@creatio/theming` and clio
  **cannot** edit it, when this story is planned, then the **cross-repo devkit-guide repoint is flagged** as the
  real coupling (coordinate with the devkit team) and recorded as **external** — the gate that keeps this story
  `deferred`. (R-01; TC-M-05.)
- [ ] **AC-ERR (gate documented)** — Given the producer/external dependencies are unmet, when the story is
  picked up, then it must remain `deferred` (do **not** remove the dep before the catalog is rehomed + the
  devkit guide repointed — that leaves the author-time documentation gap); the gate, its owner, and the target
  date are documented in `sprint-status.yaml`. (R-01, R-12 family.)

## Implementation Notes

clio-side **SMALL** — a 1-line dep removal + ~15–25 lines of scaffold guidance. It is **not** a build-pipeline /
SCSS migration (nothing in the scaffold imports the package). The binding cost is **not in clio**: it is the
**cross-repo devkit-guide repoint** + the **token catalog's rehoming to CDN/Academy** (the same out-of-scope
producer workstream as the future `get-theme-tokens`; the build-theme template itself is now bundled in clio).

**Key file (modify): `clio/tpl/ui-project/package.json`** (ADR Implementation Plan, D9, R-01) — remove
`"@creatio/theming": "^0.1000.0"` (line 41) entirely.

**Key file (modify): `clio/tpl/ui-project/AGENTS.md`** (ADR Implementation Plan, D9, R-01) — add an explicit
`--crt-*` token-catalog pointer directing component styling to the catalog's rehomed **CDN/Academy** home
(replacing the transitive `node_modules/@creatio/theming` path).

**Cross-repo (flag, cannot edit from clio):** the `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide must
be repointed at the rehomed catalog. Coordinate with the devkit team; record as external (TC-M-05).

**Verification:** `new-ui-project` scaffolds + `npm i` succeeds without the dep; a sample `--crt-*` component
renders (platform `:root`); the scaffold `AGENTS.md` styling path resolves to a token-catalog source. The
`npm i` checks need a Node toolchain on the runner — if unavailable, demote to the manual runbook (TC-M-05) and
note the gap.

Pattern to follow: the sibling `story-theming-clio-devflow-4` note (which **kept** the dep under the old
resolution — now explicitly **superseded** by R-01: the dep is retired with its styling use migrated);
`story-theming-clio-devflow-5` (verify-then-document scaffold cleanup); the existing `NewUiProjectScaffoldTests`
shape.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Integration `[Category("Integration")]` `[Property("Module","Command")]` | **TC-I-04** `new-ui-project` scaffolds + `npm i` succeeds WITHOUT `@creatio/theming`; `package.json` has no such devDependency; Angular build not broken | `clio.tests/Command/NewUiProjectScaffoldTests.cs` (or the existing scaffold test) |
| Integration `[Category("Integration")]` `[Property("Module","Command")]` | **TC-I-05** `--crt-*`-referencing component renders (tokens from platform `:root`); if no live render in CI → assert `styles.scss` empty + no `.ts` imports `@creatio/theming` (only `@creatio-devkit/common`), defer render to TC-M-05 | `NewUiProjectScaffoldTests.cs` |
| Integration `[Category("Integration")]` `[Property("Module","Command")]` | **TC-I-06** scaffold `AGENTS.md` styling path resolves to the rehomed token-catalog pointer (CDN/Academy), not `node_modules/@creatio/theming/...` | `NewUiProjectScaffoldTests.cs` |
| Manual / cross-repo (NOT unit-verifiable from clio) | **TC-M-05** `@creatio-devkit/common` REMOTE_COMPONENT_STYLING repointed at the rehomed catalog; the catalog has a surviving online home — the **gate** for un-deferring this story | runbook + PR notes |

- The `npm i` scaffold checks need Node on the runner; if unavailable, demote to TC-M-05 and note the gap.
- `[Category("Integration")]` (never `[Category("UnitTests")]`) **AND** `[Property("Module","Command")]`; naming
  `MethodName_ShouldBehavior_WhenCondition` (e.g.
  `NewUiProject_ShouldInstallWithoutThemingPackage_WhenScaffolded`); temp dirs under the OS temp dir, deleted in
  teardown; OS-portable.
- AAA + a `because` on every assertion + `[Description]` on every test.

## Definition of Done

- [ ] **GATE confirmed cleared** before leaving `deferred`: the `--crt-*` token catalog is rehomed to CDN/Academy **and** the cross-repo `@creatio-devkit/common` REMOTE_COMPONENT_STYLING guide is repointed (TC-M-05) — otherwise this story stays `deferred`
- [ ] `"@creatio/theming"` removed entirely from `clio/tpl/ui-project/package.json` (line 41) — full removal (R-01)
- [ ] `clio/tpl/ui-project/AGENTS.md` repoints the `--crt-*` token-catalog reference to the rehomed CDN/Academy home (R-01)
- [ ] `new-ui-project` scaffolds + `npm i` succeeds without the dep; Angular build not broken; `--crt-*` renders from platform `:root` (R-01)
- [ ] Scaffold `AGENTS.md` styling path resolves to a token-catalog source (not `node_modules/@creatio/theming/...`)
- [ ] Cross-repo devkit-guide repoint flagged as external (TC-M-05); the gate + owner + target date documented in `sprint-status.yaml`
- [ ] Integration tests `[Category("Integration")]` — never `[Category("UnitTests")]`; `npm i` checks demoted to TC-M-05 if Node unavailable on the runner
- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005) — scaffold-only change, but run CLIO005 after
- [ ] Targeted run: `dotnet test --filter "Category=Integration&Module=Command"` (scaffold tests)
- [ ] PR description references this story file and states the styling-migration gate outcome

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Gate cleared (catalog rehomed + devkit guide repointed):
- Tests passing:
- Notes:
