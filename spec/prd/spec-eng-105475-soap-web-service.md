# SOAP web services in clio's web service tools: spec

**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI
Toolkit). The REST part shipped in clio#1900 and clio-knowledge#315; both promise SOAP in separate PRs, and the owner
chose (2026-11-12) to deliver SOAP under the same key.
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md)
**Stories**: [1 read side](../stories/story-eng-105475-soap-web-service-1.md),
[2 register/modify](../stories/story-eng-105475-soap-web-service-2.md),
[3 read-wsdl](../stories/story-eng-105475-soap-web-service-3.md),
[4 nested test values](../stories/story-eng-105475-soap-web-service-4.md),
[5 guidance](../stories/story-eng-105475-soap-web-service-5.md)
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md)
**Research**: the SOAP research report of 2026-11-12 (Creatio support matrix, measured stand behaviour, gaps per
tool). Its facts are restated below where a decision rests on them; "measured" means measured on stand `Creatio`
(.NET Framework, prefix `Usr`) in November 2026.

## Problem

Creatio has three web service types: REST (1), SOAP 1.1 (2) and SOAP 1.2 (3). clio's web service tools create and
change REST only. For a SOAP service today:

| Tool | Behaviour |
| --- | --- |
| `list-web-services` | correct (`SOAP 1.1` / `SOAP 1.2`, read from `VwWebServiceV2`) |
| `describe-web-service` | lossy: `type: "SOAP"` for both versions; namespaces, SOAP action, SOAP-header flags and sequence element names are dropped; the message element is shown as `uri`, which for SOAP is not an address |
| `register-web-service` | REST only (`type` is an unknown argument) |
| `modify-web-service` | refused: "is a SOAP service; change it in Studio" |
| `test-web-service-method` | calls work (the platform builds the envelope), but a setting-bound value sent in the XML body (a WS-Security password) cannot be masked, so the response is withheld whole (the stopgap in clio#1900); the parsed outputs (`ParameterValues`) are never returned |

An agent that has to call a SOAP API from a business process therefore needs Studio for every step, and cannot
check a SOAP response path through the test call.

## Platform facts the design rests on

- SOAP methods are POST with an XML request and response. The method `request.uri` is the **message element** (the
  root element inside `<Body>`, e.g. `tem:Add`), not an address: the URL is the base URI alone (Terrasoft.Services
  `ServiceClientRequest`).
- A SOAP service carries **namespaces** (`uId`, `name` = URI, `prefix`, `isDefault`); the envelope declares every one.
  When any exists, exactly one must be the default; otherwise the call fails with "Sequence contains no matching
  element" and no HTTP request is sent (measured).
- A method carries `soapAction`. An empty one sends no `SOAPAction` header (measured); SOAP 1.2 sends it as a header,
  not as an `action=` Content-Type parameter.
- A parameter carries `isSoapHeader` (a top-level body parameter rendered under `<soapenv:Header>`) and
  `sequenceElementName` (the item element of an array; the platform stores `item` on every SOAP parameter by default).
  Paths are element paths `prefix:a/prefix:b`; response paths are local-name element paths `a/b` (JSONPath reads null,
  measured).
- An undeclared path prefix saves fine and fails at call time with "The given key was not present in the
  dictionary." and no HTTP request (measured). The platform save validates nothing.
- A trailing slash on an `.asmx` base URI makes the server answer 500 (measured); REST stores one.
- WSDL import has no server-side parser: `rest/DownloadWsdlEndpoint/GetXmlSchemaFilesContent` only downloads the
  WSDL and the XSDs it imports (measured); Studio parses them in the browser.
- `CallServiceSchemaService.Execute` returns `RequestBody`, `RequestRawData`, `ResponseBody`, `StatusCode` and
  `ParameterValues`, a JSON text of the parsed response parameters by code (measured, REST and SOAP).
- A REST method with `content-type: xml` cannot be test-called: the platform has no REST XML request builder and
  answers "Rest:Xml" before sending anything, with or without body parameters (measured). A process can call it
  only through the element's `RequestBody`.

## Requirements

| # | Requirement | Slice |
| --- | --- | --- |
| R1 | `describe-web-service` reports `SOAP 1.1` / `SOAP 1.2`, the namespaces with the default, and per method the message element and SOAP action, per parameter the SOAP-header flag and the sequence element name | A |
| R2 | `test-web-service-method` masks setting-bound values sent in an XML body (SOAP body and SOAP header elements), and withholds the response when such a value cannot be located (fail closed); the clio#1900 stopgap goes | A |
| R3 | `test-web-service-method` returns the parsed response values, masked like the body | A |
| R4 | `test-web-service-method` explains a REST XML failure: callable only from a process with `RequestBody` | A |
| R5 | `register-web-service` creates a hand-described SOAP 1.1 or 1.2 service; `modify-web-service` changes one, with `setNamespaces` and `setType` (1.1 ↔ 1.2) | B |
| R6 | The SOAP validator refuses what the platform accepts but cannot call: no default namespace, an undeclared prefix, path or query placements, a SOAP header below the top level, a JSONPath response path | B |
| R7 | describe output of a SOAP service is valid modify input (round trip) | B |
| R8 | A read-only `read-wsdl` tool turns a WSDL (URL through the platform endpoint, or local files) into ready `register-web-service` arguments with warnings, saving nothing | C |
| R9 | The test call takes values for arrays and object arrays | D |
| R10 | The clio-knowledge registration guide covers SOAP, element paths and the REST XML limit; `process-web-service` and `routing` point at it | K1 |

## Non-goals

- A delete tool (Studio removes services; the e2e teardown uses the platform contract directly).
- WSDL 2.0, `wsdl:import` of another WSDL, RPC/encoded typing, MTOM, signed WS-Security: Studio's wizard does not
  support them either; `read-wsdl` reports them as warnings.
- Any CrtProcessBuilder change: the Call web service element already builds SOAP services by the REST rules (notice
  N10); only an optional test-only fixture is listed as a follow-up.
- A custom REST XML request body builder (`bodyBuilderId`): no builder ships and the lookup is empty on the stands;
  it stays in the request's extension bag and survives a round trip.

## Acceptance

Per slice, in its story. Every slice ships unit tests in `clio.tests`, MCP e2e coverage in `clio.mcp.e2e` that
creates and removes its own fixtures (no shipped package, any schema-name prefix), and the docs/MCP review the
repository requires. The tools are MCP-only (no CLI verb, so no `help/en` or `docs/commands` page).
