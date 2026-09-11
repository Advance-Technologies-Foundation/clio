---
description: VwProcessLib carries PackageUId and no package NAME, so versions[].packageName comes from a separate unfiltered SysPackage query - an ATF collection Contains cannot express the filter and would raise ExpressionConvertException, which the process-library guard deliberately does not catch
applies-to:
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
  - clio/Command/ProcessModel/ProcessLibRead.cs
  - clio/CreatioModel/VwProcessLib.cs
ticket: ENG-94374
date: 2026-09-11
---

**What is true** — the process-library view reports which package a version lives in as a **UId and
nothing else**. There is no name column on `VwProcessLib`, and the model maps every column the view
has. So `versions[].packageName` cannot come from the family read: it is a second query, against
`SysPackage`, joined in memory on `PackageUId`.

Measured on a live stand (`creatio_2`, core 10.2.20, CrtProcessBuilder 1.6.1.9, 2026-09-11): the stock
`InvoiceVisaProcess` family reports both members in package `Invoice`, resolved from the single UId
`e7a2c20a-9591-484e-bf77-5953f2dc592e` the view returns for each of them, with no `versionReadWarning`.

**That query is deliberately unfiltered.** The filter that belongs on it — "the package UIds this
family uses" — needs a collection `Contains` inside the ATF expression tree. `ProcessLibRead`'s own
docblock records the measurement: a call-site LINQ expression ATF cannot convert raises
`ATF.Repository.Exceptions.ExpressionConvertException`, and that type is **absent from the guard's
catch ladder on purpose** — it means the query is wrong, not that a fact could not be established. So
an unsupported filter would not degrade, it would escape the reader and fail the whole describe. The
alternative, one `FirstOrDefault` per distinct UId, is bounded only by `FamilyCap`: fifty round-trips
on a cross-package family, inside a read that already owns a ten-second budget. One query returning
the package table — a few hundred rows of scalars — is the cheaper of the three.

**Why it is this way** — `VwProcessLib` exists to drive the process library section, which renders the
package through a lookup the UI resolves for itself; nothing on the server side ever needed the name
in the same row. The substitute provider replays a canned set whatever the filter says, so no test in
this repository can tell a pushed-down filter from an absent one either — which is why the in-memory
join is the part that decides, and the part that is tested.

**What breaks if you ignore it**

- **Dropping the second query** puts the answer back where manual testing found it on ENG-94374: a
  builder who asks which package a version lives in reads `lives in package
  a00051f4-cde3-4f3f-b08e-c5ad1a5c735a`. The fact is present and unusable.
- **Pushing the UId set into the query** to "save bandwidth" turns a degraded read into a failed
  describe, and it fails only against a real environment — every test here stays green, because the
  mock never evaluates the expression.
- **Reading an absent `packageName` as "no package"** is wrong in both directions: it means the name
  could not be resolved, never that the version is packageless. `packageUId` stays the authority and is
  always present. Only a package read that FAILED outright raises a warning, because that is the case
  where every member loses its name at once; a single UId with no row stays quiet, so that a cosmetic
  gap never makes a caller report the version standing as unknown.
