---
description: SaveAdministratedObject REPLACES the whole entitySchemaOperationsRights list - a row left out of the array is deleted, and a row sent with its id at a new position is moved (id kept); the Object permissions designer deletes and drags rows exactly this way and renumbers every position 0..n-1, so set-object-rights --remove-row / --position / --above send the full list in the new order
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/ObjectRightsPlanner.cs
ticket: ENG-104066
date: 2026-10-25
---

**What is true** — `RightManagementService.svc/SaveAdministratedObject` treats `entitySchemaOperationsRights` as
the object's complete row list. Probed on stand `sae_m_seeenu_16229277_1024` (2026-10-25) with three rows
`[0] All employees, [1] System administrators, [2] All external users`, saving the read object back with the
other three collections null:

| Sent | Read back |
|---|---|
| the same three nodes, reordered `[SA, AE, EXT]`, positions rewritten 0..2 | the new order; every row keeps its `id` |
| the two nodes `[SA, AE]` only | the `All external users` row is gone |

The Freedom designer does the same (creatio-ui `lib.studio-enterprise.administrated-object`,
`rights-by-operations-detail.component.ts`): a delete splices the row out of the array, a drag uses
`moveItemInArray`, and both then set every row's `position` to its index before the whole object is saved.
A `null` collection still means "leave untouched" — only an ARRAY replaces.

**Why it is this way** — the service has no per-row endpoint; the list sent is the list stored.

**What breaks if you ignore it** — a move written through the row-matching save (`IObjectRightsWriter.Save`,
which finds a changed row by grantee AND position) finds no read row at the new position and appends the row
as a NEW id-less row: the role ends up with two rows, after which every later grant to it is refused as a
duplicate. And a writer that "drops" a row by leaving its flags cleared but keeps the node turns a removal into
a deny, which narrows access where the caller asked to widen it. That is why the layout save
(`SaveRowLayout`) rebuilds the array from the read nodes in the planned order, and why the read-back after it
treats every row as written.
