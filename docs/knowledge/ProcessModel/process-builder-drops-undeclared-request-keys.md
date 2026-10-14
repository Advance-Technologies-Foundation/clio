---
description: CrtProcessBuilder reads BuildProcess / ModifyProcess requests through WCF's DataContractJsonSerializer, which drops any key its contract does not declare - a filter nested inside readData saved green and every Read data read the first record; clio refuses such keys before sending, from key lists held equal to the bundled archive
applies-to:
  - clio/Command/ProcessModel/ProcessBlockKeyCheck.cs
  - clio/CrtProcessBuilder/
ticket: ENG-103085
date: 2026-10-13
---

**What is true** — `ProcessDesignService` binds its request through `DataContractJsonSerializer`. A member the
`[DataContract]` type does not declare is discarded before any server code runs, so no server-side guard can
see it: `{"readData":{"source":"AppRequest","filter":{...}}}` built a Read data with NO filter and the call
answered success. The only refusal the block ever gave a wrong key was indirect - `object` in place of
`source` was stopped by "'readData' requires a 'source'", not because `object` was unknown, so `object` next
to a valid `source` passed. `ProcessBlockKeyCheck` refuses undeclared element and block keys client-side, on
create, modify and save-as-new-version, before the page-facts check.

**Why it is this way** — clio is the last side that still sees the caller's keys, and fixing it in the package
(for example `IExtensibleDataObject` on every request contract plus a refusal when `ExtensionData` is not
empty) needs a rebundle and only protects stands that install it. The client key lists are a copy of the
server contracts; `ProcessBlockKeyCheckTests` holds every list equal to the `[DataMember]` names of the
bundled archive, and the read-back tolerance (describe-only fields such as `sourceSchemaUId`, `position`)
to members the describe types really emit.

**What breaks if you ignore it** — a rebundle that adds a request member without the matching key-list edit
fails `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`; shipping past it would refuse the new key
on every stand. Removing the check brings back silent drops that save green and misbehave only at run time.
Version skew remains: a clio older than the stand's package refuses a member that package added.
