---
description: The designer card's parameter ROW has no code column - ProcessSchemaParameterViewConfig builds five controls and none renders Name - so a callee CODE rename is invisible there on a CAPTIONED parameter, and inSync is one-directional so a DROPPED parameter is invisible too. What OPENING the card does to the stored mapping is a separate record: subprocess-card-reattaches-mappings-by-name.md.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-21
---

**What is true** — after a called process renames or drops a parameter, the caller's designer card can
still read healthy. Three surfaces disagree about the drift and this record is about the one a person
actually looks at.

* **the parameter ROW carries no code column.** `ProcessSchemaParameterViewConfig.js`, row body
  `item-view` (`:24-164`, its `items` array at `:32-163`), builds five controls: a type icon, a direction
  icon, a label, a tools menu button and a `Terrasoft.MappingEdit`. No VISIBLE control renders `Name` or
  `Id`, and there is no tooltip, hint or title binding. So after a rename that BREAKS runtime delivery the
  row reads as it did before: mapping present, nothing marked, and — **when the parameter has a caption** —
  the same label. **Measured on a stand, 2026-09-17**, at CrtProcessBuilder 1.6.3.10, through a real
  Chrome, on captioned parameters.
* **without a caption the row DOES show the code.** The label is `getDisplayValue()`
  (`ProcessFlowElementPropertiesPage.js:767`) = `getCaption() || getName()` (`base-schema.js:122-124`), so
  an uncaptioned parameter displays its name and a rename changes it. The blindness is conditional.
* **the code is in the DOM even when it is not on screen** — `data-item-marker="esn-notification-item-<name>"`,
  fed by `markerValue: "$MarkerValue"` → `ProcessSchemaParameterViewModel._getMarkerValue` over `Id: name`
  (`ProcessFlowElementPropertiesPage.js:764`). Two bounds: it lands on the row's containers and its two
  image controls but NOT on the label, the tools button or the `MappingEdit`; and it is emitted only when
  `UseMarkerValue` is on (`component.js:824-826` over `GlobalAppSettings.UseMarkerValue`, ships true). A
  human sees nothing; a DOM read or an automated test reads the stale code straight out of the attribute.
  **"Not on screen" and "not in the DOM" are different claims and only the first is true** — which matters,
  because the measurements above were taken through a browser.
* **the code is one click away in the UI too.** The row's label replaces the row IN PLACE with the
  `ProcessSchemaParameterEditModule` inline editor (`MappingEditMixin.js:28-51`, into the row's own
  `item-edit` container — not a separate page), whose schema is `ProcessSchemaParameterEditPage` and which
  renders a populated `Name` field. Disabled, through a chain worth spelling out because the attribute is
  RENAMED in the middle: the row seeds `Enabled: false` (`ProcessFlowElementPropertiesPage.js:776`),
  `getParameterEditInfo` copies it across as `IsEnabled: this.get("Enabled")` (`MappingEditMixin.js:104`),
  and the field binds `enabled` to `IsEnabled` (`ProcessSchemaParameterEditPage.js:761`). Read-only, but
  READ. (The PROCESS-level list is where renaming is meant to happen; it overrides `Enabled` at
  `ProcessSchemaPropertiesPage.js:463`, CONDITIONALLY on
  `useUnlockInheritedProcessParameters || parameter.createdInSchemaUId === this.$ProcessElement.uId`
  (`:451-452`) — an INHERITED parameter stays disabled there too.)
* **`inSync` is ONE-DIRECTIONAL and a DROPPED parameter is invisible to it.** It asks whether every
  parameter the CALLEE declares is present on the element: callee ADDS one → `false`; callee REMOVES one →
  `true`, because the element merely carries an extra. Measured. A code RENAME reads as add-plus-remove
  and does flip it. So `inSync` answers the rename case and says nothing about the drop.
* **`inSync` does not see a caption.** The caller keeps its OWN copy and reports `true` while the callee's
  differs — measured. Only a CODE change flips it, which is the right half to be sensitive to since the
  runtime binds by code; but do not read `inSync: true` as "the element matches the callee".
* the MODIFY path always takes the design instance (`ProcessModifyHandler` then `GetDesignInstance`),
  which converges before the package sees the schema — which is why the re-synchronization's own drift
  report is empty. **Captions are the exception from CrtProcessBuilder 1.6.6.17:** they are compared with the
  caller's STORED `SysLocalizableValue` rows rather than with the converged element, so a resync does name
  a caption the callee changed (ENG-100077, `a-process-resource-cache-survives-its-own-save.md`).
* `describe-business-process` can report the stale name and `inSync: false`, but only for some processes
  and only at some times. WHEN is platform behaviour and is NOT restated here: see
  `subprocess-insync-depends-on-the-schema-instance.md`. **Never compile to expose drift**: on this stand
  a compile is what left the environment not-ready past 600 seconds.

**The measurements.** Every cell carries its provenance, because "measured" is not one bit — it is a
reading, by a particular pass, against a particular build:

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
returns the parameter byte-identical to its healthy baseline — same `uid`, `source`, `value`,
`valueDisplay` — so nothing on disk was lost. What the OPEN CARD has done to it is the other record.

Two cells in the code-only row are **M07**, and a plain **M** hid that: no `describe` and no runtime run
happened in the code-only state during the 1.6.3.10 pass. They are real measurements against an older
build, which is not the same claim as "measured here".

**Markers differ WITHIN a row on purpose** — that is the point of tagging cells rather than rows. Each
cell was taken by whichever pass last read that thing, so `M12` beside `M10` in the caption-only row means
its `inSync` and card were re-read at 1.6.3.12 while the stored-mapping cell keeps its 1.6.3.10 reading.
One cell IS unexplained and is flagged rather than quietly resolved: the 1.6.3.10 sequence on record
(rename code → open designer → read card → rename the caption too) contains no caption-only state, so what
produced that row's **M10** stored-mapping reading is not established. Do not re-tag it from inference —
find the session or re-measure.

**The code-only row contradicts the mechanism in the companion record, and nothing explains the gap.**
Keyed on name, ANY code rename should empty the row — which is row 3, but not row 2, where the card looked
healthy. **No supported mechanism closes it.** The row is a real reading and is NOT rewritten to fit; it
needs a re-run on a stand with the callee's CLIENT-side schema known-fresh. Until then: three readings, a
mechanism that explains two, and **no guess for the third.**

**Why it is this way** — convergence on read is what keeps a design-time instance correct without a
migration step. It was never meant to be an inspection surface, and it is not one.

**What breaks if you ignore it** — the three surfaces do NOT agree, so "I looked and it was fine" is not
evidence. `describe-business-process` reads whatever instance the schema manager already HOLDS
(`ProcessSchemaRepository.LoadForDescribe`) and does not re-converge it, so while that instance predates
the callee's change it reports the STALE name and `inSync: false` — both real evidence. The MODIFY path
takes the design instance, which is why its drift report sees nothing of a rename or a drop (it does see a
caption change from 1.6.6.17, measured against the stored rows). From CrtProcessBuilder 1.6.3.7 a
re-synchronization does report the CONSEQUENCE of a DROPPED parameter — references left bound to a UId the
element no longer carries — the half a caller can act on. A CODE rename produces no consequence to find:
the mapping row keeps the UId, so every reference stays resolvable and only the saved NAME is stale. Two
of the three surfaces are blind to it and `describe` is the one that is not. A person who suspects a
problem, opens the caller and sees a correct card closes it reassured while the process keeps delivering
an empty parameter on every run — and the runtime binds by NAME (`FindScalarParameterByName`), skipping an
unmatched one with no exception and no log line. The read that would have told them is the one nobody
thinks to run.

The rule is procedural, not diagnostic: after any change to a called process's parameters, re-save every
caller. Any `setElement` touching the element re-synchronizes and persists it, and
`subProcess: {resync: true}` asks for that and nothing else. Do it because the callee changed, not because
something looked wrong. **"Re-save" means that API call — not opening the caller in the designer**, which
is destructive in its own right: see `subprocess-card-reattaches-mappings-by-name.md`.

Detecting it after the fact needs the STORED metadata — the `SysSchema` body before a design-time load
touches it. See DQ-10 and DQ-25 in
`spec/eng-92707-sub-process-element/eng-92707-sub-process-element-deferred-questions.md`.

---

**CORRECTED 2026-09-21**, against client and platform source. Four claims this record used to make were
wrong, and the shape of the errors is the reusable part: each was a plausible mechanism asserted without
being traced.

1. *"The card renders the caption and never the code."* Too strong twice over — the label falls back to the
   NAME when a parameter has no caption, and the row's label opens an edit page carrying a populated
   `Name` field. The accurate claim is the narrow one: no code column on the ROW.
2. *"The card pairs the caller's stored parameter to the callee's by CAPTION."* Filed as an unmeasured
   hypothesis. It was not merely unmeasured, it was wrong — see the companion record for the real key.
   The class does expose a `findParameterByCaption` (`parametrized-process-schema-element.js:298-301`),
   which is probably what made it feel plausible, but nothing on that path calls it. Do not re-derive the
   dead guess from that grep hit.
3. Neither *"the empty row is a rendering artefact"* nor, before it, *"a visible signal, not a
   reassurance"*. The stored schema is intact — that measurement stands — but the open card really has dropped
   the mapping.
4. WITHDRAWN, and do not restore it: *"instance freshness is the likely confound"* for the `code only`
   row, citing `subprocess-insync-depends-on-the-schema-instance.md`. That citation was wrong on every
   count. That record is about the CALLER's instance, states that "every row below assumes the CALLEE's own
   read reflects the change", names COMPILATION rather than caching as the only stale-callee route, records
   that saving EVICTS (`SchemaManager.cs:2311,2315,2317`), and describes the .NET schema manager while the
   card re-derives client-side in JS. Wrong side, wrong layer, wrong mechanism. It also cuts the other way:
   a stale callee there reports `inSync: true`, while the `code only` row's `inSync` is `false` — though at
   **M07** off a different build that settles nothing either. Do not restore it, and do not replace it with
   a fourth guess.
