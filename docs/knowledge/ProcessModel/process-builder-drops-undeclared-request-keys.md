---
description: A CrtProcessBuilder before 1.6.6.136 drops any request key its contract does not declare (a filter nested inside readData saved green and every Read data read the first record); 1.6.6.136+ refuses it server-side, and clio still refuses such keys before sending, from key lists held equal to the bundled archive, because it answers first and covers the packages that do not
applies-to:
  - clio/Command/ProcessModel/ProcessBlockKeyCheck.cs
  - clio/CrtProcessBuilder/
ticket: ENG-103085
date: 2026-10-13
---

**What is true** — `ProcessDesignService` binds its request through `DataContractJsonSerializer`. Before
CrtProcessBuilder 1.6.6.136 a member the `[DataContract]` type does not declare is discarded before any server
code runs, so no server-side guard can see it: `{"readData":{"source":"AppRequest","filter":{...}}}` built a Read data with NO filter and the call
answered success. The only refusal the block ever gave a wrong key was indirect - `object` in place of
`source` was stopped by "'readData' requires a 'source'", not because `object` was unknown, so `object` next
to a valid `source` passed. From 1.6.6.136 (ENG-95244) the request contracts keep undeclared keys and the server
refuses them (`crtprocessbuilder-refuses-undeclared-request-keys.md`). `ProcessBlockKeyCheck` still refuses
undeclared element and block keys client-side, on create, modify and save-as-new-version, before the page-facts
check.

**Why it is this way** — clio was the last side that saw the caller's keys. The package fix
(`IExtensibleDataObject` on every request contract plus a refusal when `ExtensionData` is not empty) shipped in
1.6.6.136, but the client check stays: it answers before anything is sent ("Nothing was sent") with where the
key belongs, and it covers a host that reports the server check unavailable. Since ENG-92710's follow-up the
write commands require at least 1.6.6.150 (1.6.6.153 since ENG-103990, 1.6.6.162 since ENG-105557, 1.6.6.174 since ENG-96034), so an older package never receives these payloads, and clio no longer warns
about describe read-back members itself: it tolerates them and the server names them. The floor compares numbers
only, so the other package line is kept out only while it stamps lower numbers. The client key lists are a copy of the
server contracts; `ProcessBlockKeyCheckTests` holds every list equal to the `[DataMember]` names of the
bundled archive, and the read-back tolerance (describe-only fields such as `sourceSchemaUId`, `position`)
to members the describe types really emit.

**What breaks if you ignore it** — a rebundle that adds a request member without the matching key-list edit
fails `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`; shipping past it would refuse the new key
on every stand. Removing the check brings back silent drops that save green and misbehave only at run time.
Version skew remains: a clio older than the stand's package refuses a member that package added.
