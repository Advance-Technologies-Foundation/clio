---
description: A sub-process element stale after a callee parameter CODE rename is visible through describe ONLY while the manager still holds an instance built BEFORE the change - prime it by describing once first, because any later read builds a converged one - invisible to the modify path, invisible to inSync when the callee DROPPED a parameter, and unshowable by the designer card, which never displays a code
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-92707
date: 2026-09-18
---

**What is true** - a DESIGN-TIME read of a process schema runs the platform's own parameter
synchronization; a runtime-instance read does not. Which one you get decides what you see after a called
process renames or drops a parameter, and the answers are opposite:

* `describe-business-process` returns the manager's instance AS-IS
  (`ProcessSchemaRepository.LoadForDescribe`), so while that instance predates the callee's change it
  reports the STALE parameter name with `inSync: false`. With no instance cached one is BUILT, and the
  build converges. The fallback to the design instance happens only when the manager has no ITEM at all
  (file-design mode): `FindInstanceByUId` returns `default` solely on a missing item, so a process merely
  unread since its last save does NOT fall back — it gets a fresh, converged instance.
* **What decides it is WHEN the instance was built — cache timing, not running and not compiling.**
  Describe reads whatever instance the schema manager already holds and does not re-converge it, so an
  instance built BEFORE the callee changed reports the stale state, and one built AFTER it reports
  `true` — because a freshly built instance CONVERGES as it is created
  (`BaseProcessSchemaManager.CreateSchemaInstance` routes an interpretable process to
  `FindInstanceFromMetaData`, whose `GetItemFromMetaData` calls `SynchronizeParameters()`).
  `SchemaManagerItem.Instance` is a lazy double-checked build, so ANY reader creates one — an earlier
  describe as much as a run — and saving the schema evicts it (`DropInstance` / `ClearRuntimeInstances`).
  **Verified in platform source 2026-09-18.**
* **Two earlier revisions of this record got the mechanism wrong, in opposite directions**, and the
  second was worse than the first. It said "a runtime instance is produced by RUNNING the process" and
  told the reader to do that. Running or re-reading the caller AFTER changing the callee builds a fresh,
  converged instance and HIDES the drift — the advice actively destroyed the evidence it promised to
  reveal. The control behind it showed only that a run is one way to PRIME the cache before the change,
  which is not the same as being the mechanism. **Never compile to expose drift either**: on this stand a
  compile is what left the environment not-ready past 600 seconds.
* **`inSync` is ONE-DIRECTIONAL and a DROPPED parameter is invisible to it.** It asks whether every
  parameter the CALLEE declares is present on the element: callee ADDS one → `false`; callee REMOVES one →
  `true`, because the element merely carries an extra. Measured. A code RENAME reads as add-plus-remove
  and does flip it. So `inSync` answers the rename case and says nothing about the drop.
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
  > **M12** read at 1.6.3.12, the V1 and card pass.
  > **I** inferred from the binding mechanism. Not observed.

  | Renamed on the callee | `inSync` | Caller's STORED mapping | The card | Runtime |
  |---|---|---|---|---|
  | caption only | `true` **M12** | all printed fields identical to baseline **M10** | NEW caption, value KEPT **M12** | unaffected **I** |
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
* that hypothesis is DEAD. It said the card pairs by CAPTION, and predicted the caption-only row would
  render empty too. Measured 2026-09-17: a caption-only rename shows the NEW caption and KEEPS the mapped
  value. **No mechanism is established for why the code+caption row empties** — do not reason forward from
  one, and do not replace this with a third guess. Three readings, no model.
* `inSync` does not see a caption. The caller keeps its OWN copy of the caption and reports `true` while
  the callee's differs - measured. Only a CODE change flips it, which is the right half to be sensitive
  to, since the runtime binds by code; but do not read `inSync: true` as "the element matches the
  callee".

Either way the schema that RUNS is the saved one, and the runtime binds caller to callee by parameter
NAME (`FindScalarParameterByName`), skipping an unmatched name with no exception and no log line.

**Why it is this way** — convergence on read is what keeps a design-time instance correct without a
migration step. It was never meant to be an inspection surface, and it is not one.

**What breaks if you ignore it** — the reads do NOT all agree, and the round-9 stand pass corrected this
paragraph. `describe-business-process` reads whatever instance the schema manager already HOLDS
(`ProcessSchemaRepository.LoadForDescribe`) and does not re-converge it — so while that instance predates
the callee's change it reports the STALE parameter name and `inSync: false`, and both are real evidence.
It falls back to the design instance when nothing is cached, and that one converges as it loads. The MODIFY path always takes the design
instance (`ProcessModifyHandler` → `GetDesignInstance`), which is why its own drift report sees nothing.
The designer's card and the re-synchronization's warning list therefore report health while describe does
not - the card because the code is never on screen, the warning list because its load already converged. From
CrtProcessBuilder 1.6.3.7 a re-synchronization does report the CONSEQUENCE of a DROPPED parameter — the
references left bound to a parameter UId the element no longer carries — which is the half a caller can
act on. A CODE rename produces no consequence to find: the mapping row keeps the UId, so every reference stays
resolvable and only the saved NAME is stale. Two of the three surfaces are blind to it — the designer's
card because it shows the caption, the re-synchronization because its load converged first — and
`describe` against a caller whose cached instance predates the change is the one that is not. A person who suspects a problem, opens the
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
