---
description: the web service test call takes an array in value and an object array in nested; any other shape is sent distorted with no error
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceTestValues.cs
  - clio/Command/WebServiceDesigner/WebServiceResponseMasker.cs
ticket: ENG-105475
date: 2026-11-13
---

**What is true** — `CallServiceSchemaService.Execute` (Studio's Send test request, `test-web-service-method`) takes
`parameters: [{code, value, nested}]`. An array of scalars goes in `value` as a JSON array; an array of objects goes in
`nested`, one list of `{code, value, nested}` per item, recursively. Every other shape is accepted and sent wrong, with
success and no error (measured on stand Creatio, .NET Framework, 2026-11-13, REST and SOAP 1.1):

- a text for an array goes out one item per character (`"ab"` → `["a","b"]`);
- objects in `value` go out as an empty array; `nested` on an array of scalars sends `System.Collections.Generic.Dictionary...`;
- an unknown code inside an item is dropped; only a code given twice fails ("An item with the same key has already
  been added.", nothing sent);
- an array, an inner one included, is rendered only from a value the call gives it, never from a default
  (Terrasoft.Services `RequestBodyBuilder.ProcessArrayNode`); a setting-bound item field is filled in every item.

**Why it is this way** — the platform binds `value` as `object` with `DataContractJsonSerializer` and builds the
request from whatever enumerable or dictionary it gets (`ServiceSchemaParameterBuilder`, `RequestBodyBuilder`); it never
checks the value against the parameter tree. Studio's form only ever sends the right shape, so nothing else was guarded.

**What breaks if you ignore it** — a test call "succeeds" against the external API with a body that is not what the agent
meant (split text, empty arrays, dropped fields), and the agent reports the API wrong. In the masking, judging a missing
inner array element by its top-level array instead of by whether the call filled that inner array withholds every
response of a call that leaves an inner array out; judging it by nothing would return an unmasked setting value.
