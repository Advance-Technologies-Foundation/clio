# Story 10: Documentation + MCP Capability Map

**Feature**: browser-session-handoff
**FR coverage**: FR-15
**PRD**: [prd-browser-session-handoff.md](../prd/prd-browser-session-handoff.md)
**ADR**: [adr-browser-session-handoff.md](../adr/adr-browser-session-handoff.md)
**Status**: ready-for-dev
**Size**: M (half day — multiple help/doc/capability-map/Wiki targets per AGENTS.md rigor)
**Revised**: 2026-06-10 — `--output-path` is CLI-only (not an MCP param); Mode A docs deferred; added `Wiki/WikiAnchors.txt`

---

## As a

developer (CLI user)

## I want

complete CLI help files, markdown docs, and an updated MCP capability map for the new browser-session commands

## So that

users can discover and use `get-browser-session` and `clear-browser-session` without reading source code (Mode A / `--authenticated` docs are deferred with that feature)

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio get-browser-session -H`, when run, then the help text from `help/en/get-browser-session.txt` is displayed, listing `--output-path`, `--force-refresh`, and `-e/--environment` with descriptions
- [ ] **AC-02** — Given `clio clear-browser-session -H`, when run, then the help text from `help/en/clear-browser-session.txt` is displayed
- [ ] **AC-03** — Given `docs/commands/get-browser-session.md`, when read, then it contains purpose, all flags with defaults, usage examples, and a note that cookie values are never exposed
- [ ] **AC-04** — Given `docs/commands/clear-browser-session.md`, when read, then it contains purpose, flags, and usage examples
- [ ] **AC-05** — Given `clio/Commands.md`, when read, then `get-browser-session` and `clear-browser-session` appear in the command index with one-line descriptions
- [ ] **AC-06** — Given `docs/McpCapabilityMap.md`, when read, then both `get-browser-session` and `clear-browser-session` appear as MCP tool entries with correct safety flags (`ReadOnly`, `Destructive`, `Idempotent`) and parameter lists. `get-browser-session` MCP params are `environment`, `forceRefresh` only — **`outputPath` is CLI-only and must NOT appear** in the MCP param list
- [ ] **AC-07** — Given `Wiki/WikiAnchors.txt`, when read, then anchors for `get-browser-session` and `clear-browser-session` are present
- [ ] **AC-08** *(deferred with Mode A)* — `--authenticated` on `open-web-app` is documented when that follow-up feature ships; out of scope for this iteration

---

## Implementation Notes

**Files to create:**
- `clio/help/en/get-browser-session.txt` — CLI `-H` help; match format of existing `clio/help/en/*.txt` files
- `clio/help/en/clear-browser-session.txt` — CLI `-H` help

**Files to modify:**
- `clio/docs/commands/get-browser-session.md` — detailed GitHub docs; include: description, synopsis, options table (`--output-path`, `--force-refresh`, `-e`), examples (basic, with `--output-path`, with `--force-refresh`), security note (cookies never in stdout)
- `clio/docs/commands/clear-browser-session.md` — detailed GitHub docs
- `clio/Commands.md` — add rows for `get-browser-session` and `clear-browser-session` in the appropriate section (new "Browser Session" section or existing misc section)
- `docs/McpCapabilityMap.md` — add entries:

  | Tool | ReadOnly | Destructive | Idempotent | Parameters |
  |------|----------|-------------|------------|------------|
  | `get-browser-session` | false | false | false | `environment`, `forceRefresh` (**no `outputPath` — CLI-only**) |
  | `clear-browser-session` | false | true | true | `environment` |

- `Wiki/WikiAnchors.txt` — add anchors for both verbs (CLAUDE.md doc target)
- `open-web-app` docs (`--authenticated`) are **deferred** with Mode A (Story 9) — not part of this iteration

**Format reference:** read 2-3 existing `clio/help/en/*.txt` files and `clio/docs/commands/*.md` files to match the established format exactly.

**Note on `ReadmeChecker` coupling:** the inherited `Command_ShouldHave_DescriptionBlock_InReadmeFile` test will fail for `get-browser-session`/`clear-browser-session` until their `Commands.md`/help entries exist — so this story's docs must land in (or before) the same PR as Stories 5/6, or those stories' command tests will fail. Flag this in Stories 5/6 "Depends on".

**Depends on:** Stories 5, 6, 7, 8 (options and tool names must be final before docs are written). Story 9 (Mode A) is deferred — its docs are out of scope.

## Test Requirements

No automated tests for documentation files. Verification is manual:

| Type | What to verify | How |
|------|---------------|-----|
| Manual | `clio get-browser-session -H` shows correct flags | Run compiled binary |
| Manual | `docs/McpCapabilityMap.md` entries match tool implementation | Code review |

## Definition of Done

- [x] `help/en/get-browser-session.txt` and `help/en/clear-browser-session.txt` created (in Stories 5/6 — ReadmeChecker requires them with the verb)
- [x] `docs/commands/get-browser-session.md` and `docs/commands/clear-browser-session.md` created (Stories 5/6)
- [x] `Commands.md` updated with both command index entries (Stories 5/6)
- [x] `docs/McpCapabilityMap.md` updated — new "Browser Session Handoff" domain; `get-browser-session` MCP params are `environment-name`, `force-refresh` only (no `output-path`); counts bumped 60→62 tools, 53→55 with safety metadata; snapshot date refreshed
- [x] `Wiki/WikiAnchors.txt` updated with both verbs (Stories 5/6)
- [x] `open-web-app --authenticated` docs explicitly deferred (Mode A follow-up) — not added
- [x] All flag names in docs match the kebab-case option names
- [ ] PR description references this story file (single PR)

## Dev Agent Record

- Implementation started: 2026-06-10
- Implementation completed: 2026-06-10
- Tests passing: docs verified by the inherited `ReadmeChecker` test on both command fixtures (Stories 5/6); full unit suite 3526 passed / 0 new failures
- Files: `docs/McpCapabilityMap.md` (this story); per-command docs were created with their verbs in Stories 5/6 (`help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, `Wiki/WikiAnchors.txt`).
- Notes: the `BaseCommandTests<T>.Command_ShouldHave_DescriptionBlock_InReadmeFile` gate forced the per-command docs to ship in Stories 5/6, so Story 10 reduced to the MCP capability map + the documented Mode-A deferral. The capability map is a narrative doc (no automated count test); the +2 tool count is consistent with the prior baseline.
