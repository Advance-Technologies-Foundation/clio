# Test Plan: SOAP web services in clio's web service tools (ENG-105475)

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md)
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md)
**Stories**: [1](../stories/story-eng-105475-soap-web-service-1.md), [2](../stories/story-eng-105475-soap-web-service-2.md),
[3](../stories/story-eng-105475-soap-web-service-3.md), [4](../stories/story-eng-105475-soap-web-service-4.md),
[5](../stories/story-eng-105475-soap-web-service-5.md)
**Status**: slice A, B and D rows final; slices C and K are outlines, refined when their story starts
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
| TC-U-A26 | unit | An array item field whose array element is missing too fails closed unless the call gave the array no value and it has no default | 5 |
| TC-U-A27 | unit | An XML response with a DOCTYPE is masked by its decoded values (character references) | 4 |
| TC-I-A01 | e2e | Saves a SOAP 1.1 and a SOAP 1.2 service through `ServiceDataContract` (namespaces, SOAP action, a SOAP-header element bound to a system setting, a body element, a scalar array with a sequence element, a response parameter with a JSONPath), expects describe to report both versions, every SOAP member and a notice for the JSONPath, then test-calls the 1.1 method and expects the platform's reply (status, body) instead of a withheld response | 1, 2, 4, 6 |
| TC-I-A02 | e2e | Registers a REST service with an xml method, test-calls it and expects the error to name the `RequestBody` route | 7 |
| TC-I-A03 | e2e (existing) | The clio#1900 REST e2e suite stays green: REST describe and test output are unchanged | 2, 3 |

TC-U-A11 covers the echo case that TC-I-A01 cannot reach without an echoing SOAP endpoint on the stand.

## Slice B (story 2)

Unit rows live in `clio.tests/Command/WebServiceDesigner/WebServiceSoapWriteTests.cs`.

| Id | Level | Case | AC |
| --- | --- | --- | --- |
| TC-U-B01 | unit | A SOAP 1.1 service is saved as Studio saves one: default namespace, message element in the request uri, POST and xml, SOAP action, `isSoapHeader` false and `sequenceElementName` item on every parameter, base URI without a trailing slash | 1, 3 |
| TC-U-B02 | unit | One refusal names every broken rule: a uri, a non-POST verb, a non-xml content type, an undeclared prefix in the message element and in a path, a query placement, a name the platform would cut (`tem:Api.Key`), a sequence element on a scalar, a SOAP header on a nested or a response parameter, a JSONPath response path; nothing is saved | 2 |
| TC-U-B03 | unit | No namespace, two defaults, a duplicate prefix and an empty URI are refused | 2 |
| TC-U-B04 | unit | A namespace marked default wins over the first; soap12 is SOAP 1.2 | 1 |
| TC-U-B05 | unit | Namespaces, message-element, soap-action, soap-header and sequence-element on a REST service are refused | 2 |
| TC-U-B06 | unit | An empty soap-action is saved with a notice that no SOAPAction header is sent | 3 |
| TC-U-B07 | unit | A SOAP-header parameter passes the flag to its items; a constant default on a SOAP-header parameter is refused | 1, 2 |
| TC-U-B08 | unit | describe output sent back as setNamespaces + setMethod leaves describe unchanged and keeps every UId, and a hidden SOAP-header constant is kept | 5 |
| TC-U-B09 | unit | setType switches 1.2 to 1.1 and the usage guard treats it as changing every method | 4 |
| TC-U-B10 | unit | setType to REST or to an unknown type is refused | 4 |
| TC-U-B11 | unit | setNamespaces dropping a prefix a kept method uses is refused | 4 |
| TC-U-B12 | unit | setNamespaces and setType are refused on a REST service | 4 |
| TC-U-B13 | unit | A SOAP setBaseUri keeps the URI as given | 3 |
| TC-U-B14 | unit | A SOAP-header object array read back from describe (its items carry the flag) saves unchanged through setMethod | 5 |
| TC-U-B15 | unit | setNamespaces counts as changing every method only when the list really changes | 4 |
| TC-U-B16 | unit | Reserved prefixes (xml*, soapenv) are refused | 2 |
| TC-U-B17 | unit | A soap-action with a line break or credentials, a namespace uri with credentials, a message element with a path and a SOAP base-URI placeholder are refused without echoing the values | 2 |
| TC-U-B18 | unit | setType with an unknown type reports that error alone | 4 |
| TC-U-B19 | unit | A constant on an item of a SOAP-header parameter is hidden by describe even when the item lacks the flag | 2 |
| TC-U-B20 | unit | A refusal lists at most 50 problems and counts the rest | 2 |
| TC-U-B21 | unit | A namespace row with null uId / isDefault reads, and setNamespaces keeps its collection key as UId (#1925 review note) | 5 |
| TC-U-B22 | unit | A namespace row's null uId / isDefault is left out on write, never sent as null | 5 |
| TC-U-B23 | unit | The credential-name pattern matches licence/license, nonce and a name ending in pass (UsrPass, AdminPass, SmtpPass, tem:pass), not passport, compass or bypass. REST effect: a constant default on a REST parameter so named (e.g. `UsrLicenseType`) is now refused and hidden by describe as well | 2 |
| TC-U-B24 | unit | A request array's sequence-element may be `prefix:name` with a declared prefix (`tem:Region`, as Studio's wizard writes it); an undeclared prefix or a name the platform would cut is refused, and setNamespaces cannot drop a prefix only a sequence-element uses | 2, 5 |
| TC-U-B25 | unit | A visible body constant is not carried into the SOAP Header by a setMethod that adds soap-header without a default; a constant hidden by a SOAP-header parent is refused, not erased, when the parent leaves the Header (final-gate review) | 2, 5 |
| TC-U-B26 | unit | User information is detected fail-closed (any `@` after `://`) in a soap-action and a namespace uri, also when the password makes the value unparseable as a URI | 2 |
| TC-U-B27 | unit | setNamespaces counts a prefix the platform reads in a segment clio would refuse (`ns2:Api.Key`) | 4 |
| TC-U-B28 | unit | A SOAP base URI keeps a fixed query (`?sap-client=100`); a credential-like key with a constant value there is refused; unprefixed Unicode element names are accepted | 2, 3 |
| TC-U-B29 | unit | An empty response sequence-element survives describe → setMethod (it reads every child element; `item` would read nothing) | 5 |
| TC-U-B30 | unit | register-web-service passes `type` and `namespaces` to the designer (MCP mapping) | 1 |
| TC-U-B31 | unit | Review round (ArtemKulykov): the empty-soap-action notice is SOAP 1.1 only and follows a setType to 1.1 for every method; a stored namespace list without a default sent back from describe is not a change; a `?wsdl` / `?singleWsdl` base URI is refused; a blank request sequence-element is stored as `item`; the single-Object advice is an element path on SOAP; `pass` before a capital (`UsrSmtpPassValue`) is credential-like | 2, 3, 4 |
| TC-I-B01 | e2e | Registers a SOAP 1.1 service through the tool (a SOAP-header object array with an item included), refuses one with an undeclared prefix, round-trips describe through modify unchanged, switches to SOAP 1.2, refuses dropping a namespace in use, and test-calls it against `http://localhost/<code>` without the response being withheld | 1-5 |
| TC-M-B01 | manual | A service registered by clio opens and saves unchanged in Studio, SOAP 1.1 and 1.2 | 6 |

TC-M-B01 result, 2026-11-12, Creatio 10.2 (.NET Framework, prefix `Usr`), clio at `b2f878532`: **PASS**. Three services
registered by clio were opened in Studio > Web services: SOAP 1.1 `UsrTcNoActionB`, SOAP 1.2 `UsrTcCalcB` (registered
as 1.1 and switched by `setType`) and SOAP 1.2 `UsrTcSecureB` (a WS-Security `soap-header` parameter bound to a
SecureText setting). A method and a parameter of each closed with OK and no validation message, and each card was
saved (Studio offers Save only after an edit, so a reversible Description edit was used). `describe-web-service`
before and after the save was byte-identical for all three, and for `UsrTcNoActionB` the raw `ServiceSchemaRequest`
answer was identical too; test calls after the save returned HTTP 200 with the expected result and the header value
masked. The SysSchema `Checksum` changes on a Studio save while the designer's read stays identical: save
bookkeeping, not a content change.

## Slice C (story 3), outline

TC-U-C: golden tests ported from Studio's wizard parser fixtures and expected outputs; warnings; local files mode;
the platform download error without its stack trace. TC-I-C: `wsdl-files` mode with a fixture WSDL shipped in the
test project (no internet); URL mode in a separate category that is Ignored when the stand has no internet; Studio vs
clio diff of the saved services (manual stand check).

## Slice D (story 4)

The wire shape was measured first (ADR D10). Unit rows live in
`clio.tests/Command/WebServiceDesigner/WebServiceTestValuesTests.cs` (D01-D10) and next to the code they cover.

| Id | Level | Case | AC |
| --- | --- | --- | --- |
| TC-U-D01 | unit | A scalar goes out as text (a number or boolean as its JSON text) with no `nested` member; null leaves a scalar and an array out | 1 |
| TC-U-D02 | unit | A body array of scalars goes out as a JSON array of texts in `value`; an empty array goes out empty | 1 |
| TC-U-D03 | unit | An array of objects goes out in `nested`, one list per item, with a scalar array and an inner object array inside an item; an empty one as `nested: []`; a null field is left out; every code given a value is reported, nested ones included | 1, 2 |
| TC-U-D04 | unit | One refusal names, with its location and the unknown codes first, an unknown top-level code and an unknown item field (each with the valid codes), a scalar given an array, a body array given a text (one item per character), a single object for an object array, a non-object item, a misshapen value two arrays deep, a null scalar-array item and a value for a single Object parameter | 1, 2 |
| TC-U-D05 | unit | A value of the wrong kind for an array (a text or number for an object array, a number, object or nested array for a scalar array) names the kind it got, without the per-character hint | 1 |
| TC-U-D06 | unit | A path, query, header or cookie parameter marked as an array takes one text (`"1,2"` sent as written); a JSON array there is refused, since the platform would send `System.Object[]` (measured) | 1 |
| TC-U-D07 | unit | A field given twice in one item is refused (the platform fails the call on it) | 2 |
| TC-U-D08 | unit | No values build an empty call; a value for a method without request parameters is refused with "valid codes: none" | 1 |
| TC-U-D09 | unit | Codes match case-sensitively, as the platform looks them up | 2 |
| TC-U-D10 | unit | More than 1,000 array items in all, counted at every depth, are refused | 1 |
| TC-U-D11 | unit | The client posts `{code, value}`, `{code, value: [...]}` and `{code, nested: [[...]]}` (`WebServiceDesignerClientTests`) | 1 |
| TC-U-D12 | unit | A setting-bound field of an inner object array is collected two arrays deep; its missing element fails closed only when the call gave that inner array a value, not merely the outer one (`WebServiceSoapTests`) | 2 |
| TC-U-D13 | unit | A setting-bound JSON body value is found under the name the platform writes it to, for `k[0]` and `$['b']` too; one missing from the body fails closed, one sent as null is located; a name holding only an object, or a path ending in an unclosed bracket, fails closed (`WebServiceResponseMaskerTests`) | 2 |
| TC-U-D14 | unit | A setting-bound field of a JSON array item is found in every item also when its own placement says header; it fails closed when an item that went out lacks it or a given array is missing; an array left out or sent empty, and a setting-bound body array, hide nothing (`WebServiceResponseMaskerTests`) | 2 |
| TC-U-D15 | unit | A setting-bound SOAP item field whose own placement says header is collected from the envelope body, and fails closed when an item lacks it (`WebServiceResponseMaskerTests`) | 2 |
| TC-U-D16 | unit | `TestMethod` sends an object array in `nested` and masks a setting value the platform filled into every item and an API echoed, in the body and `response-values` (`WebServiceDesignerTests`) | 1, 2 |
| TC-U-D17 | unit | `TestMethod` refuses an unknown item field before any request (`WebServiceDesignerTests`) | 2 |
| TC-U-D18 | unit | `TestMethod` on SOAP returns the masked response when the call left an inner array out, instead of withholding it (`WebServiceDesignerTests`) | 2 |
| TC-U-D19 | unit | test-web-service-method passes `parameters` with arrays to the designer unchanged (`WebServiceToolsArgumentTests`) | 1 |
| TC-I-D01 | e2e | Registers a REST service on an echo API (`https://httpbin.org/anything`) with a Text array and an Object array whose items hold a Text array and an inner Object array, test-calls it with values at every depth and expects the echoed JSON body to carry them. Ignored only when the echo API gives no answer; a call the platform fails to build fails the test | 1 |
| TC-I-D02 | e2e | Registers a SOAP 1.1 service on `http://localhost/<code>` whose Object array has an item field and an inner Object array field bound to a system setting; test-calls it leaving the inner array out and filling it, and expects neither response withheld | 2 |
| TC-I-D03 | e2e | Registers a REST service with a query array, a body array and an Object array, and expects a JSON array for the query value, a text for the body array and an unknown item field refused together with their locations, with no status (nothing sent); needs no internet | 1, 2 |
| TC-I-D04 | e2e (existing) | The REST, SOAP and contract web service e2e suites stay green | 1 |

## Knowledge (story 5)

TC-K: the clio-knowledge bundle tests and the fact-check gate on the changed articles.
