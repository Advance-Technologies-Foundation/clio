---
description: Opening a sub-process element's designer card runs synchronizeActualSchemaParameters - clearParameters() on the LIVE parent schema, then re-attach each stored mapping through findParameterByNameOrByUId, which is NAME-keyed because every re-derived copy gets a fresh GUID. A value that finds no name match is gone, and the card's only button commits on close (onHidePropertyPage calls saveElementProperties first). There is no Cancel.
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-92707-sub-process-element/
ticket: ENG-92707
date: 2026-09-21
---

**What is true** — opening the properties card of a sub-process element is not a read. It rebuilds the
element's parameters from the callee and re-attaches the stored mapping values BY NAME, on the live
schema, and there is no way to decline the result.

**The carry-over is keyed on NAME.** `SubProcessPropertiesPage.synchronizeActualSchemaParameters`
snapshots the element's stored parameters, re-derives them from the callee, then re-attaches each stored
value through `findParameterByNameOrByUId` (`process-activity-schema.js:519-521`) =
`findParameterByName(name) || findParameterByUId(uId)`. The UId branch CANNOT match a RENAMED parameter:
every re-derived copy gets a fresh GUID — `synchronizeParameter` → `createElementParameter`
(`parametrized-process-schema-element.js:172-177`), which calls `schemaParameter.clone()` and
`_prepareClonedParameter` as SIBLINGS (`:173`, `:174`; the GUID is assigned in the latter, `:142-149`) —
and for a sub-process `ProcessSubprocessSchema.createElementParameter` (`process-subprocess-schema.js:208-211`)
additionally calls `clearSourceValue()`.

The reasoning needs one more step than "all UIds are fresh": `_synchronizeSchemaParameter` writes old UIds
BACK into the live collection as it iterates. But it only does so for a parameter that already matched **by
name** in its own pass, and collection keys are unique, so a renamed parameter's old UId is still never
present at lookup time. Name is the only key that can reach it.

**There is no fallback.** `_synchronizeSchemaParameter` (`RootUserTaskPropertiesPage.js:631-655`, inherited
by `SubProcessPropertiesPage`) opens with
`if (!newParameter || newParameter.dataValueType !== oldParameter.dataValueType) { return; }` — no
positional match, no second pass, no preservation of the unmatched original.

**A name match is necessary but NOT sufficient — four ways to lose the value**, since
`setMappingValue(oldValue)` is the only thing that puts one back after the derivation stripped it:

1. no name match (the rename case);
2. a changed `dataValueType` under an unchanged name;
3. `getCanAssignParameterSourceValue` returning false (`RootUserTaskPropertiesPage.js:645-650`) — NOT a
   standalone mode: for a sub-process the predicate is
   `AssignableProcessSchemaParameterDirections.includes(parameter.direction)` = `[IN, VARIABLE]`
   (`parametrized-process-schema-element.js:203-205`), so if the direction did not change there was no
   assignable mapping to lose. It bites when the callee changed the DIRECTION to a non-assignable one
   under an unchanged name and type;
4. **nested parameters, environment-gated.** The snapshot is `getRootParameters()` — roots only — and
   nested `itemProperties` values are re-attached solely by `_synchronizeNestedParameters`
   (`RootUserTaskPropertiesPage.js:661-671`), whose first statement is
   `if (!Terrasoft.Features.getIsEnabled("ManageProcessCollectionParameters")) { return; }`. With that
   feature OFF, every stored value on a nested/collection-item parameter is dropped on card open
   REGARDLESS of name match.

**OPENING is the destructive act, and closing does not undo it.** The re-derivation begins with
`clearParameters()` (`parametrized-process-schema-element.js:427-434`), which removes the element's rows
from the parent schema's `mappings` before rebuilding them. This happens on the LIVE objects: the page
takes the element by reference (`BaseProcessSchemaElementPropertiesPage.js:393`,
`this.set("ProcessElement", element)`; there is no `clone` anywhere in that file), so nothing snapshots it
and nothing rolls it back. The card has exactly ONE button — `ClosePropertyButton` (`:1044-1053`) — bound
to `onHidePropertyPage`, whose FIRST statement is `this.saveElementProperties()`
(`process-schema-designer-view-model.js:224-231`); ESC takes the same path (`:959`). **There is no Cancel
and no separate Save: closing COMMITS.**

**Where this stops being traced.** Whether the emptied mapping then reaches the SERVER depends on a
subsequent designer schema save, and that serialization step is NOT traced here. Treat "it gets persisted"
as INFERENCE. What source establishes is the in-memory mutation and the commit-on-close.

The one case that skips the rebuild is the CLIENT-side `getCanSynchronizeParameters`
(`parametrized-process-schema-element.js:96-99`) returning false, which needs no schema or
`schemaUId === parentSchema.uId` — a process calling ITSELF, which the picker already excludes
(`SubProcessPropertiesPage.getSchemaListFilter` puts `parentSchema.uId` in `ExcludedSchemas`). No normal
caller takes it. **Do not confuse it with the SERVER-side gate of the same name** — that one has three
conditions, in `subprocess-schemauid-setter-runs-the-parameter-sync.md` and
`subprocess-insync-depends-on-the-schema-instance.md`. Same name, different layer, different answer.

**Why it is this way** — the element's parameters are a copy derived from the callee, and the element's
parameter UIds are freshly generated and never equal the callee's, so a UId-keyed re-attachment was never
available to the card. Name is the only thing the two sides share, which is the same constraint the
runtime works under (`subprocess-runtime-binds-by-parameter-name.md`).

**What breaks if you ignore it** — a person who suspects drift and opens the caller to look has already
booked the loss, and cannot decline it by closing. Every mapping whose parameter the callee renamed,
retyped, redirected — or, with `ManageProcessCollectionParameters` off, every nested one at all — is gone
from the in-memory schema before they have read a single row. The safe way to refresh a caller is the API:
`setElement` re-synchronizes and persists it, and `subProcess: {resync: true}` asks for that and nothing
else. **Opening the designer is not the cheap alternative to it.**

What the card SHOWS in those states, and why a code rename is invisible on the row, is the companion
record: `subprocess-designer-card-hides-stale-state.md`. Its `code only` measurement contradicts the
name-keyed mechanism above — the row read healthy where this mechanism predicts empty — and **no
explanation is established for that gap**. It needs a stand re-run with the callee's client-side schema
known-fresh; do not fill the gap with a guess.

---

**PROVENANCE.** Settled in client source 2026-09-21. This replaces a hypothesis that the card paired
stored to re-derived parameters BY CAPTION, which had been filed as unmeasured and was wrong. An early
revision of the name-keyed write-up then added its own error — "closing the card discards that; saving
from that card persists the removal" — which source refutes in both halves: the card exposes no save
control, and its close button saves. The truth is worse than the retracted version, not milder.
