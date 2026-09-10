---
description: an unrouted Creatio .svc answers 404 with a zero-length body and clio's synchronous client returns it as an empty string, so ScriptSchemaDesignerService looks like a JSON parser bug
applies-to:
  - clio/Command/SchemaDesignerHelper.cs
  - clio/Command/SqlSchemaCreate.cs
  - clio/Command/SourceCodeSchemaCreate.cs
  - clio/Package/ServiceResponseJsonGuard.cs
ticket: GH-1322
date: 2026-09-05
---

**What is true** — `ServiceModel/ScriptSchemaDesignerService.svc` is not served by every Creatio
version. Measured on stand `stand1` (Creatio 10.1.725, .NET Framework, cliogate 2.0.0.48) with a raw
authenticated request: every method of that route answers `HTTP 404` with `Content-Length: 0`, while
`SourceCodeSchemaDesignerService.svc/CreateNewSchema` on the same instance answers `HTTP 200` with a
schema payload. `SqlScriptSchemaDesignerService.svc` exists as a file on that instance but its WCF
help page says `Endpoint not found.`, so it is not a drop-in replacement either. The issue reporter
saw the same failure on Creatio 10.0.0.858.

`IApplicationClient.ExecutePostRequest` does not expose the HTTP status: a 404 with an empty body
comes back as an empty string, indistinguishable from a served endpoint that answered 200 with no
body. Issue #1317 is the work that would make the status authoritative.

**Why it is this way** — the synchronous `ICreatioClient` API clio still calls returns only the
response string. Every classification clio makes about a transport failure is therefore inferred
from the body, and an empty body carries no information at all.

**What breaks if you ignore it** — `JObject.Parse("")` throws
`Error reading JObject from JsonReader. Path '', line 0, position 0.`, which the command's catch-all
turned into its whole error text. That message names neither the service nor the route, so a missing
platform endpoint reads as a clio JSON bug: the reporter of issue #1322 retried a create that could
never succeed. Do not add a "the server accepted the request" claim to an empty-body message either
— on this path that claim is simply false.

**Extra measurement** — the distinct `ManagerName` values on that same stand are
`AddonSchemaManager, ClientUnitSchemaManager, CopilotIntentSchemaManager, DcmSchemaManager,
EntitySchemaManager, ImageListSchemaManager, PageSchemaManager, ProcessSchemaManager,
ProcessUserTaskSchemaManager, ServiceSchemaManager, SourceCodeSchemaManager, ValueListSchemaManager`.
No SQL-script manager appears under any name, so the SQL-script schema type is absent from that
platform build entirely — the message fix makes the failure diagnosable, it does not make
`create-sql-schema` work there.
