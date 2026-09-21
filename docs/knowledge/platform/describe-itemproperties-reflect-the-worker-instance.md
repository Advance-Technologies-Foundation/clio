---
description: A described itemProperties reports what the schema instance the IIS worker is holding carries, not what the environment stores - the same stand, the same package version and the same clio answered with them and without them on two different worker processes, so a reading taken just after a package push is not evidence about the product
applies-to:
  - clio/Command/ProcessModel/
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - spec/eng-99856-multi-instance/
ticket: ENG-99856
date: 2026-09-21
---

**What is true** — `describe-business-process` reports a parameter's `itemProperties` off the
`ProcessSchema` instance the environment's worker process is holding at that moment. That instance is
cached for the worker's lifetime, and it is not guaranteed to agree with the row in `SysSchema.MetaData`.

Measured twice against the same stand, at the same `CrtProcessBuilder` version (1.6.3.31), with clio built
from the same describe path, on the shipped fixture `ExpireLicenseNotificationProcess`:

- **2026-09-21, earlier:** `itemProperties` on **no** parameter of any element, and none on the
  process-level `CheckedLicenses` either. Taken minutes after the package was pushed 1.6.3.14 → 1.6.3.31.
- **2026-09-21, later**, on a worker process started by an idle timeout whose whole request history was
  read-only: `itemProperties` on all five parameters that carry them — `SubProcess2`'s
  `InputRecordCollection` **4** and `OutputRecordCollection` **6**,
  `PushExpiredLicensesNotificationSubProcess` 10 and 10, `CheckedLicenses` 4.

The stored blob was read directly out of the environment's own database in the same session and carries
exactly those counts, so the environment never disagreed with the shipped metadata. Only the served answer
did. A second describe in the same worker, 85 seconds later, was identical — so describe does **not**
flatten its own cache, and the instability is strictly *between* worker processes, never within one.

**Why it is this way** — the reason a particular worker held a stripped instance is **not established**,
and two candidates commonly reached for do not fit.

The activity rebuild does not fit, on SCOPE. `SynchronizeParametersInternal`
(`Terrasoft.Core/Process/ProcessSchemaActivity.cs:373-395`, reached from
`ProcessSchemaSubProcess.SchemaUId`'s setter) acts only on an ACTIVITY's parameters, so it cannot explain a
reading in which the PROCESS-level `CheckedLicenses` was empty too. It is also weaker than it is usually
described: `:387-388` clear the `ItemProperties` of CLONES taken at `:378-379`, and `:390`
`FillCollectionParameters` refills them — what mutates the cached graph in place is `Parameters.Clear()` at
`:385` and `:391`. The collections come back empty only if the diff at `:389` left `Parameters` empty,
which is what happens when the callee does not resolve.

A COMPILED instance fits the shape perfectly — the generators strip `ItemProperties` from every parameter
initializer they emit (`ProcessSchemaGenerator.cs:1637`, `:1725`, `:1783`,
`ProcessSchemaGeneratorNew.cs:1852`, `:2057`) while leaving `SourceValue` alone — and is ruled out by one
field only. `MultiInstanceOptions` is `[MetaTypeProperty]`, writable, and NOT in the generator's deny-list
(`WriteSchemaContainer`, `ProcessSchemaGenerator.cs:1515-1516`), and `GenerateProperties` emits
reflectively over `GetProperties()` — so "the generator source never names it" proves nothing. What does:
`GenerateValue` falls through to `value.ToString()` for an unhandled type (`GeneratorUtilities.cs:490`) and
`ProcessSchemaMultiInstanceOptions` has no `ToString` override, so a generated schema carrying one would
not compile. A compiled instance therefore cannot answer `multiInstance: true`, and that reading did.

The best-scoped candidate nobody has checked is
`Terrasoft.Core.ServiceModel/Designers/Mappers/DtoToSchema/SchemaParametersDtoApplier.cs:104-107`:
`ApplyNestedParameters` calls `parameter.ItemProperties.Clear()` whenever the incoming DTO carries no
nested parameters, generically over parameter kind — so it reaches a process-level parameter, which is the
scope the evidence demands. A second, unproven suspicion is a worker still serving or reloading a
configuration from before a package push: an install does not by itself replace the assembly a running
`.NET Framework` worker has loaded, and `list-packages` reads the database row, not the serving assembly.

**What breaks if you ignore it** — you conclude that clio cannot report a callee's contract, and you scope
work to add what already ships. That is exactly what happened: a research pass recorded "describe returns
no `itemProperties`" as a platform fact with a full source derivation behind it, an ADR eliminated three
candidate causes around it, and a contingent `calleeContract` wire member was designed as the fallback —
all from one reading taken on a worker nobody had characterised. A whole story existed to diagnose it.

So: before quoting a describe reading as evidence about the product, know which worker answered it.

```powershell
Get-Process -Name w3wp | Select-Object Id, StartTime
```

A reading taken minutes after a `push-pkg`, a compile, or any other operation that reloads the
configuration is a reading about that worker's transient state. Re-take it on a worker whose history you
know, and say which one you asked. The stored bytes are the tie-breaker and are cheap to read: the
`SysSchema.MetaData` column is **plain JSON, not compressed** — it is handed straight to the metadata
serializer — so `SELECT MetaData FROM SysSchema WHERE Name = '<schema>'` and parse it.
