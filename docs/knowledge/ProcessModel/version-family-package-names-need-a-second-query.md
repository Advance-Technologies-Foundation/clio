---
description: VwProcessLib carries PackageUId and no package NAME, so versions[].packageName comes from a separate unfiltered SysPackage query - an ATF collection Contains cannot express the filter and would raise ExpressionConvertException, which the process-library guard deliberately does not catch
applies-to:
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio/CreatioModel/VwProcessLib.cs
ticket: ENG-94374
date: 2026-09-11
---

**What is true** — the process-library view reports which package a version lives in as a **UId and
nothing else**. There is no name column on `VwProcessLib`, and the model maps every column the view
has. So `versions[].packageName` cannot come from the family read: it is a second query, against
`SysPackage`, joined in memory on `PackageUId` and projected onto the wire in `IProcessDescriber`.

**That query is deliberately unfiltered.** The filter that belongs here — "the UIds this family uses" —
needs a collection `Contains` inside the ATF expression tree. `ProcessLibRead`'s own docblock records
the measurement: a call-site LINQ expression ATF cannot convert raises
`ATF.Repository.Exceptions.ExpressionConvertException`, and that type is **absent from the guard's
catch ladder on purpose** — it means the query is wrong, not that a fact could not be established. So
an unsupported filter would not degrade, it would escape the reader and fail the whole describe. The
alternative, one `FirstOrDefault` per distinct UId, is bounded only by `FamilyCap`: fifty round-trips
on a cross-package family. One query returning the package table — a few hundred rows of scalars — is
the cheapest of the three.

**Why it is this way** — `VwProcessLib` exists to drive the process library section, which renders the
package through a lookup the UI resolves for itself; nothing server-side ever needed the name in the
same row. The substitute provider replays a canned set whatever the filter says, so no test here can
tell a pushed-down filter from an absent one — which is why the in-memory join is the part that
decides, and the part that is tested.

**What breaks if you ignore it**

- **Dropping the second query** puts the answer back where manual testing found it: a builder who asks
  which package a version lives in reads `lives in package a00051f4-cde3-4f3f-b08e-c5ad1a5c735a`.
- **Pushing the UId set into the query** turns a degraded read into a failed describe, and it fails
  only against a real environment — every test here stays green, because the mock never evaluates the
  expression.
- **Reading an absent `packageName` as "no package"** is wrong in both directions: it means the name
  could not be resolved. `packageUId` stays the authority and is always present. See
  [a-zero-row-syspackage-read-is-a-refusal-not-an-answer](a-zero-row-syspackage-read-is-a-refusal-not-an-answer.md)
  for which absences are announced and which are silent.
