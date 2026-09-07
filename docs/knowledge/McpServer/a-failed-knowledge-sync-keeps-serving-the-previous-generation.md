---
description: update-knowledge failing on a source does NOT stop get-guidance answering - the last-known-good generation stays active, so agents keep reading stale guidance and every symptom points at the guidance text instead of the sync
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeGitRepositoryReader.cs
  - clio/Command/McpServer/Knowledge/KnowledgeBundleRuntime.cs
ticket: ENG-95891
date: 2026-08-30
---

**What is true** — when `update-knowledge` fails for a source, clio deliberately keeps that source's
previously installed generation active: the command reports `failed`, and `get-guidance` goes on
answering, at the OLD content. `info-knowledge` says `Valid: yes` and prints the OLD
`Library version`, which is the only place the staleness is visible. Nothing about a `get-guidance`
response says which generation produced it.

Three separate causes produce the identical `Git knowledge synchronization failed` line, and each
one alone looks like "sync is broken":

- A local debug branch must carry the source's own `libraryId` **and** the ~250 resource `uri`
  values built from it. Changing only `libraryId` gets you past the identity check and fails later
  with `resource '<first-item>' has an invalid descriptor`.
- A minimal git-over-HTTP server can serve `clone` and still fail protocol-v2 incremental fetch with
  `fatal: expected 'acknowledgments'`. Clone works, update does not.
- Pinning `protocol.version=0` in the checkout's git config does not help: clio validates the
  checkout configuration and refuses with `checkout configuration contains unsupported settings`.

The working repair for a local source is to clone from the bare repository by filesystem path and
then `git remote set-url origin` back to the HTTP URL.

**Why it is this way** — keeping the last-known-good generation is correct and deliberate: one
source's bad day must not withdraw a library that agents are mid-task against, and the alternative
(serving nothing) is worse. The cost of that choice is that a sync failure is not a *usage* failure,
so it never surfaces where it is felt.

**What breaks if you ignore it** — an agent evaluation reads guidance the author believes they
published and did not. Guidance fixes appear to have had no effect, so the next round of work goes
looking for a defect in the corrected text, or "fixes" the same passage again. A whole round of
manual cases can be run, analysed and reported against content that was two generations old, and
nothing in the transcripts distinguishes that from a genuine result. Read `info-knowledge` and check
the active `Library version` before drawing any conclusion from a guidance-dependent run.
