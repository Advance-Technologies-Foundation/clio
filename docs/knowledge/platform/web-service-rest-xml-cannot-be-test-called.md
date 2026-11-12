---
description: a REST web service method with content-type xml always fails a test call with "Rest:Xml"; only a process RequestBody calls it
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — the platform has no request body builder for a REST method whose content type is XML: every
call through `CallServiceSchemaService.Execute` (Studio's Send test request, and `test-web-service-method`) fails with
"Rest:Xml" before any HTTP request, with or without body parameters (measured on stand Creatio, .NET Framework,
2026-11-11). A process can call such a method only by sending the whole XML text in the Call web service element's
`RequestBody` (measured: calculator.asmx through REST + XML in a process returned 30). A custom request body builder
(`bodyBuilderId`, feature `UseCustomRequestBodyBuilder`) could change this, but none ships and the lookup is empty on
the stands measured.

**Why it is this way** — Terrasoft.Services `RequestBodyBuilderFactory` builds a JSON body for REST and a SOAP envelope
for SOAP, and throws `NotSupportedException("Rest:Xml")` otherwise unless the request carries a raw body or a custom
builder.

**What breaks if you ignore it** — an agent that registers an XML API as REST and test-calls it reads a bare
"Rest:Xml" and starts changing the service instead of moving the body into the process. `test-web-service-method`
therefore keeps the platform's message and appends the `RequestBody` route. It does not refuse before calling, so a
platform or a builder that makes the call work is not blocked.
