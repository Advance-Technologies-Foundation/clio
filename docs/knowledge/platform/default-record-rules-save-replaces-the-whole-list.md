---
description: SaveAdministratedObject REPLACES the whole entitySchemaRecordDefRights list (a rule left out is deleted, null leaves it alone) and validates nothing - an all-zero rule is dropped, a duplicate author+grantee keeps only the LAST rule, a level 3 is stored - always answering success:true
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/DefaultRecordRightsPlanner.cs
ticket: ENG-100406
date: 2026-10-04
---

**What is true** — the default record rules of an object (`entitySchemaRecordDefRights`, one rule per author +
grantee pair, levels 0 not set / 1 granted / 2 delegated, no id and no position) are saved as ONE collection:
- a rule missing from the list is deleted; `null` leaves the stored rules untouched;
- the server checks nothing and still answers `success: true`: a rule with all three levels 0 is dropped without a
  word, two rules for one pair keep only the LAST one (an AE→AE `1/1/0` sent before `2/0/0` lost edit), and a level
  outside 0..2 (3) is stored as is.
Checked on 10.2.367 (2026-10-02) and 10.2.370 (2026-10-04). `DataService` cannot read the storage
(`SysEntitySchemaRecordDefRight` is virtual), so the service is the only API.

**Why it is this way** — the designer page owns the whole grid and saves it whole; it refuses duplicates itself, so the
server never needed to.

**What breaks if you ignore it** — send only the changed rule and every other rule of the object is deleted. Send back
a stored list that already holds a duplicate pair or a bad level, and rights are lost or kept invalid silently. So
`set-default-record-rights` sends the FULL list only when its plan changes a rule (else `null`), and its planner
refuses a list with duplicate pairs or invalid levels whenever it would send it.
