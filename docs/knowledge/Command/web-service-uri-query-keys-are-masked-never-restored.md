---
description: describe masks a key kept in a stored URI query and the tools never write one back; restoring the stored key on an exact echo was tried and dropped
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceModels.cs
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-14
---

**What is true** — a service made in Studio can keep a key as a constant in a URI query: a base URI, a REST method
`uri`, a `soap-action`, or a namespace URI. `describe-web-service`, `list-web-services` and the test call show that
value as `[masked]`, and `register-web-service` / `modify-web-service` refuse to write one. Both sides use one
scanner, `WebServiceSchemaView.FindQueryKeys` and the scan behind `MaskUriQuery`, and one rule,
`IsSecretQueryValue`. A `setMethod` or `setBaseUri` that sends `[masked]` back is refused. The stored value is never
put back in its place.

**Why it is this way** — restoring the stored key when describe's masked text came back unchanged was implemented and
then removed. Because the write rule refuses exactly what the read rule hides, a restored key is refused on save
anyway. The restore only added a second comparison that had to match every display form (user information, a
leading `/`, a `?` or `;` inside a value), and each mismatch was a refusal with a misleading message. Reading the
write side with a different parser than the read side is what let a `[masked]` after a second `?` be stored in place
of a key.

**What breaks if you ignore it** — bringing the restore back without the same scanner reintroduces two failures.
The mask gets saved as the key, so every later call fails silently. Or a restored key leaves through a new base URI
host. Splitting the read and write parsers again lets a value describe hides be written: `;`- or `?`-separated pairs,
and a key whose value contains `@`.
