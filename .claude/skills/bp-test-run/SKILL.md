---
name: bp-test-run
description: Debug CrtProcessBuilder, clio, and the clio knowledge library on a real stand, in two modes. Mode agent runs a business-process manual test prompt in an isolated clean-room Claude session against locally built clio, guidance and package, then inspects only the read-back description of what was created and the session transcript. Mode browser runs afterwards against what that left on the stand: opens the processes in the designer, executes them, and verifies the runtime result in the browser. Use when asked to run manual testing on a stand, dogfood the package or MCP surface, find guidance defects, or verify a previous run in the browser.
---

# BP manual test run

**This is a debugging session for three components**, run against a real stand with all three built
locally:

1. **`CrtProcessBuilder`** — schema serialization, what the designer renders, what the platform
   executes.
2. **clio** — the CLI and MCP tool surface: contracts, arguments, validation, error reporting.
3. **The clio knowledge library** — the guidance that steers an agent through the other two.

The prompt is the instrument, not the subject. The output of a run is **defects with reproductions,
each attributed to one of those three** — not a verdict on the prompt.

Two questions produce those defects:

- **Did the functionality work** — design time and runtime, verified in the browser. Failures here
  are usually `CrtProcessBuilder` or clio.
- **Did the agent get there without flailing** — failures here are usually the knowledge library or
  a tool description, and they are invisible to a functional pass. A green result reached through a
  transcript full of redundant calls is a defect report, not a celebration.

## Invocation contract

`/bp-test-run <ENG-KEY-or-URL> --mode <agent|browser> [--env <alias>] [--prompt <path>] [--isolation bare|isolated] [--run <id>] [--skip-install]`

- `<ENG-KEY-or-URL>` — **required**.
- `--mode` — **required**, and the biggest decision here. `agent` runs the tests and reads back what
  was created; `browser` opens what a previous `agent` run left behind and executes it. See *Two
  modes* below.
- `--env <alias>` — clio environment to test on. Resolution and the persisted personal default are
  described in [references/environment.md](references/environment.md).
- `--prompt <path>` — mode `agent` only. Default `spec/<feature>/<feature>-manual-test-prompt.md`,
  written by `/bp-test-cases`.
- `--isolation` — mode `agent` only: how the executor session is launched. Default `bare` when an
  Anthropic API key is available, otherwise `isolated`. See *Phase 3*.
- `--run <id>` — mode `browser` only: which `agent` run to verify. Default: the most recent manifest
  for this issue.
- `--skip-install` — mode `agent` only. Reuse what is already on the stand; the phase-2 verification
  still runs and still aborts if the stand is behind the local build.

## Two modes

The two questions this skill asks are answered by different machinery, at different cost, and one
depends on the other. Running them as one pass makes the cheap half hostage to the expensive half.

**`--mode agent`** — the tests run. A clean-room session executes the prompt against the stand, and
the only thing inspected afterwards is **the description of what was created**, read back through the
tool surface, plus the session transcript. No browser, no process execution. This is the fast,
repeatable half: it answers *did the agent, guided only by the shipped library, build the right
thing, and how directly did it get there*. Defects here are almost always clio or the knowledge
library. Phases 0-3, 4, 5, 6, 7.

**`--mode browser`** — run afterwards, against what the `agent` run left on the stand. The processes
are opened in the designer and executed, and the result is verified in the browser. This is the half
that needs a human-visible surface and real execution: it answers *does the platform actually render
and run what was stored*. Defects here are almost always `CrtProcessBuilder` or the platform.
Phases 0', 4B, 6'.

Two consequences, both load-bearing:

- **The `agent` run must not clean up what it created on the stand.** Its processes are the input to
  the `browser` run. Only the knowledge configuration is restored (phase 7); stand artifacts stay.
- **An `agent` run alone is never a pass for the feature.** It establishes the *Stored* level and
  nothing more. Say so in the report, in those words — a stored-level green read as "it works" is
  exactly the failure the three-level split exists to prevent.

A `browser` run needs a manifest from an `agent` run and refuses to guess without one: nothing else
can tell it which of the processes on a shared stand belong to this test.

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

- **`--isolation bare`** — `claude --bare` skips auto-memory and CLAUDE.md auto-discovery along with hooks,
  plugin sync, and attribution. Requires `ANTHROPIC_API_KEY` or `apiKeyHelper`; OAuth and keychain
  are never read in this mode.
- **`--isolation isolated`** — a scratch directory outside any repository. Its project slug has no memory
  directory, and no `CLAUDE.md` exists above `C:\Projects\`. The user-level `~/.claude/CLAUDE.md`,
  skills, plugins, and hooks still load. Acceptable and constant across runs, but it is *not* a
  clean room.

In both modes: `--strict-mcp-config` with a config containing **only** the clio server. The executor
must not reach Jira — an executor that can read the issue stops testing the prompt. Pass a generated
`--session-id` so the transcript path is known in advance, and `--output-format stream-json` captured
to a file next to the prompt.

**Record the isolation in the report.** Efficiency numbers from `bare` and `isolated` runs are not
comparable.

Bound the run. If the executor stops making progress, capture the transcript as it stands and treat
the stall as a result — where it stalled is a finding. Do not relaunch and quietly report the second
attempt; a retried run measures a different prompt than the one on file.

## Phase 4 — read-back (mode `agent`)

Read every process the run created back through the tool surface and compare it with what the case
declares at the *Stored* level: the expression as written, the reference form, the source kind, the
element and flow structure.

Read it back **yourself**. The executor's own account of success is evidence of what it believed, not
of what is stored — and a stored-level claim is the one most easily reported as working when nothing
a user looks at would show it.

Capture per case: expected form, actual form, PASS or FAIL, and the identity of every process created
(name, UId, package) — that identity is what phase 6 writes into the manifest and what the `browser`
run depends on.

Stop here. Do not open a designer, do not start a process: those belong to the other mode, and doing
them here quietly makes the cheap run as expensive as the full one.

## Phase 4B — designer and runtime verification (mode `browser`)

Load the manifest named by `--run`, or the most recent one for this issue, and confirm the stand it
names is the stand being addressed. A manifest from a different stand is a stop, not a warning.

For each process the manifest lists, and each case that declares those levels:

- **Design time** — open it in the process designer. Check the diagram shape, captions as a human
  reads them, what the element settings show when opened, and what must *not* be there. Record what
  the designer does on save when the case says it complains.
- **Runtime** — start the process with the inputs the case names, then verify the outcome where a
  person would see it: the record, the task, the Activity card, the process log. Which branch ran,
  and which did not.

A case marked *platform behaviour, recognized, not filed* is verified here in one specific way: check
the neighbouring regression the case names, not the known-wrong outcome itself. "The designer cannot
display it" needs no action; "the value is gone after saving" is a defect.

Capture per case and per level: expected, actually observed, PASS or FAIL, with the evidence.

## Phase 5 — transcript analysis (mode `agent`)

Score the captured transcript against [references/efficiency-rubric.md](references/efficiency-rubric.md).
Classify every finding by where the fix belongs — guidance article, tool description, tool behavior,
or the prompt itself. A finding with no owner is an observation, not a result.

## Phase 6 — report and manifest

Both modes write into **one** report per run: `spec/<feature>/<feature>-manual-test-run-<YYYY-MM-DD>.md`.
The date is part of the name on purpose — runs are compared against each other, so a report must never
overwrite the evidence it is supposed to be measured against. A `browser` run **appends to the report
of the run it verifies**; it does not open a competing file, so a case's stored, design-time and
runtime verdicts end up next to each other.

Mode `agent` also writes a **manifest** — the handoff, without which the `browser` run cannot know
which processes on the stand are the ones under test. It records the run id, the stand alias and URL,
the package version, the prompt file and its commit, and for every process created: case, name, UId,
package. Layout is in [references/environment.md](references/environment.md).

**An `agent` report states its own limit in the verdict**, in words: *stored level only; design time
and runtime not verified*. Not a footnote — the verdict line itself. A green stored-level result read
as "the feature works" is the failure the three-level split exists to prevent, and the report is where
it happens.

- run header: issue, stand, mode, isolation, clio commit, knowledge commit, package version, and the
  positive-control result from phase 1
- **baseline** — the minimum call sequence a well-guided agent would use, per case, stated before the
  numbers. Without it a call count means nothing and cannot be compared across runs
- per case: PASS/FAIL **per declared level** (stored / design time / runtime), with the evidence.
  Levels no mode has covered yet are `not verified`, never blank and never assumed
- **defects** — the point of the run. Each one carries: the observed behavior, the minimal
  reproduction, the owning component (`CrtProcessBuilder` / clio / knowledge library), and the
  repository the fix belongs in. A defect without a reproduction is an anecdote and will not survive
  contact with whoever has to fix it
- efficiency findings, each with its owner and a proposed fix
- *Invalidated by the prompt* — cases where the prompt, not the product, was wrong, so the case
  measured nothing. These and only these feed `/bp-test-cases --revise`. Keep the list short and
  honest: moving a real product defect into this section makes it disappear
- teardown confirmation (phase 7), and — for an `agent` run — the manifest path and the exact
  `browser` invocation that continues it

Post the summary to Jira with `addCommentToJiraIssue`. **Comments only** — never edit the issue
description. If the Atlassian MCP is not authorized, say the report is on disk and Jira is
unreachable; do not report it as posted.

## Phase 7 — teardown (mode `agent`)

Knowledge sources are configured globally, so the local wiring outlives the run and applies to every
later clio session on this machine. Restore it at the end of **every** `agent` run, successful or not.
Commands and the exact state to print are in [references/environment.md](references/environment.md).

Leaving the local library in place silently changes the guidance every unrelated session sees
afterwards. That is a defect of the run, not a leftover detail.

**What teardown must not touch: the stand.** The processes the run created are the input to the
`browser` run, and deleting them turns a two-mode workflow into a one-mode one that can never reach
runtime. Clean them up when the `browser` run is done and the report is written, or leave them — a
dev stand accumulating test processes is cheaper than a verification that cannot happen.

Mode `browser` restores nothing: it never wired a library and never installed anything.

## Where each defect goes

Three components, three destinations. Getting this wrong wastes a fix in the wrong repository.

| Owner | Repository | Notes |
|---|---|---|
| `CrtProcessBuilder` | `cli-process-builder` | Package/server-side behavior. A fix reaches a stand only after a rebundle with a raised `-Version` **and** a clio rebuild |
| clio | this repository | Tool contracts, arguments, validation, error text. An MCP surface change pulls in the MCP review policy in `AGENTS.md` |
| Knowledge library | `clio-knowledge` | Article content, one Markdown file per article. Needs a `libraryVersion` + `sequence` bump — clio rejects a library whose content changed under a reused sequence. Never patch article text into clio |

When a defect could belong to two of them — a tool that behaves correctly but is undiscoverable, say
— record both, with the reason. Deciding it silently is how a guidance gap gets filed as a tool bug
and closed as "works as designed".

**A fourth disposition: platform behaviour, recognized, not filed.** Some wrong-looking outcomes are
the Creatio platform's own behaviour, already understood by the team, and filing them against
`CrtProcessBuilder` wastes a round. When the prompt labelled a case that way, carry the label into the
report — and check the neighbouring regression the case names. That check is the whole value of the
label: "the designer cannot display the value" is expected, "the value is gone after saving" is a
regression, and only the second is a defect. A label with no such check performed is not a
disposition, it is a skipped case.

## Reuse, do not reimplement

- `creatio-testing:manual-test` — the generic Jira-to-browser QA loop. Delegate the browser phase.
- `creatio-development:share-session` — normalized transcript export when the raw stream-json
  capture is unavailable, e.g. an executor run started outside this skill.
