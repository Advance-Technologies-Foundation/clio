# Session prompt

Paste the block below as the first message of a fresh session in `C:\Projects\clio`.

---

Work on branch `fix/local-knowledge-source` in this repository (`C:\Projects\clio`). Check it out first —
it is pushed and independent of every other branch here.

It carries two commits that fix a bug where a Git knowledge source installs successfully and then serves
nothing. An adversarial review found five issues that must be addressed before this opens as a pull
request. **One of them is a regression the branch itself introduced.**

Read these two before touching anything. They are self-contained and assume no prior context — the
investigation behind this branch took most of a day and eight excluded hypotheses, and almost none of that
survives in the diff:

- `spec/local-knowledge-source/local-knowledge-source-handoff.md` — what the branch fixes, the measurement
  that found the cause, what was ruled out, and how to reproduce the whole thing locally.
- `spec/local-knowledge-source/local-knowledge-source-review-findings.md` — the five findings with exact
  file and line references, plus a "checked and found nothing" list you should not re-derive.

Do them in this order, and say why if you reorder:

1. **Finding 1 — the regression, first.** `ClearReadOnlyAttributes` recurses through directory symlinks
   and junctions, where the `Directory.Delete` it prepares for unlinks them instead. It can clear
   read-only attributes outside the managed root — including on a checkout clio has just rejected as
   untrusted — and it can throw from the enumerator where a delete previously succeeded, which recreates
   the "not owned by Clio" dead end this branch exists to remove.
   `Common/Skills/Agents/CodexAgent.cs:108-141` already has the correct shape. Port it into ONE shared
   helper: the current code is copy-pasted into two classes, so an in-place fix has to be written twice.
2. **Finding 2 — the missing test.** Nothing pins the stdin change, and it is the class behind every
   process launch in clio. A child that reads stdin to completion must see EOF and exit rather than block.
3. **Findings 3, 4, 5** — redact `LastDiagnostic` before it leaves the server, decide the fire-and-forget
   path, drop the dead `recursive` parameter.
4. **Finding 6** — the `docs/knowledge/` record `AGENTS.md` requires in the same pull request. "A child
   process inherits clio's stdin unless output is redirected" is exactly the implicit-behaviour-with-a-
   silent-failure class that rule exists for.

Then run `dotnet test clio.tests --filter "Category=Unit&(Module=Common|Module=McpServer)"` — 4945 passed
before your changes — and verify the ORIGINAL bug is still fixed end to end using the reproduction recipe
in the handoff. That verification is not optional for finding 1: the reparse-point fix touches the same
delete path whose failure produced the dead end.

Three things to know before you start:

- The reproduction needs a **smart** HTTP git server on loopback. clio clones with
  `--filter=blob:none --depth=1`, and neither a partial nor a shallow clone works over the dumb protocol —
  so a static file server fails, while a plain `git clone` against that same server succeeds and misleads
  you. The handoff has the setup and the two other traps (a permanently reserved library id;
  fast-forward-only updates).
- Enabling `knowledge-allow-unsequenced` is part of that setup. It is persistent and global, and while it
  is on the content-integrity check does not apply. Disable it when you are done and restore
  `creatio-curated`.
- The branch is one commit behind `origin/master` (`31d191651`). No conflict is expected — different
  files — but rebase before opening the pull request.

Do not open a pull request without being asked. Report what you changed, what you verified, and anything
you found that the review missed.
