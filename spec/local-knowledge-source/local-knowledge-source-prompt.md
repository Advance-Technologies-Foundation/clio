# Session prompt

Paste the block below as the first message of a fresh session in `C:\Projects\clio`.

---

Work on branch `fix/local-knowledge-source` in this repository. It carries two commits that fix a bug
where a Git knowledge source installs successfully and then serves nothing. An adversarial review found
five issues that must be addressed before this opens as a pull request.

Read these two first, in order — they are self-contained and assume no prior context:

- `spec/local-knowledge-source/local-knowledge-source-handoff.md` — what the branch does, the bug it
  fixes, what was excluded by measurement, and how to reproduce the whole thing locally.
- `spec/local-knowledge-source/local-knowledge-source-review-findings.md` — the five findings, with exact
  file and line references, and a "checked and found nothing" list you should not re-derive.

Do them in this order, and do not reorder without saying why:

1. **Finding 1 first.** It is a regression this branch introduced: `ClearReadOnlyAttributes` recurses
   through directory symlinks and junctions, where the delete it prepares for does not. It can clear
   attributes outside the managed root — including on a checkout clio has just rejected as untrusted — and
   it can throw where a delete previously succeeded. `Common/Skills/Agents/CodexAgent.cs:108-141` already
   has the correct shape; port it into ONE shared helper, because the current code is duplicated in two
   classes.
2. **Finding 2.** Add the missing test for the stdin behaviour: a child that reads stdin to completion
   must see EOF and exit, not block. This is the class behind every process launch in clio and nothing
   pins the change today.
3. **Findings 3, 4 and 5** — redact `LastDiagnostic`, decide the fire-and-forget path, drop the dead
   parameter.
4. **Finding 6** — the `docs/knowledge/` record `AGENTS.md` requires in the same pull request.

Then run `dotnet test clio.tests --filter "Category=Unit&(Module=Common|Module=McpServer)"` (4945 passed
before your changes) and verify the original bug is still fixed end to end using the reproduction recipe in
the handoff. The verification is not optional for finding 1: the reparse-point fix touches the same delete
path whose failure produced the "not owned by Clio" dead end.

Two things to know before you start:

- The reproduction needs a **smart** HTTP git server on loopback. clio clones with
  `--filter=blob:none --depth=1`, and a static file server cannot serve either — a plain `git clone`
  against one silently falls back to a full clone and succeeds while clio's fails. The handoff explains
  the setup and the two other traps (a reserved library id, fast-forward-only updates).
- Enabling `knowledge-allow-unsequenced` is part of that setup. It is **persistent and global**, and while
  it is on the content-integrity check does not apply. Disable it when you are done, and restore
  `creatio-curated`.

Do not open a pull request without being asked. Report what you changed, what you verified, and anything
you found that the review missed.
