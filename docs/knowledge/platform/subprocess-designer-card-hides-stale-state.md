---
description: The designer card's parameter ROW has no code column (ProcessSchemaParameterViewConfig), so a callee CODE rename is invisible on a captioned parameter; and OPENING the card runs synchronizeActualSchemaParameters - clearParameters() on the live schema, then re-attach by NAME only through findParameterByNameOrByUId - so a drifted mapping is dropped in memory, and the card's only button commits on close (onHidePropertyPage saves first). There is no Cancel. inSync is one-directional and cannot see a DROPPED parameter. Which schema instance describe reads is a separate question: subprocess-insync-depends-on-the-schema-instance.md.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
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
  (`:24-164`, its `items` array at `:32-163`), builds five controls: a type icon, a direction icon, a
  label, a tools menu button, and a `Terrasoft.MappingEdit` for the mapped value. No VISIBLE control renders `Name` or `Id`, and there is no
  tooltip, hint or title binding - checked exhaustively. "No code-bearing BINDING" would be false, and the
  next paragraph says why. So after a rename that BREAKS runtime delivery the row reads as it did before:
  mapping present, nothing marked, and - **when the parameter has a caption** - the same label.
  Without a caption the label IS the code and the rename shows there; see the `getDisplayValue()` note
  below. **Measured on a stand, 2026-09-17**, at CrtProcessBuilder 1.6.3.10, through a real Chrome, on
  captioned parameters.
  **One trap for anyone re-measuring through the DOM:** the code IS in the markup, as
  `data-item-marker="esn-notification-item-<name>"`, fed by `markerValue: "$MarkerValue"` →
  `ProcessSchemaParameterViewModel._getMarkerValue` over `Id: name`
  (`ProcessFlowElementPropertiesPage.js:764`). Two bounds on that: it lands on the row's containers and
  its two image controls but NOT on the label, the tools button or the `MappingEdit`; and it is emitted
  only when `UseMarkerValue` is on (`component.js:824-826` over `GlobalAppSettings.UseMarkerValue`, which
  ships true). A human sees nothing; a DOM read or an
  automated test can read the stale code straight out of the attribute. "Not on screen" and "not in the
  DOM" are different claims, and only the first one is true. The code is not unreachable in the UI either:
  the row's label is a click target that replaces the row IN PLACE with the
  `ProcessSchemaParameterEditModule` inline editor (`MappingEditMixin.js:28-51`, into the row's own
  `item-edit` container - it is not a separate page), whose schema is `ProcessSchemaParameterEditPage` and
  which renders a populated
  `Name` field - disabled, through a three-hop chain worth spelling out because the attribute is RENAMED
  in the middle of it: the row seeds `Enabled: false` (`ProcessFlowElementPropertiesPage.js:776`),
  `getParameterEditInfo` copies it across as `IsEnabled: this.get("Enabled")`
  (`MappingEditMixin.js:104`), and the field binds `enabled` to `IsEnabled`
  (`ProcessSchemaParameterEditPage.js:761`). Read-only, but READ: a person who opens the parameter CAN see
  the code. It is just never on the row a person scans. (The PROCESS-level parameter list is where
  renaming is meant to happen, and it overrides the row's `Enabled` at
  `ProcessSchemaPropertiesPage.js:463` - CONDITIONALLY: `useUnlockInheritedProcessParameters ||
  parameter.createdInSchemaUId === this.$ProcessElement.uId` (`:451-452`), so an INHERITED parameter stays
  disabled there too unless the `UseUnlockInheritedProcessParameters` feature is on. Do not cite `:1221`
  for this, as an earlier revision did - that is `getNewParameterEditInfo`, the payload for a
  BRAND-NEW parameter, next to `IsNew: true`.)
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

  **Markers differ WITHIN a row on purpose - that is the whole point of tagging cells rather than rows.**
  A row is not one reading: each cell was taken by whichever pass last read that particular thing, so
  `M12` beside `M10` in the caption-only row means the caption-only `inSync` and card were re-read at
  1.6.3.12 while the stored-mapping cell still carries its 1.6.3.10 reading. A reviewer read this as an
  inconsistency, which means the legend alone was not enough; it is not one. One cell IS unexplained
  though, and is flagged rather than quietly resolved: the record's account of the 1.6.3.10 sequence
  (rename code → open designer → read card → rename the caption too) contains no caption-only state, so
  what produced that row's **M10** stored-mapping reading is not established here. Do not re-tag it from
  inference - find the session or re-measure.

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
  `createElementParameter` (`parametrized-process-schema-element.js:172-177`), which calls
  `schemaParameter.clone()` and `_prepareClonedParameter` as SIBLINGS (`:173`, `:174`; the GUID is
  assigned in the latter, `:142-149`) — not a chain, though an earlier revision wrote it as one, and for a sub-process specifically
  `ProcessSubprocessSchema.createElementParameter` (`process-subprocess-schema.js:208-211`) then
  additionally calls `clearSourceValue()`. The reasoning needs one more step than "all UIds are fresh",
  because `_synchronizeSchemaParameter` writes old UIds BACK into the live collection as it iterates: but
  it only does so for a parameter that already matched **by name** in its own pass, and collection keys are
  unique, so a renamed parameter's old UId is still never present. Name is the only key that can reach it.
  There is **no fallback**: `_synchronizeSchemaParameter` (`RootUserTaskPropertiesPage.js:631-655`, which
  `SubProcessPropertiesPage` inherits) opens with
  `if (!newParameter || newParameter.dataValueType !== oldParameter.dataValueType) { return; }` — no
  positional match, no second pass, no preservation of the unmatched original. And a name match is
  NECESSARY BUT NOT SUFFICIENT — **four ways to lose the value**, since `setMappingValue(oldValue)` is the
  ONLY thing that puts a value back after the derivation stripped it:
  1. no name match (the rename case);
  2. a changed `dataValueType` under an unchanged name;
  3. `getCanAssignParameterSourceValue` returning false (`RootUserTaskPropertiesPage.js:645-650`). This
     one is NOT a standalone failure mode: for a sub-process the predicate is
     `AssignableProcessSchemaParameterDirections.includes(parameter.direction)` =
     `[IN, VARIABLE]` (`parametrized-process-schema-element.js:203-205`), so if the direction did not
     change there was no assignable mapping to lose. It bites when the callee changed the DIRECTION to a
     non-assignable one under an unchanged name and type;
  4. **nested parameters, environment-gated.** The snapshot is `getRootParameters()` — roots only — and
     nested `itemProperties` values are re-attached solely by `_synchronizeNestedParameters`
     (`RootUserTaskPropertiesPage.js:661-671`), whose first statement is
     `if (!Terrasoft.Features.getIsEnabled("ManageProcessCollectionParameters")) { return; }`. With that
     feature OFF, every stored value on a nested/collection-item parameter is dropped on card open
     REGARDLESS of name match.
  This **replaces the caption-pairing hypothesis** an earlier revision floated and marked unmeasured. It
  was not merely unmeasured — it was wrong, and the caption-only row had already falsified its prediction.
  The class DOES expose a `findParameterByCaption` (`parametrized-process-schema-element.js:298-301`), which is probably what made the
  hypothesis feel plausible — but nothing on this path calls it. Do not re-derive the dead guess from
  that grep hit.
* **the mechanism and the code-only row disagree, and NO explanation is established for it.** Keyed on
  name, ANY code rename should miss and leave the row EMPTY — which is the third row, but not the second,
  where the card looked healthy. **There is no supported mechanism for that gap.** An earlier revision of
  this bullet offered "instance freshness" and cited
  `subprocess-insync-depends-on-the-schema-instance.md` for it; that citation was wrong on every count and
  is withdrawn. That record is about the CALLER's instance, it says in terms that "every row below assumes
  the CALLEE's own read reflects the change", the only stale-callee route it names is COMPILATION rather
  than caching, it records that saving EVICTS (`SchemaManager.cs:2311,2315,2317`), and it is about the
  .NET schema manager while the card re-derives client-side in JS. Wrong side, wrong layer, wrong
  mechanism. It also cuts the other way: that record has a stale callee reporting `inSync: true`, and the
  code-only row's `inSync` is `false` — though as **M07** off a different build it cannot settle anything
  either. The code-only row is a real reading and is NOT rewritten to fit the mechanism; it needs a re-run
  on a stand, with the callee's CLIENT-side schema known-fresh. Until then this record has three readings
  and a mechanism that explains two of them — **do not reason forward from the mechanism for the third,
  and do not fill the gap with a guess.** That standing order is what the caption-pairing hypothesis
  already cost.
* **OPENING the card is the destructive act, and there is no way to back out of it.** The re-derivation
  begins with `clearParameters()` (`parametrized-process-schema-element.js:427-434`), which removes the
  element's rows from the parent schema's `mappings` before rebuilding them, and a stored value that finds
  no name-match is never re-attached. This happens on the LIVE objects: the page takes the element by
  reference (`BaseProcessSchemaElementPropertiesPage.js:393`, `this.set("ProcessElement", element)`, and
  there is no `clone` anywhere in that file), so nothing snapshots it and nothing rolls it back.
  **An earlier revision of this bullet said "closing the card discards that" and warned against "saving
  from that card". Both are refuted by source, 2026-09-21, and the truth is worse.** The card has exactly
  ONE button - `ClosePropertyButton` (`BaseProcessSchemaElementPropertiesPage.js:1044-1053`) - bound to
  `onHidePropertyPage`, whose FIRST statement is `this.saveElementProperties()`
  (`process-schema-designer-view-model.js:224-231`); ESC takes the same path (`:959`). There is no Cancel,
  no Save, and no rollback: closing COMMITS. So a person who merely opens the card on a drifted caller has
  already damaged the in-memory schema and cannot undo it by closing.
  **Where this stops being traced:** whether that emptied mapping then reaches the SERVER depends on a
  subsequent designer schema save, and that serialization step is NOT traced here. Treat "it gets
  persisted" as INFERENCE. What source establishes is the in-memory mutation and the commit-on-close.
  The one case
  that skips the whole rebuild is the CLIENT-side `getCanSynchronizeParameters`
  (`parametrized-process-schema-element.js:96-99`) returning false, which needs no schema or
  `schemaUId === parentSchema.uId` — a process calling ITSELF, which the picker already excludes
  (`SubProcessPropertiesPage.getSchemaListFilter` puts `parentSchema.uId` in `ExcludedSchemas`). No normal
  caller takes it. **Do not confuse it with the SERVER-side gate of the same name** — that one has three
  conditions and is documented in `subprocess-schemauid-setter-runs-the-parameter-sync.md` and
  `subprocess-insync-depends-on-the-schema-instance.md`. Same name, different layer, different answer.
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
The MODIFY path always takes the design instance (`ProcessModifyHandler` → `GetDesignInstance`), which is
why its own drift report sees nothing.
The designer's card and the re-synchronization's warning list therefore report health while describe does
not - the card because the code is not on the parameter row, the warning list because its load already
converged. From CrtProcessBuilder 1.6.3.7 a re-synchronization does report the CONSEQUENCE of a DROPPED
parameter — the references left bound to a parameter UId the element no longer carries — the half a caller can
act on. A CODE rename produces no consequence to find: the mapping row keeps the UId, so every reference stays
resolvable and only the saved NAME is stale. Two of the three surfaces are blind to it — the designer's
card because the parameter row has no code cell, the re-synchronization because its load converged first
— and `describe` is the one that is not, under the conditions the companion record states. A person who
suspects a problem, opens the caller and sees a correct card closes it reassured while the process keeps
delivering an empty parameter on every run, which is worse than a visibly stale name would have been; the
read that would have told them is the one nobody thinks to run, which is why the rule below is
procedural.

The two hazards are in DIFFERENT states, and an earlier revision of this paragraph fused them into one
sentence. Keep them apart. A card that looks CORRECT — mapping shown — is by the mechanism above one where
the re-attachment SUCCEEDED; nothing was lost. The hazard there is the false reassurance, nothing else.
The write hazard is the OTHER state, the EMPTY row, where the re-attachment failed — and there the person
is not reassured at all. It is not a hazard of SAVING, which is the trap an earlier revision described and
which does not exist: the card's only button commits on close, so the loss is booked the moment the card
opens and cannot be declined. Nothing is "written back"; the ABSENCE is what survives.

The rule is therefore procedural, not diagnostic: after any change to a called process's parameters,
re-save every caller. Any `setElement` touching the element re-synchronizes and persists it, and
`subProcess: {resync: true}` asks for that and nothing else. Do it because the callee changed, not
because something looked wrong. "Re-save" means that API call — **not** opening the caller in the designer,
which is the act this record warns about: it re-derives on live objects and commits when you close it.

Detecting it after the fact needs the STORED metadata — the `SysSchema` body before a design-time load
touches it — which is the same surface a real drift report would need. See DQ-10 and DQ-25 in
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-deferred-questions.md`.
