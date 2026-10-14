---
description: administratedByRecords on/off is fast and touches no record - ON adds no default rule and gives EXISTING records no rights rows (only author/owner, managers, "view any data" reach them); OFF keeps the rules and the Sys<Entity>Right rows, which come back on re-enable; a record inserted while OFF gets no rows; ON with NO rule = every user sees only their own records
applies-to:
  - clio/Command/ObjectRights/SetDefaultRecordRightsCommand.cs
  - clio/Command/ObjectRights/GetObjectRightsCommand.cs
ticket: ENG-100406
date: 2026-10-04
---

**What is true** — turning "Use record permissions" on through `SaveAdministratedObject` (`administratedByRecords:
true`, rules `null`) takes ~7 s the first time (it creates the record-rights storage) and ~2 s later. It adds no rule
and writes no rights row: a record that existed before has no `Sys<Entity>Right` rows, so only its author/owner, their
managers and holders of "view any data" reach it until the rules are applied (`ObjectRecordRightsActualizationProcess`).
With NO rule at all, a new record gets only the built-in author right: every user sees only the records they create.
Turning it off keeps the rules and every rights row; they apply again after a re-enable. A record inserted while it is
off gets no rights rows. Rules apply to a NEW record on insert (a level 0 writes no row). Verified on 10.2.367 /
10.2.370.

**Why it is this way** — the switch only decides whether record rights are evaluated; filling them for existing
records is the separate, heavy actualization run, which the Freedom UI asks the user about instead of running it.

**What breaks if you ignore it** — reporting "record permissions are on, the rules apply" after an enable hides that
existing records just became invisible to almost everyone; turning it off and on again silently revives old rules and
rows. So `set-default-record-rights` states these facts in its output with the record count, and never applies the
rules itself.
