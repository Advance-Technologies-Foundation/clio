---
description: the web service test call takes an array in value and an object array in nested; any other shape is sent distorted with no error
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceTestValues.cs
ticket: ENG-105475
date: 2026-11-13
---

**What is true** — `CallServiceSchemaService.Execute` (Studio's Send test request, `test-web-service-method`) takes
`parameters: [{code, value, nested}]`. A body array of scalars goes in `value` as a JSON array; a body array of objects
goes in `nested`, one list of `{code, value, nested}` per item, recursively. Every other shape is accepted and sent
wrong, with success and no error (measured on stand Creatio, .NET Framework, 2026-11-13, REST and SOAP 1.1):

- a text for a body array goes out one item per character (`"ab"` → `["a","b"]`);
- objects in `value` go out as an empty array; `nested` on an array of scalars sends `System.Collections.Generic.Dictionary...`;
- a path, query, header or cookie value is written with `ToString()` whatever its array flag says: a text goes out as
  given (`"1,2"`), a JSON array as `System.Object[]`;
- an unknown code inside an item is dropped; only a code given twice fails ("An item with the same key has already
  been added.", nothing sent).

**Why it is this way** — the platform binds `value` as `object` with `DataContractJsonSerializer` and builds the
request from whatever enumerable or dictionary it gets (`ServiceSchemaParameterBuilder`, `RequestBodyBuilder`,
`ServiceClientRequest`); it never checks a value against the parameter tree. Studio's form only ever sends the right
shape, so nothing else was guarded.

**What breaks if you ignore it** — a test call "succeeds" against the external API with a request that is not what
the agent meant (split text, empty arrays, `System.Object[]` in the URL, dropped fields), and the agent reports the
API wrong.
