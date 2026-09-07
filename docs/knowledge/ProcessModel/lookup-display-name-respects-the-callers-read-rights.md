---
description: valueDisplay on a lookup constant is resolved server-side through the platform's rights-aware entity read (Entity.FetchFromDB -> EntitySchemaQuery with UseAdminRights), so a record the caller may not read yields NO display name while the id is still accepted - describe/modify cannot be used as a name oracle, and the existence check stays a raw Select on purpose
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
ticket: ENG-96325
date: 2026-09-02
---

**What is true** - `valueDisplay` (and the `DisplayValue` the designer renders) for a Lookup constant is
produced by `CrtProcessBuilder`, not by clio, and it is produced through the platform's own entity read:
`referenceSchema.CreateEntity(uc).FetchFromDB(recordId, useDisplayValues: false)` followed by
`PrimaryDisplayColumnValue`. Underneath, `InternalFetchFromDB` builds an `EntitySchemaQuery` with
`UseAdminRights` (the entity default, `true`) and the caller's `LocalizationCultureId`. In this API
`UseAdminRights = true` means the caller's row-level rights ARE applied (`AddRightCondition` adds the
rights subquery only when it is true; the platform's own doc says "whether rights are taken into
account"). Two consequences, both verified against the platform source:

- a record the calling profile cannot read comes back as NO row: the name is not resolved, `DisplayValue`
  is left unset, `valueDisplay` is absent - and the VALUE is still accepted, because existence is decided
  separately by a raw `Select` that ignores row rights
- the name arrives in the caller's culture with a fallback to the primary language

clio only surfaces the field; it performs no read of its own.

**Why it is this way** - the existence guard and the name read must be two different engines. Existence
on the rights-aware read would report a record the writer cannot see as absent and REFUSE a valid id;
the name on a raw `Select` would hand any caller holding `CanManageProcessDesign` the display name of any
record of any entity by id - an admin-rights name oracle over caller-controlled entity + id. The first
cut of ENG-96325 did exactly that with a raw `Select` display column; it was replaced before review and
never shipped in a released archive (at the 1.3.1.1 lineage the validator reads only `.Column("Id")`).

**What breaks if you ignore it** - "simplifying" the two reads into one, in either direction, is a
regression that no unit test in either repository catches, because the unit harness has no data access
for the entity read: fold existence into the rights-aware read and valid ids start being refused for
callers with narrow rights; fold the name into the raw `Select` and the oracle is back. The
"exists but unreadable" contract is pinned hermetically in the package
(`ApplyMapping_ShouldAcceptAndLeaveDisplayValueUnset_WhenRecordExistsButNameIsUnreadable`), but only
because the harness's entity read fails - it cannot model a real rights denial, and neither can the clio
E2E suite, which runs as Supervisor. Do not read the absence of an E2E case for a rights-restricted
record as an untested gap; read it as a limit of both harnesses, with the guarantee resting on the
platform's `UseAdminRights` default and this record. If a restricted-profile E2E lane ever exists, the
case to add is: a Guid of a record the profile cannot read -> the write succeeds, `valueDisplay` is
absent.
