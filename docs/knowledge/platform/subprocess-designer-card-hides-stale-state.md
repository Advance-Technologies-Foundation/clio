---
description: A sub-process element stale after a callee parameter rename is VISIBLE through describe (runtime instance, not converged) and invisible to the modify path (design instance, converged); the designer card is believed to converge as it renders, which is read from source and not yet measured
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-92707
date: 2026-09-17
---

**What is true** - a DESIGN-TIME read of a process schema runs the platform's own parameter
synchronization; a runtime-instance read does not. Which one you get decides what you see after a called
process renames or drops a parameter, and the answers are opposite:

* `describe-business-process` prefers the RUNTIME instance for a compiled process
  (`ProcessSchemaRepository.LoadForDescribe`), so it reports the STALE parameter name and
  `inSync: false`. **Measured on a stand, 2026-09-17.** For an uncompiled process there is no runtime
  instance, it falls back to the design instance, and that one converges.
* the MODIFY path always takes the design instance (`ProcessModifyHandler` then `GetDesignInstance`),
  which converges before the package sees the schema - which is why the re-synchronization's own drift
  report is empty.
* the classic designer's card is believed to converge as it renders
  (`SubProcessPropertiesPage.synchronizeActualSchemaParameters`), which would show the new name and
  reassure the reader. **Read from the designer's source, NOT measured** - the classic designer would not
  load on the stand used for the pass, so this clause is still owed a measurement.

Either way the schema that RUNS is the saved one, and the runtime binds caller to callee by parameter
NAME (`FindScalarParameterByName`), skipping an unmatched name with no exception and no log line.

**Why it is this way** — convergence on read is what keeps a design-time instance correct without a
migration step. It was never meant to be an inspection surface, and it is not one.

**What breaks if you ignore it** — the reads do NOT all agree, and the round-9 stand pass corrected this
paragraph. `describe-business-process` prefers the RUNTIME instance for a compiled process
(`ProcessSchemaRepository.LoadForDescribe`), which the platform does not converge — so it reports the
STALE parameter name and `inSync: false`, and both are real evidence. It falls back to the design
instance only for an uncompiled process, and that one converges. The MODIFY path always takes the design
instance (`ProcessModifyHandler` → `GetDesignInstance`), which is why its own drift report sees nothing.
The designer and the re-synchronization's warning list therefore report health while describe does not. From
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
