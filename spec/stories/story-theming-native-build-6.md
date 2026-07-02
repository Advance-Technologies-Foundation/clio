# Story 6: Docs — `build-theme` help / GitHub docs / Commands.md / WikiAnchors

**Feature**: theming-native-build (ENG-90636, epic ENG-26797 — Theming with AI, clio dev flow; native-build + bundled-template continuation)
**FR coverage**: D6 (docs/toggle interaction), Implementation Plan "Files to create/modify" docs targets
**PRD**: design substance in the approved migration plan ([adr-theming-native-build.md](../adr/adr-theming-native-build.md) §Context)
**ADR**: [adr-theming-native-build.md](../adr/adr-theming-native-build.md) — **Accepted**; refinements R-01…R-18 are authoritative and supersede the pre-review body
**Test plan**: [tp-theming-native-build.md](../test-plans/tp-theming-native-build.md)
**Status**: ready-for-dev
**Size**: M (half day)

> **Depends on Story 4** (the `build-theme` surface must exist to document). Docs-only. Write the CLI
> `-H` help, the GitHub command doc, the `Commands.md` index+section, and the `Wiki/WikiAnchors.txt` anchor for
> `build-theme`. **Feature-toggle docs-omission interaction (D6):** while `[FeatureToggle("theming")]` is
> off, the verb is omitted from **generated** public docs (`Commands.md`/help/wiki use a deterministic export
> baseline) — so the authored docs are committed here but **land publicly when the toggle flips on at go-live
> (Story 8)**. Author them now so they ship the moment the feature un-darkens.

---

## As a

clio user / GitHub reader / AI agent looking up the `build-theme` command

## I want

complete, accurate CLI help and GitHub docs for `build-theme` — flags, defaults, output modes, examples, and
the toggle note

## So that

once the feature ships (toggle on), the command is fully documented across all three surfaces with flags and
behavior aligned to the Story 4 source

---

## Acceptance Criteria

- [ ] **AC-01 (CLI `-H` help)** — Given `clio/help/en/build-theme.txt`, when written, then it documents every
  flag (kebab-case) from the Story 4 source: `--primary` (required), `--secondary`/`--accent`/`--success`/
  `--error` (optional, derived/defaulted), `--css-class-name` (required, pattern + ≤100), `--heading-font`/
  `--body-font`/`--font-weights` (optional, Montserrat default ⇒ no `@import`), `--id`/`--caption`
  (optional, `theme.json`), `--output` (dir ⇒ workspace `theme.css`+`theme.json`, omitted ⇒ stdout), and
  the mutually-exclusive optional `--version`/`--environment-name` (neither ⇒ highest bundled) —
  with at least one usage example. (Implementation Plan.)
- [ ] **AC-02 (GitHub docs)** — Given `clio/docs/commands/build-theme.md`, when written, then it covers purpose
  (native C# theme math + bundled template → `theme.css`, composes with both flows as a pure pipe — D1), the full
  flag table, the two output modes (stdout vs workspace dir + `theme.json`), the bundled version-pinned template
  source (`tpl/themes/{version}/`), version resolution (`--version`/`--environment-name`, neither ⇒
  highest bundled), and worked examples for both the workspace/dev and no-code/server flows. (Implementation
  Plan, D1.)
- [ ] **AC-03 (Commands.md index + section)** — Given `clio/Commands.md`, when updated, then it gains a
  `build-theme` index entry **and** a command section, aligned with the help/docs (canonical verb name, no
  aliases). (Implementation Plan.)
- [ ] **AC-04 (WikiAnchors)** — Given `clio/Wiki/WikiAnchors.txt`, when updated, then it gains the
  `build-theme` anchor. (Implementation Plan.)
- [ ] **AC-05 (toggle / docs-omission note — D6)** — Given the docs, when reviewed, then they note that
  `build-theme` is **feature-toggled** (`clio experimental --name theming --enable`) and **dark until the
  surface is complete and go-live is approved** (Story 8); and the team understands the **generated** public docs
  (`Commands.md`/help/wiki export baseline) **omit** the gated verb until the toggle flips on (Story 8) — so
  these committed docs surface publicly at go-live, not before. (D6.)
- [ ] **AC-06 (alignment)** — Given the docs, when cross-checked against `BuildThemeCommand`/`BuildThemeTool`
  (Story 4), then argument lists, defaults, required flags, examples, and notes match the current source
  behavior exactly (no drift); the MCP tool's `build-theme` row in `McpCapabilityMap` (added in Story 5) and the
  CLI docs are consistent. (Command documentation maintenance policy.)
- [ ] **AC-ERR** — Given the docs, when reviewed, then the error/exit behavior is documented: invalid hex /
  empty required flag → `Error: …` + non-zero exit (CLI); a too-old/invalid `--version` (below the
  lowest bundled template) → graceful error (`success:false` on MCP).

## Implementation Notes

Docs-only. **Use the `document-command` skill.** If a `ReadmeChecker`-style gate forces per-command docs to
co-land with the verb (Story 4), then Story 4 carries `help/en/build-theme.txt` + `docs/commands/build-theme.md`
and this story reduces to `Commands.md` + `WikiAnchors.txt` + cross-surface review — note that outcome in the PR.

**Key files (create): `clio/help/en/build-theme.txt`, `clio/docs/commands/build-theme.md`** (ADR Implementation
Plan) — CLI `-H` help + GitHub docs. **Key files (modify): `clio/Commands.md`** (index + section),
`clio/Wiki/WikiAnchors.txt`** (anchor).

Documentation targets per the AGENTS.md command-documentation policy: `clio/help/en/<verb>.txt`,
`clio/docs/commands/<verb>.md`, `clio/Commands.md`. The `McpCapabilityMap` `build-theme` row is owned by Story 5
(guidance swap) — keep this story's CLI docs consistent with it.

Pattern to follow: the sibling `story-theming-server-flow-6` (docs-only, canonical verb names, align flags to
source, FR-10 limits / behavior notes); existing `docs/commands/*.md` + `help/en/*.txt` for `list-themes` /
`create-theme` as the theming-doc shape.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Docs review (no unit test) | All four surfaces present + aligned with the Story 4 source; toggle/docs-omission note present; canonical verb name (no aliases); `McpCapabilityMap` `build-theme` row (Story 5) consistent | `clio/help/en/build-theme.txt`, `clio/docs/commands/build-theme.md`, `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` |
| Unit (if a ReadmeChecker gate exists) `[Category("Unit")]` | The repo's docs-completeness/ReadmeChecker test passes for `build-theme` (help + Commands.md entry present) | the existing ReadmeChecker test |

- No new behavior is added — this is a documentation pass. If the repo has a ReadmeChecker/docs-completeness
  unit test, it must pass for the new verb; otherwise this story is a doc review with no new test.
- `[Category("Unit")]` (never `[Category("UnitTests")]`) for any ReadmeChecker assertion.

## Definition of Done

- [ ] `clio/help/en/build-theme.txt` created — all kebab-case flags + at least one example
- [ ] `clio/docs/commands/build-theme.md` created — purpose, flag table, output modes, bundled template + version resolution, both-flow examples
- [ ] `clio/Commands.md` index + section added for `build-theme`
- [ ] `clio/Wiki/WikiAnchors.txt` anchor added for `build-theme`
- [ ] Toggle / docs-omission interaction noted (D6): feature-toggled + dark until the surface is complete and go-live (Story 8); generated public docs omit the gated verb until the toggle flips on (Story 8)
- [ ] Docs aligned with the Story 4 `BuildThemeCommand`/`BuildThemeTool` source (flags/defaults/examples/notes); consistent with the Story 5 `McpCapabilityMap` row
- [ ] "docs reviewed" outcome stated per the AGENTS.md command-documentation policy
- [ ] If a ReadmeChecker gate exists, it passes for `build-theme` (and note whether per-command docs co-landed with Story 4)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Docs reviewed:
- Notes:
