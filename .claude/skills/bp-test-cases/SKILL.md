---
name: bp-test-cases
description: Author an AI-executable manual test prompt for committed business-process functionality and publish it on the Jira issue as a comment. Use after process functionality is committed, when asked to add manual test cases or a manual test prompt to a Jira task, or to revise an existing prompt after a failed run. Produces business-level scenarios with explicit design-time and runtime expectations; it does not execute them.
---

# BP manual test cases

Turn committed business-process work into a **prompt that a different AI session can execute from
scratch** against a real stand, and publish it on the Jira issue.

The prompt is not documentation of the change. It is the artifact under test: the run
(`/bp-test-run`) measures whether the shipped MCP surface and guidance library are good enough for
an agent that knows nothing but this prompt. Every fact the executor needs must be inside it;
every fact it must discover for itself must be left out.

## Invocation contract

`/bp-test-cases <ENG-KEY-or-URL> [--feature <slug>] [--range <git-range>] [--revise]`

- `<ENG-KEY-or-URL>` — **required**. The Jira issue the functionality belongs to.
- `--feature <slug>` — feature folder under `spec/`. Default: derive from the issue key and summary
  (`eng-95891-formula-expressions`). Must satisfy the `spec/<feature-name>/` convention in AGENTS.md.
- `--range <git-range>` — commits that carry the functionality. Default: `master..HEAD`.
- `--revise` — rewrite the existing prompt instead of writing a new one. Use after a run exposed a
  prompt defect (see the run report's *Prompt defects* section).

## What it reads

1. The committed diff in `--range` — what actually shipped, including the MCP tool surface and any
   guidance trigger lines that changed.
2. The Jira issue: description, acceptance criteria, comments that materially change behavior
   (`getJiraIssue`).
3. The guidance articles the feature depends on, in `C:\Projects\clio-knowledge\guidance\` — the
   executor will be steered by them, so the prompt must be answerable using them.

## Hard rules for the prompt

These are what make the run meaningful. Violating any one of them turns the test into a
transcription exercise. The full contract with worked examples is in
[references/prompt-contract.md](references/prompt-contract.md) — read it before drafting.

- **Business requirements, not construction steps.** State the outcome the business needs. Never
  name process elements, tool names, arguments, schema names, or UIds.
- **Two observation blocks per case, always both**: *Design time* — what the person opening the
  process designer must see; *Runtime* — what must happen when the process actually runs, with the
  observable trace that proves it.
- **Self-contained.** The executor session has no memory, no repository, and no access to this
  Jira issue. Anything it cannot discover through the clio MCP surface must be stated in the prompt.
- **No leading.** Do not hint at the element, the order of operations, or the tool to call. If the
  prompt has to explain how, the guidance library is what needs fixing — record that instead.
- **English**, `TC-0X` blocks, one scenario per case.

## Workflow

1. Read the diff, the issue, and the affected guidance articles.
2. Identify the **business capability** that shipped — the thing a user can now express in a
   process that they could not before. This is the subject of the prompt, not the implementation.
3. Draft the cases: happy path first, then the negative/boundary cases the diff actually supports.
   Do not invent behavior the code does not implement.
4. Check the draft against every rule in `references/prompt-contract.md`. Reject and rewrite your
   own draft on the first violation — this is cheaper than a wasted stand run.
5. Write it to `spec/<feature>/<feature>-manual-test-prompt.md`. This file is the version of record
   and is committed; `--revise` overwrites it so the diff shows how the prompt evolved. **Without
   `--revise`, refuse to overwrite an existing prompt** — show the difference and ask. A prompt that
   has already been executed is the only record of what a past run actually measured.
6. Show the draft in chat and wait for approval before touching Jira.
7. Post it to the issue with `addCommentToJiraIssue`.

## Jira write policy

**Comments only.** Never modify the issue description or any other field. The description is the
statement of work; the test prompt is revised every iteration, and its history reads correctly as a
comment thread. The committed file in `spec/` remains the authoritative copy.

This overrides `jira-manual-test-cases`, which edits the description's `Test Cases` section: reuse
its `TC-0X` formatting, redirect its output into a comment.

## Blockers

- **Atlassian MCP not authorized** — `atlassian` is listed in `~/.claude/mcp-needs-auth-cache.json`
  until the user authorizes it (claude.ai connector settings, or `claude mcp` / `/mcp` in an
  interactive session). Write the `spec/` file, then stop and say the prompt is ready but Jira is
  unreachable. Never report the cases as published when they were not.
- **No committed functionality in `--range`** — stop and ask. A prompt written against uncommitted
  work cannot be reproduced by the run.

## Handoff

Print the exact next command:

`/bp-test-run <ENG-KEY>`
