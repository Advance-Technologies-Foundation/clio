# Repository knowledge base

This directory records **what the code does not say**: workarounds, temporary decisions, implicit
behaviour whose failure is silent, and external facts about the platform and the infrastructure.

One file is one fact. There is no index — an index would be a single file every branch has to
touch, which is the problem this base was created to escape.

> **This is internal repository knowledge.** It is unrelated to the *shipped guidance library*
> (`clio-knowledge`, `get-guidance`, `clio/Command/McpServer/Knowledge/`), which is a product
> surface delivered to users and agents through MCP. Nothing here is shipped; nothing here is
> reachable with `get-guidance`. If you are looking for the published guides, you are in the wrong
> directory.

## When a record is mandatory

The test is **"the code does not say this"** — the next engineer or agent will not learn it by
reading the code and the tests. In practice:

- a workaround or a temporary decision,
- implicit behaviour whose failure is **silent**,
- an external fact (server, stand, TeamCity, platform),
- a rejected alternative someone will predictably return to.

**Anything else is not written.** Exceptions are allowed but are meant to be rare. In particular
these are *not* records: what a PR did, a merge or a rebase, a review round, a CI or Sonar fix, a
"why we decided this at the time" narrative that is neither a workaround nor a trap. Git, the PR
thread and Jira already carry those.

Add the record **in the same pull request** that introduces the thing being recorded, so the
reviewer sees the workaround and its description together and can push back on both.

## Layout

```
docs/knowledge/
  <code module>/   # the module map in AGENTS.md: Command, McpServer, Common, Package,
                   # Workspace, Core, Theming, ModelBuilder, Analyzers, ClioGate, ClioRing, ...
  platform/        # Creatio and server behaviour
  infra/           # stands, TeamCity, CI, VPN
  process/         # release, Sonar, review
```

Pick the directory by **where the reader will be standing** when they need the fact. A trap in
`clio/Command/McpServer/**` goes in `McpServer/` even when the underlying cause is a platform one;
`platform/` is for facts that hold no matter which module you are in.

Create a new module directory when you need one — it is a `mkdir`, not a decision.

## Record schema

```markdown
---
description: one line, this is what grep matches on
applies-to:
  - clio/Common/Foo.cs
  - clio/Command/McpServer/Bar.cs
ticket: ENG-XXXXX
date: 2026-08-19
---

**What is true** — the fact itself.

**Why it is this way** — the constraint that forced it.

**What breaks if you ignore it** — the concrete failure. This paragraph is the point of the
record; without it the entry is a restatement of the code.
```

Target 10–25 lines. There is deliberately no `type` field — the ceremony stays low.

| Field | Rules |
|---|---|
| `description` | Required. One line. Written so that a `grep` for the symbol, path or error text a reader would actually type finds it. |
| `applies-to` | Required, at least one entry. **Literal repository-relative paths, or directory prefixes ending in `/`. No globs, no absolute paths.** A path either exists or it does not — that is what makes the dead-path report meaningful. |
| `ticket` | Optional. Omit it rather than inventing one. |
| `date` | Required, `YYYY-MM-DD`. When the fact was recorded or last re-verified. |

File name: a slug that reads as the fact, not as a ticket — `applies-to-paths-are-literal.md`, not
`eng-95557.md`.

## Staleness

**Whoever changes a file listed in a record's `applies-to` must update or delete that record in the
same pull request.** A fact that stopped being true is `rm`, not an edit that hedges it.

You get told which records apply: `scripts/check-knowledge-applies-to.py` intersects every
record's `applies-to` with the pull request's diff, and the `Knowledge base check` workflow posts
the result as a pull-request comment. The same script reports records whose `applies-to` paths no
longer exist on disk.

The check is **advisory and never turns the pull request red** — touching a file for an unrelated
reason is legitimate and frequent, and a blocking check would only produce cosmetic edits to
records.

Run it locally — a local run also counts uncommitted work, so it reports before the commit what the
pull request will report after it:

```bash
make check-knowledge
```

```bash
python3 scripts/check-knowledge-applies-to.py --dead-only
```

## History

Before 2026-08 this knowledge was appended to a single chronological file, `.codex/workspace-diary.md`.
It reached 1206 entries / ~560k tokens — more than three context windows, so the instruction to read
it could not be carried out, and 30% of the paths it referenced no longer existed. It is archived
read-only at [`.codex/archive/workspace-diary-2026-08.md`](../../.codex/archive/workspace-diary-2026-08.md)
for `grep` and for the specification documents that cite it. **Do not append to it.**
