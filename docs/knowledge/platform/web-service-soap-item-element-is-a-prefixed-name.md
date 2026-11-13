---
description: a SOAP array's item element name (sequenceElementName) is read like a path segment, prefix:name with the prefix looked up in the service namespaces, and Studio's WSDL wizard writes it prefixed (ns0:string)
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDefinitionBuilder.cs
  - clio/Command/WebServiceDesigner/WsdlRegistrationMapper.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — Terrasoft.Services `SoapRequestBodyBuilder.GetArrayElementNodeName` returns the array parameter's
`SequenceElementName`, and `SoapBodyBuilder` creates each item element from it with `ParseElementPath`: a `prefix:name`
match takes its namespace from the service namespaces by prefix (an undeclared one throws "The given key was not present
in the dictionary." before any HTTP request), a name without a colon gets the default namespace. Studio's WSDL wizard
stores the prefixed form (`ns0:string`, `tem:Region`); Studio's form stores `item` on every parameter. On a response
the item is matched by local name. Traced from the platform source; a registered `tem:Tag` item element round-tripped
through describe on stand `Creatio` (read-wsdl e2e, 2026-11-12).

**Why it is this way** — the item element is an element like any path segment, so the platform reuses the segment
parser.

**What breaks if you ignore it** — a validator that accepts only a plain name refuses every array that came from a WSDL
and the describe → setMethod round trip of a Studio-made one (slice B did, ADR D13); one that accepts any prefix saves a
service whose every call with that array fails before sending; dropping the namespace an item element uses with
setNamespaces breaks the same way, so the prefix counts as used.
