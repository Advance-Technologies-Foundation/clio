---
description: process IsTracing is a SysSchemaUserProperty row on the version family root, not schema metadata; off stays stored as False; SysSchemaUserProperty is not reachable through ESQ/DataService
applies-to:
  - clio/Command/ProcessModel/IProcessDescriber.cs
  - clio.mcp.e2e/ProcessTracingToolE2ETests.cs
ticket: ENG-102111
date: 2026-10-09
---

**What is true** — the "Enable tracing" switch of a business process (`BaseProcessSchema.IsTracing`) is a platform
USER property: a `SysSchemaUserProperty` row (Name `IsTracing`) keyed by the SysSchema.Id of the version family's
ROOT, where the Process Library record page writes it. It is not in the schema metadata, so no schema save sets or
clears it; and since no packaging code in the platform source reads `SysSchemaUserProperty`, an exported package does
not carry it either (source-read, not measured). The runtime reads the running schema, its calling process and
the family root, so the root's switch traces every version. Switching it off writes `False` and keeps the row
(measured 2026-10-09 on d_krestov_n, .NET Framework); the platform's daily `ProcessTracingDisablerJob` writes the same
`False` once `ProcessParameterTracingDisableTimeoutDays` days have passed since the switch-on (0 = never). The trace
itself lands in `SysPrcElementTraceLog`, two rows per executed task (events write none).

**Why it is this way** — the platform models tracing as an environment-local, temporary diagnostic, not as part of the
process definition. clio reaches it only through CrtProcessBuilder (1.6.6.92+): `isTracing` on create, `setTracing`
on modify, the `tracing` block on describe.

**What breaks if you ignore it** — a check that reads the switch out of `describe`'s graph metadata, a schema export or
`SysSchema` finds nothing and reports every process as untraced. `SysSchemaUserProperty` is a SYSTEM entity, so
`execute-esq` answers "Item with name SysSchemaUserProperty not found" - which reads like a missing table; query it
with `clio execute-sql-script` instead. And a verification that expects the row to disappear on switch-off waits
forever.
