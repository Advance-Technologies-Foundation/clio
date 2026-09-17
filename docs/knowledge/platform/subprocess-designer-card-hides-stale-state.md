---
description: Opening a sub-process element's card re-synchronizes it as it renders, so after a callee parameter rename the designer shows the healthy NEW name while the saved schema still carries the old one and delivers nothing
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-92707
date: 2026-09-17
---

**What is true** — every design-time read of a process schema runs the platform's own parameter
synchronization, and that includes the classic designer rendering a Sub-process element's card. So after
a called process renames or drops a parameter, opening the CALLER shows the element already converged:
the new parameter name, the mapping intact, nothing amiss. The schema that RUNS is the saved one, which
still carries the old name — and the runtime binds caller to callee by parameter NAME
(`FindScalarParameterByName`), skipping an unmatched name with no exception and no log line.

Measured on a stand at CrtProcessBuilder 1.6.3.6, 2026-09-17, during the ENG-92707 manual pass (TC-10).

**Why it is this way** — convergence on read is what keeps a design-time instance correct without a
migration step. It was never meant to be an inspection surface, and it is not one.

**What breaks if you ignore it** — no read reveals the stale state itself. Not the designer, not
`describe-business-process`, not `inSync`, and not the re-synchronization's own warning list: all four go
through a design-time load that converges the element first, so all four report health. From
CrtProcessBuilder 1.6.3.7 a re-synchronization does report the CONSEQUENCE of a DROPPED parameter — the
references left bound to a parameter UId the element no longer carries — which is the half a caller can
act on. A RENAME produces no consequence to find: the mapping row keeps the UId, so every reference stays
resolvable and only the saved NAME is stale. That case remains invisible to every surface. A person who
suspects a problem, opens the caller and sees a correct card closes it reassured while the process keeps
delivering an empty parameter on every run — which is worse than a visibly stale name would have been.

The rule is therefore procedural, not diagnostic: after any change to a called process's parameters,
re-save every caller. Any `setElement` touching the element re-synchronizes and persists it, and
`subProcess: {resync: true}` asks for that and nothing else. Do it because the callee changed, not
because something looked wrong.

Detecting it after the fact needs the STORED metadata — the `SysSchema` body before a design-time load
touches it — which is the same surface a real drift report would need. See DQ-10 and DQ-25 in
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-deferred-questions.md`.
