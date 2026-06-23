# Story 6: Docs + MCP capability map

**Feature**: theming-server-flow (ENG-91387 — Theming with AI, Toolkit / no-code server flow, Contour B)
**FR coverage**: FR-16
**PRD**: [prd-theming-server-flow.md](../prd/prd-theming-server-flow.md)
**ADR**: [adr-theming-server-flow.md](../adr/adr-theming-server-flow.md)
**Status**: ready-for-dev
**Size**: M (half day)

> **Depends on Stories 2, 3, 4, 5** (the three verbs, their MCP tools, and the guidance edit must exist so the
> docs and capability map describe the shipped surface). CLI `-H` help, GitHub command docs, the `Commands.md`
> index, wiki anchors, and the MCP capability map for all three verbs and six tool variants. Use the
> `document-command` skill.

---

## As a

developer or AI agent discovering the theme write commands from the CLI help, GitHub docs, or the MCP capability map

## I want

`create-theme` / `update-theme` / `delete-theme` documented in every required surface, and the six MCP tool variants
listed in the capability map

## So that

the new verbs are discoverable and accurately described (flags, defaults, required fields, error behavior) wherever a
user or agent looks

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio create-theme -H` / `update-theme -H` / `delete-theme -H`, when run, then each prints
  CLI help from `help/en/{create,update,delete}-theme.txt` listing every option with its kebab-case long-name,
  required/optional state, defaults (create `--id` → auto-UUID; create `--package-name` omitted → CurrentPackageId),
  and the `--css-content` xor `--css-content-file` mutual exclusion. (FR-16, FR-04, FR-05, FR-08.)
- [ ] **AC-02** — Given `docs/commands/{create,update,delete}-theme.md`, when read, then each documents the verb,
  all flags, the FR-10 limits (id/caption/cssClassName regex+length, cssContent ≤1 MiB, empty-ok-null-not), the
  full-overwrite semantics of update (no package parameter), the not-idempotent semantics of delete, error
  behavior (`Error: …` + non-zero exit on invalid input or server `success:false`), and at least one example.
  (FR-16, FR-06, FR-07, FR-09, FR-10.)
- [ ] **AC-03** — Given `clio/Commands.md`, when read, then it has index + section entries for all three verbs
  (canonical names, no aliases — none defined), grouped consistently with the shipped `list-themes` /
  `clear-themes-cache` theme commands. (FR-16.)
- [ ] **AC-04** — Given `clio/Wiki/WikiAnchors.txt`, when read, then it has anchors for `create-theme`,
  `update-theme`, and `delete-theme`. (FR-16.)
- [ ] **AC-05** — Given `docs/McpCapabilityMap.md`, when read, then it lists all six tool variants
  (`{create,update,delete}-theme-by-{environment,credentials}`) with their FR-12 safety flags, notes the
  create tool returns structured `{ success, id, error? }` while update/delete return the log envelope, and notes
  the updated `docs://mcp/guides/theming` resource now covers the server flow. (FR-16, FR-12.)
- [ ] **AC-06** — Given the docs, when reviewed against the shipped source, then argument lists, defaults,
  required flags, examples, and notes are aligned with the actual command behavior (canonical verb names from the
  `[Verb(...)]` attributes), and a `ReadmeChecker`-style gate (if present) passes for the three new verbs. (FR-16,
  AGENTS.md doc maintenance policy.)
- [ ] **AC-ERR** — Given the docs describe error behavior, when read, then each verb's doc states that invalid
  input or a server `success:false` produces `Error: {message}` + a non-zero exit and that success exits 0
  (consistent with AC-ERR). No doc references a removed prototype `new-theme` command.

## Implementation Notes

Pure documentation — no production code. **Use the `document-command` skill** (`$document-command`) per AGENTS.md.

**Key files (create):**
- `clio/help/en/create-theme.txt`, `clio/help/en/update-theme.txt`, `clio/help/en/delete-theme.txt` — CLI `-H`
  help. Mirror the shipped `clio/help/en/list-themes.txt` / `clear-themes-cache.txt` shape.
- `clio/docs/commands/create-theme.md`, `clio/docs/commands/update-theme.md`,
  `clio/docs/commands/delete-theme.md` — detailed GitHub docs. Mirror `docs/commands/list-themes.md`.

**Key files (modify):**
- `clio/Commands.md` — add `create-theme` / `update-theme` / `delete-theme` index + section entries (next to the
  shipped theme commands).
- `clio/Wiki/WikiAnchors.txt` — add anchors for the three verbs.
- `docs/McpCapabilityMap.md` — add the six tool variants with their FR-12 safety flags + the create
  structured-result note; note the updated `docs://mcp/guides/theming` resource (the guidance flip from Story 5).

Resolve aliases to the canonical verb name from `[Verb("create-theme")]` etc. (none have aliases) and use the
canonical name in filenames. Keep argument lists, defaults, required flags, examples, and notes aligned with the
Story 2/3 source behavior. If docs are still accurate after review for any surface, state "docs reviewed, no update
required" for that surface in the PR.

Pattern to follow: the shipped `list-themes` / `clear-themes-cache` docs set (help txt + `docs/commands/*.md` +
`Commands.md` + `WikiAnchors.txt` entries + `McpCapabilityMap.md` rows) delivered under ENG-90636.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | if a `ReadmeChecker` / docs-presence test exists, it passes for `create-theme` / `update-theme` / `delete-theme` (help txt + docs/commands present and referenced) | existing docs-presence test (e.g. `clio.tests/.../ReadmeChecker*`) |
| Review (no automated test) | manual review that flags/defaults/examples match the Story 2/3 source; capability-map flags match FR-12; no `new-theme` references | n/a (doc review in PR) |

- This story is docs-only; most coverage is the existing docs-presence gate plus PR review. If a `ReadmeChecker`
  gate forces per-command docs to co-land with the verb, those docs may instead ship inside Stories 2/3 — in that
  case this story reduces to `Commands.md` / `WikiAnchors.txt` / `McpCapabilityMap.md` and the cross-surface review.
- `[Category("Unit")]` for any docs-presence test (never `[Category("UnitTests")]`); naming
  `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.

## Definition of Done

- [ ] `help/en/{create,update,delete}-theme.txt` created (CLI `-H`)
- [ ] `docs/commands/{create,update,delete}-theme.md` created (GitHub docs)
- [ ] `Commands.md` index + section entries added for all three verbs
- [ ] `Wiki/WikiAnchors.txt` anchors added for all three verbs
- [ ] `docs/McpCapabilityMap.md` lists the six tool variants with FR-12 safety flags + the create structured-result note + the updated `docs://mcp/guides/theming` resource
- [ ] Docs aligned with Story 2/3 source behavior (canonical verb names; defaults; FR-10 limits; full-overwrite / not-idempotent semantics; error behavior); no `new-theme` references
- [ ] `ReadmeChecker`-style docs-presence gate (if present) passes for the three verbs
- [ ] Targeted tests run: `dotnet test --filter "Category=Unit&Module=Command"` (docs-presence gate, if any)
- [ ] PR description references this story file (and states "docs reviewed" outcome per AGENTS.md doc policy)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
