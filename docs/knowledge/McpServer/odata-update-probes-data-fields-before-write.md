---
description: Creatio's OData v4 endpoint accepts a PATCH body that names properties the entity type does not have and returns an empty 204-like body without writing anything - odata-update therefore probes every data field against the record ($select=Id,<fields>) before the write, and any unverifiable field fails the call
applies-to:
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataFieldValidation.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Command/McpServer/Tools/ODataResponseError.cs
ticket: GH-1212
date: 2026-08-27
---

**What is true** — a PATCH whose body names a property the OData type does not have is
accepted by Creatio's OData v4 endpoint: it answers with an empty (204-like) body and
**writes nothing**. An external report (GitHub #1212) demonstrated five consecutive
`odata-update` calls with a `Color` column (absent from `$metadata`) and a fully
nonexistent column both returning `success: true` while leaving the target empty — silent
data loss, made worse by the asymmetry that `odata-read` `$select` rejects the same property
names strictly. `ODataUpdateTool` therefore runs `ODataFieldValidation.ValidateDataFields`
before every PATCH: a single-record GET with `$select=Id,<fields>` reuses the service's own
`$select` validation, so `success: true` from `odata-update` now actually means the supplied
fields were written.

**Why it is this way** — the transport contract ("return the body, don't interpret it")
already forced `ValidateWriteResponse` for the body that comes *back*
(`odata-write-transport-never-throws-on-non-2xx.md`); this is the mirror for the payload that
goes *out*: the service's own strictness toward `$select` is the only pre-write signal
available without maintaining a local copy of the entity model. Three deliberate semantics in
`ODataFieldValidation`: (1) a recognized unknown-property fault ("Could not find a property
named 'X' on type 'Y'") names the offending fields and fails the call — because the service
reports only the FIRST unknown property, each remaining field is re-probed individually so
the caller gets every bad name in one round trip; (2) an empty or non-JSON probe body means
the fields are UNVERIFIED, which fails the call the same way — "cannot confirm" must never
degrade into "proceed and report success"; (3) any other recognized probe error (record not
found, unregistered entity) is surfaced verbatim, not guessed at. Malformed field names
violate the same character rules `odata-read` applies to filter fields
(`ODataKeyFormatter.IsValidMemberPath`) and are rejected locally before any remote call.

**What breaks if you ignore it** — any relaxation of the probe (skipping it on "known-safe"
fields, treating an empty probe as success, de-duplicating the unknown-field follow-up probes
back into a single batch, or moving validation to after the PATCH) reintroduces the exact
failure of #1212: the agent believes a write landed that never happened. The probe costs one
extra GET per update; that is the price of a truthful `success` flag and must stay mandatory.
Note the probe is specific to `odata-update`'s arbitrary field set: `odata-create` shapes its
body from the entity type, and `odata-delete` sends no body, so neither needs it.
