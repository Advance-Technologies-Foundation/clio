---
description: a SOAP web service method's request.uri is its message element, not an address; SOAP defaults are stored on every SOAP parameter
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceModels.cs
  - clio/Command/WebServiceDesigner/ServiceSchemaContracts.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — in a SOAP 1.1 (type 2) or 1.2 (type 3) service a method's `request.uri` is the message element,
the root element inside `<Body>` (`tem:Add`); the call goes to the base URI alone (Terrasoft.Services
`ServiceClientRequest` appends the method uri only for REST). The read of a SOAP service carries
`isSoapHeader: false` and `sequenceElementName: "item"` on every parameter, so only `isSoapHeader: true` and a
sequence name on an array change the envelope. An undeclared path prefix, or namespaces without exactly one default,
save fine and fail at call time with no HTTP request sent (measured on stand Creatio, .NET Framework, 2026-11-12).

**Why it is this way** — the SOAP designer page reuses the REST method contract and writes those defaults itself.

**What breaks if you ignore it** — showing a SOAP `uri` as an address makes an agent build a URL the platform never
calls, and reporting the defaults everywhere buries the two values that matter. `describe-web-service` reports
`message-element` instead of `uri` and `soap-header` / `sequence-element` only where they change the envelope; the
DTO writes those members only when it read them, so a REST save gains nothing.
