---
description: a process schema's Version property is stamped, not derived from family membership - a schema with SysSchema.ParentId NULL can carry Version 2, so "version 0" implies "root" but "root" does not imply "version 0", and only the family size settles whether a process has versions
applies-to:
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/CreatioModel/VwProcessLib.cs
ticket: ENG-94374
date: 2026-09-03
---

**What is true** — `SysSchemaProperty.Version` on a process schema is a value someone wrote, not one the
platform computes from the version family. Measured on a stock stand (Creatio core 10.1.448.0,
2026-09-03) over every `ProcessSchemaManager` schema:

- `BulkDuplicatesSearchProcess` carries `Version = 2` and `OrderApprovalBaseSubprocess` carries
  `Version = 1`, while **both have `SysSchema.ParentId = NULL`** — they are versions of nothing, and
  `VwProcessLib` computes their `VersionParentUId` as their own `UId`, i.e. reports them as their own
  family root.
- The converse never occurs: **no** schema with a non-NULL `ParentId` carries `Version = 0` (zero rows).

So the implication is one-directional. `Version = 0` does mean "this schema is a family root". "This
schema is a family root" does **not** mean `Version = 0`. Whether a process HAS versions is settled by
the size of its family — the rows sharing its `VersionParentUId` — and never by its own number.

Two neighbouring facts from the same measurement, because they change what a family read means:

- `VwProcessLib` is not a census of versioned processes. 18 version schemas exist in `SysSchema` and 14
  are visible in the view. For `InvoiceVisaBaseSubprocess`, `ContractVisaBaseSubprocess` and
  `OrderVisaBaseSubprocess` the ROOT is absent from the view as well, so a caption or UId read of them
  answers "no row for schema" rather than returning a short family.
- A version does not inherit the root's package: 3 of the 14 visible families are cross-package
  (`CrtEmailMarketingApp` → `CrtEmailDesignerInEmailMarketing`, `OpportunityManagement` → `PRMPortal`,
  `CrtTouchPoint` → `CrtWebTrackingBase`).

**Why it is this way** — versioning was retrofitted onto a schema table that already had a `Version`
property, and package authors write schema metadata directly: a schema authored by copying a versioned
original keeps the number it was copied with. Nothing validates the number against `ParentId`, because
nothing needs to — the runtime resolves the active version through the family, not through the number.

**What breaks if you ignore it** — the failure is a confident wrong answer, which is the whole failure
class this ticket exists to remove. Reading `version != 0` as "this is a version of something" makes
`BulkDuplicatesSearchProcess` look like a family member and sends a caller hunting for a root that does
not exist; reading `version == 0` as "this process has no versions" is the mirror error and was shipped
in the first draft of the `process-versions` guidance article before this measurement caught it. Both
are invisible at review time, because the stock stand contains exactly the rows that make the wrong rule
look right on the family everyone tests with (`InvoiceVisaProcess`, whose root really is 0).
