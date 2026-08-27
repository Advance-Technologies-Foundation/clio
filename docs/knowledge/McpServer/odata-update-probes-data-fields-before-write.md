---
description: Creatio's OData v4 endpoint accepts a PATCH naming properties the entity type does not have and answers an empty 204-like body without writing anything - odata-update pre-validates every data field against the entity's $metadata CSDL (with a $select probe fallback), and rejects lookup fields set to the empty GUID
applies-to:
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataFieldValidation.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio.tests/Command/McpServer/ODataUpdateToolTests.cs
ticket: GH-1212
date: 2026-08-27
---

**What is true** — a PATCH whose body names a property the OData type does not have is
accepted by Creatio's OData v4 endpoint: it answers with an empty (204-like) body and
**writes nothing**. GitHub #1212 demonstrated `odata-update` calls with a `Color` column
(absent from `$metadata`) and a fully nonexistent column both returning `success: true`
while leaving the target untouched — silent data loss, made worse by the asymmetry that
`odata-read` `$select` rejects the same property names strictly. A second value-level
variant of the same defect: a lookup (reference) column set to the empty GUID
`00000000-0000-0000-0000-00000000` is silently dropped by the platform (the PATCH answers
success, the reference stays untouched), while `null` clears the reference — so that value
form is rejected up front with a hint to send `null`.

`ODataUpdateTool` therefore runs `ODataFieldValidation.ValidateDataFields` before every
PATCH. The PRIMARY validator is the service's own metadata: `GET odata/{entity}/$metadata`
is fetched (bounded: 30 s timeout, 3 attempts, 1 s delay) and parsed as CSDL — the entity's
property set (following `BaseType` inheritance, navigation properties included) and its
lookup reference-ID set (every `NavigationProperty/@Partner`) come from ONE deterministic
GET, which replaces the earlier per-field binary `$select` probing. The `$select` probe
(`$select=Id,<fields>`; the service names only the FIRST unknown property, so remaining
fields are re-probed individually, capped at 10) runs only as a FALLBACK when the metadata
body is empty, non-XML, or yields no type for the entity; the empty-GUID check is skipped
on that path because the reference-ID set is then unknown. Three semantics are kept: a
recognized unknown-property fault fails the call naming the field(s) ("could not be
verified against the service" on the fallback path, "do not exist on the OData type" on the
CSDL path); an empty or non-JSON pre-write body means UNVERIFIED and fails the call —
"cannot confirm" must never degrade into "proceed and report success"; any other recognized
error (record not found, unregistered entity) is surfaced verbatim. Malformed field names
violate the same character rules `odata-read` applies to filter fields
(`ODataKeyFormatter.IsValidMemberPath`) and are rejected locally before any remote call.

One build divergence is documented: on the #1212 report environment (build 858) a PATCH
with an unknown property silently accepted and left the value unwritten; on a newer canary
build the same PATCH is rejected with a named fault. The pre-validation makes the tool's
`success` flag truthful on both.

**Why it is this way** — the transport contract ("return the body, don't interpret it")
already forced `ValidateWriteResponse` for the body that comes *back*
(`odata-write-transport-never-throws-on-non-2xx.md`); this is the mirror for the payload
that goes *out*. `$metadata` is the oracle without maintaining a local copy of the entity
model: one fetch answers both the name-existence question and the lookup-vs-plain-Guid
question, and unlike the probe it does not depend on the addressed record existing or on
per-build `$select` strictness. The probe fallback exists because some environments serve
a non-XML body from the metadata endpoint (unregistered package state, proxy pages);
failing the call outright there would make `odata-update` unusable on those environments
for entities that work fine otherwise.

**What breaks if you ignore it** — any relaxation (skipping validation on "known-safe"
fields, treating an unverified metadata/probe body as success, moving validation to after
the PATCH, or dropping the empty-GUID rejection) reintroduces the exact failures of #1212:
the agent believes a write landed that never happened, or clears a reference it thinks it
set. Note the probe is specific to `odata-update`'s arbitrary field set: `odata-create`
shapes its body from the entity type, and `odata-delete` sends no body, so neither needs it.
