---
name: bp-test-run
description: Execute a business-process manual test prompt end to end on a real stand — build local clio, wire the local knowledge library, install the local package, run the prompt in an isolated clean-room Claude session, verify the result in the browser, and analyze the executor's transcript for wasted or misordered tool calls. Use when asked to run manual testing on a stand, dogfood the MCP surface and guidance library, or check how efficiently an agent executes a test prompt.
---

# BP manual test run

Run a manual test prompt against a real stand using **locally built** clio, guidance, and package —
then judge two separate things:

1. **Did the functionality work** — design time and runtime, verified in the browser.
2. **Did the agent get there efficiently** — or did the shipped guidance make it flail.

The second question is the reason this skill exists. A green functional result over a transcript
full of redundant calls means the guidance library needs a fix, not a celebration.

## Invocation contract

`/bp-test-run <ENG-KEY-or-URL> [--env <alias>] [--prompt <path>] [--mode bare|isolated] [--skip-install]`

- `<ENG-KEY-or-URL>` — **required**.
- `--env <alias>` — clio environment to test on. Resolution and the persisted personal default are
  described in [references/environment.md](references/environment.md).
- `--prompt <path>` — default `spec/<feature>/<feature>-manual-test-prompt.md`, written by
  `/bp-test-cases`.
- `--mode` — executor isolation. Default: `bare` when an Anthropic API key is available, otherwise
  `isolated`. See *Phase 3*.
- `--skip-install` — reuse what is already on the stand. Only valid when the stand already carries
  the exact local build; the phase-2 verification still runs and still aborts on a mismatch.

## Phase 0 — preflight

Resolve and print, one line each, before doing anything: issue, prompt path, stand alias and URL,
executor mode, local `clio-knowledge` checkout and its HEAD, local package checkout and its HEAD.

Then **ask the user to confirm the stand** and proceed only on an explicit yes. Phase 2 installs a
package onto a shared environment; the invocation is the gesture to run a test, not blanket
permission to write to whichever stand a config file happened to name.

## Phase 1 — local clio and local guidance

Build clio from this working tree, and make the guidance library the executor sees come from the
local `clio-knowledge` checkout rather than the published release. Commands are in
`references/environment.md`.

**Two gates, neither optional.**

*Identity* — a failed update keeps serving the previous generation, silently. Read
`clio info-knowledge --json` and assert `resolvedRevision` equals the local checkout's HEAD. On any
mismatch, stop; never continue with a warning.

*Positive control* — identity is not enough. A Git source has no freshness marker, and when
activation fails the library is deactivated and `get-guidance` returns nothing at all. Call
`get-guidance name=routing` plus one article the feature depends on, and confirm real content comes
back. Skipping this control is the single most expensive mistake available here: a silently empty
library produces a flailing transcript, and phase 5 will attribute that flailing to guidance defects
and argue for rewriting articles the executor never read.

## Phase 2 — local package on the stand

Build and install the local `CrtProcessBuilder` package, then verify the installed version on the
stand matches what was just built. An install command resolves the bundled archive from the **build
output** directory, so a rebundle that was not followed by a rebuild installs the old archive and
every later observation is invalid. Assert, do not assume.

## Phase 3 — clean-room execution

The executor must know nothing but the prompt. This is not a request in the prompt text — memory and
`CLAUDE.md` are in context before any instruction is read — it is a property of how the session is
launched.

- **`bare` mode** — `claude --bare` skips auto-memory and CLAUDE.md auto-discovery along with hooks,
  plugin sync, and attribution. Requires `ANTHROPIC_API_KEY` or `apiKeyHelper`; OAuth and keychain
  are never read in this mode.
- **`isolated` mode** — a scratch directory outside any repository. Its project slug has no memory
  directory, and no `CLAUDE.md` exists above `C:\Projects\`. The user-level `~/.claude/CLAUDE.md`,
  skills, plugins, and hooks still load. Acceptable and constant across runs, but it is *not* a
  clean room.

In both modes: `--strict-mcp-config` with a config containing **only** the clio server. The executor
must not reach Jira — an executor that can read the issue stops testing the prompt. Pass a generated
`--session-id` so the transcript path is known in advance, and `--output-format stream-json` captured
to a file next to the prompt.

**Record the mode in the report.** Efficiency numbers from `bare` and `isolated` runs are not
comparable.

Bound the run. If the executor stops making progress, capture the transcript as it stands and treat
the stall as a result — where it stalled is a finding. Do not relaunch and quietly report the second
attempt; a retried run measures a different prompt than the one on file.

## Phase 4 — browser verification

Verify in the browser what the prompt claims must be observable — the designer view for the
design-time block, the record/task/process log for the runtime block. The executor's own account of
success is evidence of what it believed, not of what happened.

Capture, per case: what was expected, what is actually on screen, PASS or FAIL.

## Phase 5 — transcript analysis

Score the captured transcript against [references/efficiency-rubric.md](references/efficiency-rubric.md).
Classify every finding by where the fix belongs — guidance article, tool description, tool behavior,
or the prompt itself. A finding with no owner is an observation, not a result.

## Phase 6 — report

Write `spec/<feature>/<feature>-manual-test-run-<YYYY-MM-DD>.md`. The date is part of the name on
purpose: runs are compared against each other, so a report must never overwrite the evidence it is
supposed to be measured against.

- run header: issue, stand, executor mode, clio commit, knowledge commit, package version, and the
  positive-control result from phase 1
- **baseline** — the minimum call sequence a well-guided agent would use, per case, stated before the
  numbers. Without it a call count means nothing and cannot be compared across runs
- per case: PASS/FAIL for design time and runtime, with the browser evidence
- efficiency findings, each with its owner and a proposed fix
- prompt defects — anything the executor could not have known; these feed `/bp-test-cases --revise`
- teardown confirmation (phase 7)

Post the summary to Jira with `addCommentToJiraIssue`. **Comments only** — never edit the issue
description. If the Atlassian MCP is not authorized, say the report is on disk and Jira is
unreachable; do not report it as posted.

## Phase 7 — teardown

Knowledge sources are configured globally, so the local wiring outlives the run and applies to every
later clio session on this machine. Restore it at the end of **every** run, successful or not —
`disable-knowledge-source` on the local alias, `enable-knowledge-source` on `creatio-curated`. Both
preserve configuration and caches, so the next run costs nothing extra. Commands and the exact state
to print are in `references/environment.md`.

Leaving the local library enabled silently changes the guidance every unrelated session sees
afterwards. That is a defect of the run, not a leftover detail.

## Guidance fixes go to another repository

Guidance content lives in `Advance-Technologies-Foundation/clio-knowledge`, not in this repository.
A guidance finding becomes a pull request there, with a `libraryVersion` and `sequence` bump — clio
rejects a library whose content changed under a reused sequence. Do not patch article text into
clio.

## Reuse, do not reimplement

- `creatio-testing:manual-test` — the generic Jira-to-browser QA loop. Delegate the browser phase.
- `creatio-development:share-session` — normalized transcript export when the raw stream-json
  capture is unavailable, e.g. an executor run started outside this skill.
