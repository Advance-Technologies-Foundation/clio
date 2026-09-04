---
description: Creatio's OData v4 endpoint accepts a PATCH naming properties the entity type does not have and answers an empty 204-like body without writing anything - odata-update pre-validates every data field NAME against the service-root $metadata CSDL, with a $select probe fallback
applies-to:
  - clio/Command/McpServer/Tools/ODataUpdateTool.cs
  - clio/Command/McpServer/Tools/ODataFieldValidation.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Command/McpServer/Tools/ODataDateTimeGuard.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio.tests/Command/McpServer/ODataUpdateToolTests.cs
  - clio.tests/Command/McpServer/ODataCreateToolTests.cs
  - clio.mcp.e2e/ODataUpdatePreWriteValidationE2ETests.cs
ticket: GH-1212
date: 2026-08-27
---

**What is true** — a PATCH whose body names a property the OData type does not have is
accepted by Creatio's OData v4 endpoint: it answers with an empty (204-like) body and
**writes nothing**. GitHub #1212 demonstrated `odata-update` calls with a `Color` column
(absent from `$metadata`) and a fully nonexistent column both returning `success: true`
while leaving the target untouched — silent data loss, made worse by the asymmetry that
`odata-read` `$select` rejects the same property names strictly.

Scope note: this validation covers field NAMES only. The one value-level check that ships
alongside it is the zone-less date-time guard (`ODataDateTimeGuard`, see
`odata-datetime-literals-need-an-explicit-zone.md`), which refuses a date-time literal carrying no
UTC designator and no offset before the request leaves the process; there is still no
lookup-vs-plain-Guid check on either path. That related value-level variant — a lookup
(reference) column set to the empty GUID is silently dropped by the platform while `null`
clears the reference — and `odata-update` deliberately does NOT reject it: enforcing it needs
the entity's foreign-key set, which is only available on the CSDL path, so the same call would
pass or fail depending on whether `$metadata` resolved on that environment. It is tracked
separately rather than shipped non-deterministically here.

`ODataUpdateTool` therefore runs `ODataFieldValidation.ValidateDataFields` before every
PATCH. The PRIMARY validator is the service's own metadata: `GET odata/$metadata` is fetched
(bounded: 30 s timeout, 3 attempts, 1 s delay) and parsed as CSDL, and the entity's property
set comes from ONE deterministic GET, which replaces the earlier per-field binary `$select`
probing. Only STRUCTURAL `Property` elements enter that set, following `BaseType` inheritance;
a `NavigationProperty` is deliberately excluded, because an OData relationship is written
through bind semantics rather than by assigning the navigation name, and the contract points
callers at the structural foreign key (`AccountId`, not `Account`). The fallback probe leaves a
navigation name unverified for the same reason: it only proves a field is readable.

**The route is the SERVICE ROOT.** In OData v4 `$metadata` is a service-root resource
(`serviceRoot/$metadata`); `serviceRoot/EntitySet/$metadata` is not a defined resource path,
and ASP.NET Web API OData's `MetadataRoutingConvention` maps only `~/$metadata`. Every
`@odata.context` value this repo has captured confirms the root form
(`.../0/odata/$metadata#Contact`). A per-entity route would 404 into the routing-error body —
the transport does not throw on non-2xx — leaving the CSDL branch permanently unresolved and
every update silently on the degraded probe while the tool advertised metadata validation.

The `$select` probe (`$select=Id,<fields>`; the service names only the FIRST unknown property,
so remaining fields are re-probed individually, capped at 10) runs only as a FALLBACK when the
metadata body is empty, non-XML, or yields no type for the entity. Three semantics are kept: a
recognized unknown-property fault fails the call naming the field(s) ("could not be
verified against the service" on the fallback path, "do not exist on the OData type" on the
CSDL path); an empty or non-JSON pre-write body means UNVERIFIED and fails the call —
"cannot confirm" must never degrade into "proceed and report success"; any other recognized
error (record not found, unregistered entity) is reported as a fixed local diagnostic. Server
prose is NOT echoed back: the surfaced text is chosen by clio, so a server- or proxy-controlled
body cannot reach the MCP transcript through this path. Field names are validated as simple
property names before any remote call - a dotted member path is not accepted here, unlike the
filter fields `odata-read` runs through `ODataKeyFormatter.IsValidMemberPath`.

One build divergence is documented: on the #1212 report environment (build 858) a PATCH
with an unknown property silently accepted and left the value unwritten; on a newer canary
build the same PATCH is rejected with a named fault. The pre-validation makes the tool's
`success` flag truthful on both.

**Why it is this way** — the transport contract ("return the body, don't interpret it")
already forced `ValidateWriteResponse` for the body that comes *back*
(`odata-write-transport-never-throws-on-non-2xx.md`); this is the mirror for the payload
that goes *out*. `$metadata` is the oracle without maintaining a local copy of the entity
model: one fetch answers the name-existence question for every field at once, and unlike the
probe it does not depend on the addressed record existing or on
per-build `$select` strictness. The probe fallback exists because some environments serve
a non-XML body from the metadata endpoint (unregistered package state, proxy pages);
failing the call outright there would make `odata-update` unusable on those environments
for entities that work fine otherwise.

Observed (work build `394_15918340_0922`, 2026-08-28): a keyed `$select` naming two unknown
properties (`?$select=Id,labBadOne,labBadTwo`) and one naming three (`Id,labBadOne,labBadTwo,
labBadThree`) both return the SAME error naming only the FIRST bad name —
`{"error":{"code":"","message":"The query specified in the URI is not valid. Could not find a
property named 'labBadOne' on type 'Terrasoft.Configuration.OData.Contact'.","innererror":{"message":
"Could not find a property named 'labBadOne' on type 'Terrasoft.Configuration.OData.Contact'","type":"",
"stacktrace":""}}}` — the premise the fallback's per-field re-probe rests on, now anchored to data.

On the empty-body asymmetry: the pre-write READ (the `$metadata` fetch and the probe) treats an
empty body as unverified-and-fail, while the post-PATCH write path
(`ODataKeyedWrite.ValidateWriteResponse`) treats an empty/whitespace body as a valid 204 ack. The
readings differ because the operations differ — a keyed read of an existing record must answer with
the record's JSON or an error and never legitimately returns empty, so an empty GET body means the
request did not reach the OData pipeline intact (proxy page, session redirect, gateway that stripped
the body); a PATCH, by contrast, can legitimately answer a body-less 204. Treating an empty probe
body as "fields confirmed" would recreate the false success this validation removes; both stay
fail-closed after the bounded retry.

**How the route and the no-write are proved** — the service-root `$metadata` route and the
absence of the PATCH are pinned end-to-end, not just in-process:
`clio.mcp.e2e/ODataUpdatePreWriteValidationE2ETests` drives the real MCP server against a stub
Creatio that records every request it serves
(`RuntimeDetectionStubServer.RecordedRequestsPath`), so the tests assert the validation GETs
`.../odata/$metadata` and never `.../odata/{entity}/$metadata`, and that a rejected or
unverified payload produces **no PATCH request at all**. The stub acks a PATCH, so the absence
of the request — not a transport failure — is what proves the tool refused to write. A unit
test cannot establish either fact: it sees the mocked calls, not the URL the process emits.
On the unverified path the surfaced text is a fixed local diagnostic naming the field(s) and the
route that could not confirm them; no prefix of the unrecognized body is carried, so a proxy/SSO
page's credentials or internal hostnames cannot reach the transcript at all rather than relying on
`SensitiveErrorTextRedactor` to scrub them.

**What breaks if you ignore it** — any relaxation (skipping validation on "known-safe"
fields, treating an unverified metadata/probe body as success, moving validation to after
the PATCH, or letting navigation names back into the writable set) reintroduces the exact
failure of #1212: the agent believes a write landed that never happened. Scope note: the #1212 silent drop is PATCH-specific, and `odata-create` (POST) is exempt for a
verified, platform-side reason: Creatio's POST REJECTS an unknown property with a named fault and
creates no record — observed (work build `394_15918340_0922`, 2026-08-28) a POST body carrying
`labNope` returned
`{"error":{"code":"","message":"The request is invalid.","innererror":{"message":"item : The
property 'labNope' does not exist on type 'Terrasoft.Configuration.OData.Contact'. Make sure to only
use property names that are defined by the type.","type":"","stacktrace":""}}}`
with no record created — unlike the PATCH silent drop. `odata-create` therefore gets a loud,
self-correcting failure rather than a silent no-write and needs no client-side pre-validation of field
NAMES. It is NOT exempt from the value-level guard: `ODataDateTimeGuard` runs per row before every POST,
and the CSDL is read at most once per batch - only when some row actually carries a date-shaped literal,
and on a short single-attempt budget, because there the type map merely sharpens an optional guard and
must never hold up or fail the insert. `odata-delete` sends no field set and is unaffected. (As with the documented PATCH build divergence,
this POST strictness is a single-build observation; if a future build loosens POST to a silent drop,
`odata-create` would inherit the same gap and this note must be revisited.)
