---
description: the platform writes a SOAP body and Header in the stored order of the method's parameters, so describe must not sort them
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceModels.cs
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
ticket: ENG-105475
date: 2026-11-14
---

**What is true** — a SOAP call writes the body elements, and the SOAP Header elements, in the order the method's
request parameters are stored. Terrasoft.Services `ServiceClientRequest` builds the body from
`Request.BodyParameters`. `ServiceCommunication` fills that list with `Parameters.Where(p => p.Type == Body)`, which
keeps collection order. `SoapRequestBodyBuilder` then appends the body parameters, and after them the
`IsSoapHeader` ones, in that order. Nothing sorts them on the way.

**Why it is this way** — the platform keeps the order Studio's parameter list was saved in. A WSDL `xs:sequence` is
reproduced only by storing the parameters in sequence order. clio's `BuildParameters` stores them in the order the
call gives them.

**What breaks if you ignore it** — a view that lists parameters in any other order (sorted by name, say) turns
describe output sent back as `setMethod`, which the tool contract promises is valid input, into a reordered
envelope: a WS-Security `UsernameToken` would go out Password before Username, a WSDL sequence `Name, Age` as
`Age, Name`. Schema-validating endpoints reject that, and WCF's DataContractSerializer silently drops the
out-of-order members, so the call succeeds with values missing. Nothing on the clio side fails: only a test of the
saved key sequence, not the key set, catches it.
