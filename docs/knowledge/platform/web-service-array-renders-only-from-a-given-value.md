---
description: a web service request array (an inner one included) is rendered only from a value the call gives it, never from a default; setting-bound item fields fill every rendered item
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceResponseMasker.cs
ticket: ENG-105475
date: 2026-11-13
---

**What is true** — the platform builds a request body array, or an array inside an array item, only from an enumerable
value the call supplies (Terrasoft.Services `RequestBodyBuilder.ProcessArrayNode`); a parameter default never renders an
array or an object. An array the call leaves out, at any depth, is not in the JSON body or the SOAP envelope at all; one
given as empty is rendered empty (`[]`, `<tem:Tags arrayType="xsd:array[]"/>`). Inside every rendered item, a field
whose default is a system setting is filled with the setting value, also in an item given no fields (measured on stand
Creatio, .NET Framework, 2026-11-13, REST and SOAP 1.1).

**Why it is this way** — `ProcessArrayNode` and `ProcessObjectNode` read only the node's value; `GetDefaultValue` is
called for scalar nodes only.

**What breaks if you ignore it** — the test-call masking decides from this whether a setting-bound item field whose
array element is missing from the envelope was sent. Judging a missing inner array by its top-level array withholds
every response of a call that leaves the inner array out; judging it by nothing (assuming "not sent") would return an
unmasked setting value when the lookup merely missed the element.
