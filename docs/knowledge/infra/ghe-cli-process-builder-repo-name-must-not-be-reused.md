---
description: the ProcessBuilder package repo on GHE was renamed cli-process-builder to crt-process-builder (ENG-95409) - spec links still rely on the redirect, so never create a new repository named cli-process-builder
applies-to:
  - spec/adr/adr-ENG-90883-backend-process-designer.md
  - spec/sprint-status.yaml
  - spec/deliver-process-builder-package/deliver-process-builder-package-plan.md
ticket: ENG-95409
date: 2026-08-19
---

**What is true** — the GHE repository holding the `CrtProcessBuilder` package source was renamed
`engineering/cli-process-builder` to `engineering/crt-process-builder` on 2026-08-17. Only
present-tense pointers in this repository were updated; the historical references were left alone on
purpose (a commit pin, dated merge notes, a completed-work section heading that doubles as an anchor).
Several links under `spec/` therefore still contain the old name and resolve only through GHE's
rename redirect.

**Why it is this way** — GHE keeps a redirect from an old repository name indefinitely, but only for
as long as the name stays unclaimed: creating a new repository with that name takes it over and the
redirect stops.

**What breaks if you ignore it** — a new `engineering/cli-process-builder` repository silently breaks
every remaining old-name link in `spec/` (the ADR's Code row, the sprint-status merge notes, the
delivery plan's repo path and its commit pin), and worse, points them at unrelated content instead
of 404-ing. Evidence this record rests on: the rename banner at the top of
`spec/adr/adr-ENG-90883-backend-process-designer.md` and the six surviving old-name references under
`spec/`. It cannot be checked from code.
