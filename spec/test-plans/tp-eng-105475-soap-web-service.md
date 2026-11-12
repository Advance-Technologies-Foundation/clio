# Test Plan: SOAP web services in clio's web service tools (ENG-105475)

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md)
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md)
**Stories**: [1](../stories/story-eng-105475-soap-web-service-1.md), [2](../stories/story-eng-105475-soap-web-service-2.md),
[3](../stories/story-eng-105475-soap-web-service-3.md), [4](../stories/story-eng-105475-soap-web-service-4.md),
[5](../stories/story-eng-105475-soap-web-service-5.md)
**Status**: slice A rows final; slices B-D and K are outlines, refined when their story starts
**Created**: 2026-11-12

## Rules

- Unit tests (`clio.tests`, `TestCategory=Unit`, module `Command`): AAA, a `because` on every assertion, a
  `[Description]` on every test, no OS-specific paths. SOAP fixtures follow the designer read shape measured on stand
  `Creatio` (`ServiceSchemaRequest` of a SOAP 1.1 service with namespaces, `soapAction`, `isSoapHeader`,
  `sequenceElementName`).
- MCP e2e (`clio.mcp.e2e`, `McpE2E.Sandbox`, destructive opt-in): every fixture is created and removed by the test;
  no shipped package; every code is built from the stand's `SchemaNamePrefix`, so any prefix (or none) works. Until
  slice B, a SOAP fixture is saved through the platform's own `ServiceDataContract` (`call-service`), the call Studio
  makes; it is removed with `ServiceSchemaRemoveRequest`. SOAP calls target `http://localhost/<code>` on the stand, so
  no internet is needed: the platform still builds and sends the envelope.

## Slice A (story 1)

| Id | Level | Case | AC |
| --- | --- | --- | --- |
| TC-U-A01 | unit | The view reports `SOAP 1.1`, `SOAP 1.2` and `REST` for types 2, 3 and 1 | 1 |
| TC-U-A02 | unit | A SOAP view lists the namespaces default first, and each method's `message-element` and `soap-action` with no `uri` | 2 |
| TC-U-A03 | unit | `soap-header` appears only on a SOAP-header parameter, `sequence-element` only on an array of a SOAP service; a serialized REST view has none of the SOAP members | 2 |
| TC-U-A04 | unit | A REST schema without SOAP members serializes back without them; a SOAP schema keeps namespaces, `soapAction`, `isSoapHeader` and `sequenceElementName` | 3 |
| TC-U-A05 | unit | `setMethod` keeps `soapAction`, `isSoapHeader` and `sequenceElementName` of the method and the parameters it keeps | 3 |
| TC-U-A06 | unit | A setting-bound SOAP-header element (`wsse:Security/wsse:UsernameToken/wsse:Password`) is collected from a SOAP 1.2 envelope | 4 |
| TC-U-A07 | unit | A setting-bound body element under a nested path and a setting-bound field of an object array are collected | 4 |
| TC-U-A08 | unit | A top-level setting-bound element missing from the sent XML, and a body that is neither JSON nor XML, are reported unlocated | 5 |
| TC-U-A09 | unit | An XML body with a DTD is not parsed and is reported unlocated | 5 |
| TC-U-A10 | unit | `Apply` masks the XML-escaped form of a secret | 4 |
| TC-U-A11 | unit | `TestMethod` on a SOAP method returns the fault body with the echoed setting value masked and `sent-secrets-masked: true` (replaces the clio#1900 stopgap test) | 4 |
| TC-U-A12 | unit | `TestMethod` returns `response-values` masked like the body, and withholds them with the body | 6 |
| TC-U-A13 | unit | `TestMethod` on a REST xml method that the platform answers with "Rest:Xml" explains the `RequestBody` route | 7 |
| TC-U-A14 | unit | Parsed response values longer than the body limit are omitted with `response-values-omitted: true` | 6 |
| TC-U-A15 | unit | The client reads `ParameterValues` sent as a JSON text or as an object, and null as null | 6 |
| TC-U-A16 | unit | A field missing from an array item the envelope carries is unlocated | 5 |
| TC-U-A17 | unit | A `prefix:name.tail` segment is matched as the platform renders it (`name`), not by the dotted tail | 4 |
| TC-U-A18 | unit | A multi-line setting value is masked in its CRLF form and line by line | 4 |
| TC-U-A19 | unit | An XML response is masked by decoded values (numeric character references) | 4 |
| TC-U-A20 | unit | Parsed values are masked by code for setting defaults, cookies and credential-named headers, and by value for a Guid or number the platform converted; an ordinary header stays readable | 6 |
| TC-U-A21 | unit | A constant default on a SOAP-header element is hidden by describe | 2 |
| TC-U-A22 | unit | The Rest:Xml explanation is returned, not withheld, for a method with authentication and a setting-bound parameter | 7 |
| TC-U-A23 | unit | A response value filled from a setting default is masked in `response-values` | 6 |
| TC-U-A24 | unit | An error with nothing reported as sent is withheld when a response default comes from a setting | 6 |
| TC-U-A25 | unit | A JSONPath on an XML response parameter gets a notice in describe and in the test call; a JSON response gets none | 6 |
| TC-I-A01 | e2e | Saves a SOAP 1.1 and a SOAP 1.2 service through `ServiceDataContract` (namespaces, SOAP action, a SOAP-header element bound to a system setting, a body element, a scalar array with a sequence element, a response parameter with a JSONPath), expects describe to report both versions, every SOAP member and a notice for the JSONPath, then test-calls the 1.1 method and expects the platform's reply (status, body) instead of a withheld response | 1, 2, 4, 6 |
| TC-I-A02 | e2e | Registers a REST service with an xml method, test-calls it and expects the error to name the `RequestBody` route | 7 |
| TC-I-A03 | e2e (existing) | The clio#1900 REST e2e suite stays green: REST describe and test output are unchanged | 2, 3 |

TC-U-A11 covers the echo case that TC-I-A01 cannot reach without an echoing SOAP endpoint on the stand.

## Slice B (story 2), outline

TC-U-B: one row per validator rule of ADR D8 (namespaces, prefixes, placements, `soap-header` depth, `$` paths,
REST-only members), the no-trailing-slash rule, the empty `soap-action` notice, `setNamespaces`, `setType` 1.1 ↔ 1.2,
REST ↔ SOAP refused, the usage guard counting both as changing every method, describe → modify round trip on the
designer-shaped fixtures. TC-I-B: register SOAP 1.1 and 1.2, describe, modify, round trip, test call against
`http://localhost/<code>` asserting the sent envelope through the platform reply; open and save in Studio (manual
stand check).

## Slice C (story 3), outline

TC-U-C: golden tests ported from Studio's wizard parser fixtures and expected outputs; warnings; local files mode;
the platform download error without its stack trace. TC-I-C: `wsdl-files` mode with a fixture WSDL shipped in the
test project (no internet); URL mode in a separate category that is Ignored when the stand has no internet; Studio vs
clio diff of the saved services (manual stand check).

## Slice D (story 4), outline

TC-U-D: arrays and object arrays mapped to the platform's `nested` member; a single object refused; masking of
nested values. TC-I-D: a test call with an array and an object array, asserting the sent body.

## Knowledge (story 5)

TC-K: the clio-knowledge bundle tests and the fact-check gate on the changed articles.
