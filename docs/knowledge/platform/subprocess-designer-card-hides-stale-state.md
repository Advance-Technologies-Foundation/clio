---
description: The classic designer card's parameter ROW has no code column - a type icon, a direction icon, a getDisplayValue() label and a mapping editor - so a callee CODE rename is not on the row a person scans; the modify path cannot report it because its load converges first; and inSync cannot see a DROPPED parameter at all, the test being one-directional. Opening the card also clears the element's mapping rows in memory before rebuilding them by NAME, so saving from it persists a dropped mapping. Whether describe can see any of it is a separate question with its own record.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-92707
date: 2026-09-21
---

**What is true** - a DESIGN-TIME read of a process schema runs the platform's own parameter
synchronization; a runtime-instance read does not. Which one you get decides what you see after a called
process renames or drops a parameter, and the answers are opposite:

* `describe-business-process` can report the stale name and `inSync: false`, but only for some
  processes and only at some times. WHEN is platform behaviour and it is NOT restated here: see
  `subprocess-insync-depends-on-the-schema-instance.md`, which carries the table, the source lines and
  the record of three wrong versions. This record is about the DESIGNER CARD. **Never compile to expose
  drift either**: on this stand a compile is what left the environment not-ready past 600 seconds.
* **`inSync` is ONE-DIRECTIONAL and a DROPPED parameter is invisible to it.** It asks whether every
  parameter the CALLEE declares is present on the element: callee ADDS one → `false`; callee REMOVES one →
  `true`, because the element merely carries an extra. Measured. A code RENAME reads as add-plus-remove
  and does flip it. So `inSync` answers the rename case and says nothing about the drop.
* the MODIFY path always takes the design instance (`ProcessModifyHandler` then `GetDesignInstance`),
  which converges before the package sees the schema - which is why the re-synchronization's own drift
  report is empty.
* **the parameter ROW carries no code column.** `ProcessSchemaParameterViewConfig.js`, row body `item-view`
  at `:32-163`, builds five controls: a type icon, a direction icon, a label, a tools menu button, and a
  `Terrasoft.MappingEdit` for the mapped value. No control binds to `Name`, `Id` or any other code-bearing
  attribute, and there is no tooltip, hint or title binding either - checked exhaustively. So after a
  rename that BREAKS runtime delivery the row reads as it did before: same label, mapping present, nothing
  marked. **Measured on a stand, 2026-09-17**, at CrtProcessBuilder 1.6.3.10, through a real Chrome.
  **One trap for anyone re-measuring through the DOM:** the code IS in the markup, as
  `data-item-marker="esn-notification-item-<name>"` on every container in the row
  (`markerValue: "$MarkerValue"` → `ProcessSchemaParameterViewModel._getMarkerValue`, over
  `Id: name` at `ProcessFlowElementPropertiesPage.js:764`). A human sees nothing; a DOM read or an
  automated test can read the stale code straight out of the attribute. "Not on screen" and "not in the
  DOM" are different claims, and only the first one is true. The code is not unreachable in the UI either
  - the row's label is a click target
  that opens `ProcessSchemaParameterEditPage`, which renders a populated `Name` field - disabled, because
  the row seeds `Enabled: false` (`ProcessFlowElementPropertiesPage.js:776`) and the field binds
  `enabled` to it (`:761`). Read-only, but READ: a person who opens the parameter CAN see the code. It is
  just never on the row a person scans. (The process-level parameter list seeds `IsEnabled: true` instead
  - `ProcessSchemaPropertiesPage.js:1221` - which is where renaming is meant to happen.)
  **"Renders the caption and never the code" was the earlier claim here, and it is too strong** for a
  second reason as well: the label is `getDisplayValue()` (`ProcessFlowElementPropertiesPage.js:767`),
  which is `getCaption() || getName()` (`base-schema.js:122-124`) - a parameter with NO caption shows its
  CODE. Corrected 2026-09-21 against client source; the measurements below are unaffected.
* the card misleads in BOTH directions. Every cell carries its provenance, because "measured" is not one
  bit - it is a reading, by a particular pass, against a particular build:

  > **M10** read at CrtProcessBuilder 1.6.3.10, the 2026-09-17 designer pass.
  > **M07** read at 1.6.3.7, the earlier stand pass - a different session and a different build.
  > **M12** read at 1.6.3.12, the V1 and card pass.
  > **I** inferred from the binding mechanism. Not observed.

  | Renamed on the callee | `inSync` | Caller's STORED mapping | The card | Runtime |
  |---|---|---|---|---|
  | caption only | `true` **M12** | all printed fields identical to baseline **M10** | NEW caption, value KEPT **M12** | unaffected **I** |
  | code only | `false` **M07** | *not read* - intact **I** | old caption, mapping shown, looks healthy **M10** | **broken M07** (3x) |
  | code AND caption | `false` **M10** | byte-identical to baseline **M10** | new caption, mapping row EMPTY **M10** | **broken I** |

  The third row is a false alarm ABOUT THE STORED SCHEMA: `describe` on the caller in exactly that state
  returns the parameter byte-identical to its healthy baseline - same `uid`, same `source`, same `value`,
  same `valueDisplay` - so nothing on disk was lost. An earlier revision of this record read the empty row
  as "a visible signal, not a reassurance"; it is neither. But a later revision then called it "a
  rendering artefact", and that is wrong too - see the `clearParameters()` bullet below. Nothing on disk
  moved; the OPEN CARD really has dropped the mapping.

  Two cells in the code-only row are **M07**, and a plain **M** hid that: no `describe` and no runtime run
  happened in the code-only state during the 1.6.3.10 pass. They are real measurements against an older
  build, which is not the same claim as "measured here" - and a table headed with one version is exactly
  how the older reading gets cited as the newer one later.
* **the carry-over is keyed on NAME. Settled in source 2026-09-21, not a hypothesis.** Opening the card
  runs `SubProcessPropertiesPage.synchronizeActualSchemaParameters`: it snapshots the element's stored
  parameters, re-derives them from the callee, then re-attaches each stored value through
  `findParameterByNameOrByUId` (`process-activity-schema.js:519-521`), which is
  `findParameterByName(name) || findParameterByUId(uId)`. The UId branch CANNOT match for a RENAMED
  parameter: every re-derived copy is given a fresh GUID — `synchronizeParameter` →
  `createElementParameter` → `clone()` → `_prepareClonedParameter`
  (`parametrized-process-schema-element.js:142-149`, `:172-176`), and for a sub-process specifically
  `ProcessSubprocessSchema.createElementParameter` (`process-subprocess-schema.js:208-211`) then
  additionally calls `clearSourceValue()`. The reasoning needs one more step than "all UIds are fresh",
  because `_synchronizeSchemaParameter` writes old UIds BACK into the live collection as it iterates: but
  it only does so for a parameter that already matched **by name** in its own pass, and collection keys are
  unique, so a renamed parameter's old UId is still never present. Name is the only key that can reach it.
  There is **no fallback**: `_synchronizeSchemaParameter` (`RootUserTaskPropertiesPage.js:631-655`, which
  `SubProcessPropertiesPage` inherits) opens with
  `if (!newParameter || newParameter.dataValueType !== oldParameter.dataValueType) { return; }` — no
  positional match, no second pass, no preservation of the unmatched original. And a name match is
  NECESSARY BUT NOT SUFFICIENT — three ways to lose the value, not one: no name match, a changed
  `dataValueType` under an unchanged name, or `getCanAssignParameterSourceValue` returning false
  (`:648-652`), since `setMappingValue(oldValue)` there is the ONLY thing that puts a value back after the
  derivation stripped it.
  This **replaces the caption-pairing hypothesis** an earlier revision floated and marked unmeasured. It
  was not merely unmeasured — it was wrong, and the caption-only row had already falsified its prediction.
  The class DOES expose a `findParameterByCaption` (`:298-301`), which is probably what made the
  hypothesis feel plausible — but nothing on this path calls it. Do not re-derive the dead guess from
  that grep hit.
* **the mechanism and the code-only row disagree, and the disagreement is left standing.** Keyed on name,
  ANY code rename should miss and leave the row EMPTY — which is the third row, but not the second, where
  the card looked healthy. The likely confound is instance freshness: the 1.6.3.10 sequence was rename
  code → open designer → read card → rename the caption too, so the first open may have re-derived from a
  callee instance that had not yet picked up the rename (the caching the companion record documents),
  making the old name match. **That is a candidate, not a finding.** The code-only row is a real reading
  and is NOT rewritten to fit the mechanism; it deserves a re-run on a stand with the callee instance
  known-fresh. If it reproduces there, the mechanism is incomplete.
* **opening the card is not a free look: it clears the element's mapping rows in memory first.** The
  re-derivation begins with `clearParameters()` (`parametrized-process-schema-element.js:427-434`), which
  removes the element's rows from the parent schema's `mappings` before rebuilding them, and a stored
  value that finds no name-match is never re-attached. Closing the card discards that. **SAVING the caller
  from it persists the removal.** So "the card misleads in both directions and neither is data loss" holds
  only with the caveat: not in the stored schema, and not unless you save from that card. The one case
  that skips the whole rebuild is `getCanSynchronizeParameters` (`:96-99`) returning false, which needs
  no schema or `schemaUId === parentSchema.uId` — a process calling ITSELF, which the picker already
  excludes (`getSchemaListFilter` puts `parentSchema.uId` in `ExcludedSchemas`). No normal caller takes it.
* `inSync` does not see a caption. The caller keeps its OWN copy of the caption and reports `true` while
  the callee's differs - measured. Only a CODE change flips it, which is the right half to be sensitive
  to, since the runtime binds by code; but do not read `inSync: true` as "the element matches the
  callee".

Either way the schema that RUNS is the saved one, and the runtime binds caller to callee by parameter
NAME (`FindScalarParameterByName`), skipping an unmatched name with no exception and no log line.

**Why it is this way** — convergence on read is what keeps a design-time instance correct without a
migration step. It was never meant to be an inspection surface, and it is not one. The card inherits the
same design: it rebuilds the element from the callee every time it opens, and re-attaching the stored
values BY NAME is the only key available to it, because the element's parameter UIds are freshly
generated copies that never equal the callee's.

**What breaks if you ignore it** — the reads do NOT all agree, and the round-9 stand pass corrected this
paragraph. `describe-business-process` reads whatever instance the schema manager already HOLDS
(`ProcessSchemaRepository.LoadForDescribe`) and does not re-converge it — so while that instance predates
the callee's change it reports the STALE parameter name and `inSync: false`, and both are real evidence.
The MODIFY path always takes the design
instance (`ProcessModifyHandler` → `GetDesignInstance`), which is why its own drift report sees nothing.
The designer's card and the re-synchronization's warning list therefore report health while describe does
not - the card because the code is not on the parameter row, the warning list because its load already
converged. From CrtProcessBuilder 1.6.3.7 a re-synchronization does report the CONSEQUENCE of a DROPPED
parameter — the
references left bound to a parameter UId the element no longer carries — which is the half a caller can
act on. A CODE rename produces no consequence to find: the mapping row keeps the UId, so every reference stays
resolvable and only the saved NAME is stale. Two of the three surfaces are blind to it — the designer's
card because the parameter row has no code cell, the re-synchronization because its load converged first
— and `describe` is the one that is not, under the conditions the companion record states. A person who
suspects a problem, opens the caller and sees a correct card closes it reassured while the process keeps
delivering an empty parameter on every run, which is worse than a visibly stale name would have been; the
read that would have told them is the one nobody thinks to run, which is why the rule below is
procedural. And the same person is one click from making it worse: SAVING from that card writes back
whatever the name-keyed re-attachment failed to carry over.

The rule is therefore procedural, not diagnostic: after any change to a called process's parameters,
re-save every caller. Any `setElement` touching the element re-synchronizes and persists it, and
`subProcess: {resync: true}` asks for that and nothing else. Do it because the callee changed, not
because something looked wrong. "Re-save" means that API call — **not** opening the designer card and
saving from it, which is the one save this record warns about.

Detecting it after the fact needs the STORED metadata — the `SysSchema` body before a design-time load
touches it — which is the same surface a real drift report would need. See DQ-10 and DQ-25 in
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-deferred-questions.md`.
