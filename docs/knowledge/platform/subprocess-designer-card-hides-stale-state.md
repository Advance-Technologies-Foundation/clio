---
description: A sub-process element stale after a callee parameter CODE rename is VISIBLE through describe (runtime instance, not converged), invisible to the modify path (design instance, converged), and unshowable by the designer card, which displays the CAPTION and never the code
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
* the classic designer's card cannot show a CODE rename at all, and the reason is not convergence. The
  card renders each parameter by its CAPTION, and a code rename does not touch the caption - so after the
  rename that BREAKS runtime delivery the card reads exactly as it did before: same caption, mapping
  present, nothing marked. **Measured on a stand, 2026-09-17**, at CrtProcessBuilder 1.6.3.10, through a
  real Chrome. An earlier revision of this record predicted the reassurance from
  `SubProcessPropertiesPage.synchronizeActualSchemaParameters` and got the right answer for the wrong
  reason; the trap does not depend on whether the card converges.
* the card misleads in BOTH directions, and neither is data loss. Every cell carries its provenance,
  because "measured" is not one bit - it is a reading, by a particular pass, against a particular build:

  > **M10** read at CrtProcessBuilder 1.6.3.10, the 2026-09-17 designer pass.
  > **M07** read at 1.6.3.7, the earlier stand pass - a different session and a different build.
  > **I** inferred from the binding mechanism. Not observed.

  | Renamed on the callee | `inSync` | Caller's STORED mapping | The card | Runtime |
  |---|---|---|---|---|
  | caption only | `true` **M10** | all printed fields identical to baseline **M10** | *not read* | unaffected **I** |
  | code only | `false` **M07** | *not read* - intact **I** | old caption, mapping shown, looks healthy **M10** | **broken M07** (3x) |
  | code AND caption | `false` **M10** | byte-identical to baseline **M10** | new caption, mapping row EMPTY **M10** | **broken I** |

  The third row is a FALSE ALARM: `describe` on the caller in exactly that state returns the parameter
  byte-identical to its healthy baseline - same `uid`, same `source`, same `value`, same `valueDisplay` -
  so the empty row is a rendering artefact and the mapping is really there. An earlier revision of this
  record read the empty row as "a visible signal, not a reassurance"; it is neither.

  Two cells in the code-only row are **M07**, and a plain **M** hid that: no `describe` and no runtime run
  happened in the code-only state during the 1.6.3.10 pass. They are real measurements against an older
  build, which is not the same claim as "measured here" - and a table headed with one version is exactly
  how the older reading gets cited as the newer one later.
* one hypothesis fits all three rows - the card pairs the caller's stored parameter to the callee's by
  CAPTION, so an unchanged caption matches and a changed one does not. **Inference, not measured.** It
  predicts that the caption-only row would ALSO render empty, which is the cheap check that would confirm
  or kill it.
* `inSync` does not see a caption. The caller keeps its OWN copy of the caption and reports `true` while
  the callee's differs - measured. Only a CODE change flips it, which is the right half to be sensitive
  to, since the runtime binds by code; but do not read `inSync: true` as "the element matches the
  callee".

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
The designer's card and the re-synchronization's warning list therefore report health while describe does
not - the card because the code is never on screen, the warning list because its load already converged. From
CrtProcessBuilder 1.6.3.7 a re-synchronization does report the CONSEQUENCE of a DROPPED parameter — the
references left bound to a parameter UId the element no longer carries — which is the half a caller can
act on. A CODE rename produces no consequence to find: the mapping row keeps the UId, so every reference stays
resolvable and only the saved NAME is stale. Two of the three surfaces are blind to it — the designer's
card because it shows the caption, the re-synchronization because its load converged first — and
`describe` against a COMPILED caller is the one that is not. A person who suspects a problem, opens the
caller and sees a correct card closes it reassured while the process keeps delivering an empty parameter
on every run, which is worse than a visibly stale name would have been; the read that would have told
them is the one nobody thinks to run, which is why the rule below is procedural.

The rule is therefore procedural, not diagnostic: after any change to a called process's parameters,
re-save every caller. Any `setElement` touching the element re-synchronizes and persists it, and
`subProcess: {resync: true}` asks for that and nothing else. Do it because the callee changed, not
because something looked wrong.

Detecting it after the fact needs the STORED metadata — the `SysSchema` body before a design-time load
touches it — which is the same surface a real drift report would need. See DQ-10 and DQ-25 in
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-deferred-questions.md`.
