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

## Slice C (story 3)

Unit rows live in `clio.tests/Command/WebServiceDesigner/WsdlParserTests.cs` (parser), `WsdlRegistrationMapperTests.cs`
(clio's rules), `WsdlReaderTests.cs` (sources and result), `WebServiceDesignerClientTests.cs` (download),
`clio.tests/Command/McpServer/WebServiceToolsArgumentTests.cs` (tool) and
`clio.tests/Common/ServiceUrlBuilder.cs` (route). The golden files are Studio's parser output (ADR D9).

| Id | Level | Case | AC |
| --- | --- | --- | --- |
| TC-U-C01 | unit | The public calculator WSDL and a synthetic WSDL covering the wizard's fixture cases (by-ref, nested namespaces, flattened objects, wrapped scalar and object arrays, an object array in the response root, the type map, recursion, SOAP headers simple/complex/array/two, a long element name, dotted names, typed parts) give Studio's own output (golden) | 3 |
| TC-U-C02 | unit | A WSDL whose types come from an imported (URL `schemaLocation`), an included and a nested imported schema merges as the wizard merges it (golden); a missing schema is named | 3 |
| TC-U-C03 | unit | Every SOAP port is returned, the SOAP 1.2 one is Studio's pick, HTTP ports are dropped with a warning; with one SOAP 1.1 port that port is the pick | 2 |
| TC-U-C04 | unit | The wizard's expectations hold: reference namespace, ns0/ns1 order, caption normalization, scalar and object arrays with item elements, array in root, type map, recursion cap with a warning, first SOAP header only with a warning, a 200-character code, the root element, typed parts | 3 |
| TC-U-C05 | unit | The wizard's copies of nested elements are kept and marked by the parser and left out of the arguments with a warning | 2, 3 |
| TC-U-C06 | unit | A complexContent extension adds its elements before the base type's; a simpleContent extension of base64Binary is Text | 3 |
| TC-U-C07 | unit | An unmapped XSD built-in and an undefined type are Text with a warning (the wizard fails); rpc/encoded, wsdl:import, MIME/MTOM, xs:choice and XML attributes give warnings; WSDL 2.0, a schema alone and a DTD are refused | 2 |
| TC-U-C08 | unit | A downloaded file is read as ASCII text whatever encoding its declaration names | 1 |
| TC-U-C09 | unit | Codes are stable and prefixed (`UsrAdd`), the message element is `tem:Add`, collisions get 2, 3 case-insensitively, an empty prefix works, a long code keeps the prefix | 2 |
| TC-U-C10 | unit | What register would refuse is left out with a warning: a copy, a dotted request element, a dotted root element (the method), an object without fields, a repeated element without a wrapper; SOAP members are written only where they apply | 2 |
| TC-U-C11 | unit | An element of no namespace loses its prefix with a warning | 2 |
| TC-U-C12 | unit | With a target service, a matched operation keeps its code and is listed in existing-methods, another is numbered past the service's codes, and the namespaces reuse the service's prefixes | 2 |
| TC-U-C13 | unit | wsdl-files: one argument object per port, Studio's pick marked, no null members, no uri or verb on a SOAP method, nothing downloaded or saved | 1, 2 |
| TC-U-C14 | unit | Every port of the calculator and of the synthetic WSDL, with package-name added, is accepted and saved by register-web-service; a header array and an object array are saved with the members the platform renders | 2 |
| TC-U-C15 | unit | wsdl-url is downloaded through the platform as given; a non-http(s), relative or credential-carrying URL is refused before anything is sent; both or neither source is refused | 1 |
| TC-U-C16 | unit | No CanManageSolution, or an unreadable prefix, refuses the call before any download | 1 |
| TC-U-C17 | unit | wsdl-files: a relative path, a missing file, a file over 10 MB and more than 100 files are refused; local schema files merge, found by file name | 1 |
| TC-U-C18 | unit | port and operations narrow the result (case-insensitive operation names); an unknown one is refused with the names there are | 2 |
| TC-U-C19 | unit | A result over the size limit lists operation names per port with a notice; a filtered read returns arguments | 2 |
| TC-U-C20 | unit | A service code in use gives one notice; a '?' in a downloaded name gives the wsdl-files warning; service-code on a REST service is refused | 2 |
| TC-U-C21 | unit | The download client posts the URL array to the route, keys files by URL and schemaLocation, reports the platform's message redacted without its stack trace, and refuses non-text values and an empty download | 1 |
| TC-U-C22 | unit | The route is `0/rest/DownloadWsdlEndpoint/GetXmlSchemaFilesContent` on .NET Framework and `rest/...` on .NET Core | 1 |
| TC-U-C23 | unit | The tool answers guessed argument names with a rename hint, refuses unknown keys with the valid list, passes every argument to the environment's reader and returns a failure as an error | 1 |
| TC-U-C25 | unit | A file nested past 256 levels is refused before it is copied; a chain of types past 64 levels stops with a warning; a base-type or type-reference cycle stops with a warning; an exponential expansion is refused past 200,000 elements | 2 |
| TC-U-C26 | unit | A file imported under 200 spellings is merged once; a relative schemaLocation and a WCF `?xsd=xsd0` location are found; two given files of one name are refused as ambiguous | 1 |
| TC-U-C27 | unit | Operation, port and element names that are not XML names are skipped; the service caption is flattened; no warning carries a line break or a long WSDL text; warnings stop at 100 with a count | 2 |
| TC-U-C28 | unit | An array inside an array item is marked by the parser (Studio's path checked against Studio's parser) and left out of the arguments; an array whose item element register would refuse is left out | 2 |
| TC-U-C29 | unit | The warning branches of D9: missing binding, no input, undefined message element, unresolved reference, missing base type, rpc style, several WSDL files, a message element type that extends another, a split input message | 2 |
| TC-U-C30 | unit | A port address register would refuse is kept with a warning; a soapAction with a line break is left out; a target's parameter at the same path keeps its code, and a dropped one keeps its code reserved; a prefix the target uses otherwise is renamed; a cut code without a prefix stays an identifier; one warning log for every port lists a message once and counts past 100 | 2 |
| TC-U-C31 | unit | A network path and a key-carrying URL are refused before any request; an empty file (what a FIFO looks like), a device and a reparse point are refused before they are opened; files over 50 MB together are refused; a failed download points at wsdl-files; an endpoint on another host gives a notice; a filtered read over the size limit lists names; operations filter across ports; wsdl-files is refused over HTTP | 1, 2 |
| TC-U-C32 | unit | A reply over the cap a caller sets is refused before it is deserialized | 1 |
| TC-I-C01 | e2e (NoEnvironment) | read-wsdl is discoverable and answers a guessed `url` with `wsdl-url` | 1 |
| TC-I-C02 | e2e | wsdl-files with a WSDL shipped in the test project: both SOAP ports, the SOAP 1.2 one picked, codes from the stand's prefix; the pick, trimmed to three operations, registers under a throwaway code and describe reads back the same namespaces, message elements, SOAP actions, header flags and item elements (no copy); a second read with service-code lists every method as existing; removed in the teardown | 1, 2 |
| TC-I-C03 | e2e (manual, `[Explicit]`, McpE2E.Manual) | wsdl-url: the stand downloads the public calculator WSDL; two ports, `<prefix>Add`, `tem:Add`; ignored when the stand has no internet | 1 |
| TC-M-C01 | manual | The calculator WSDL through Studio's SOAP quick setup and through read-wsdl + register-web-service gives equivalent saved services (a Studio stand) | 4 |

TC-M-C01 result, 2026-11-12, Creatio 10.2 Studio (.NET Framework, prefix `Usr`), clio at the slice C head: **PASS**. The
public calculator WSDL (`http://www.dneonline.com/calculator.asmx?WSDL`) went once through Studio's SOAP quick setup
(all methods and parameters kept; the wizard preselected `CalculatorSoap12 (SOAP 1.2)`, the port read-wsdl marks
`studio-pick`) and once through read-wsdl in URL mode + register-web-service with the `studio-pick` arguments. Read back
with describe-web-service, the two services are equal in type (SOAP 1.2), base URI, retry count, authentication,
namespaces (`tem` = `http://tempuri.org/`, default), and per method in caption, verb, content type, timeout, SOAP action
and every request and response parameter (code, caption, path, type, flags). The only differences are the ones ADR D9
and D11 choose: Studio's method codes carry a random suffix (`UsrAdd_716f73f2`, clio `UsrAdd`), and Studio stores the
message element bare (`Add`), clio with its prefix (`tem:Add`), the same element because `tem` is the default
namespace. A test call of Add with 2 and 3 returned HTTP 200 and `UsrAResult` 5 through both.

## Slice D (story 4), outline

TC-U-D: arrays and object arrays mapped to the platform's `nested` member; a single object refused; masking of
nested values. TC-I-D: a test call with an array and an object array, asserting the sent body.

## Knowledge (story 5)

TC-K: the clio-knowledge bundle tests and the fact-check gate on the changed articles.
