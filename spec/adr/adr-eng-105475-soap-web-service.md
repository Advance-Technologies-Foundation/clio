# ADR: SOAP web services in clio's web service tools (ENG-105475)

**Status**: Accepted for slice A (2026-11-12, owner chose the order A → B → C → D with K1 alongside C). Decisions
D6-D12 shape later slices and are re-confirmed when their story starts; D11 and D12 are open owner questions.
**Date**: 2026-11-12
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md)
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md)

## Context

clio#1900 added five non-resident MCP tools over the Studio web service designer contracts (`ServiceSchemaRequest`,
`ServiceDataContract`, `CheckUniqueSchemaName`, `CallServiceSchemaService`). They model the REST shape only; SOAP
members ride in `[JsonExtensionData]` bags so a round trip keeps them, but nothing reads or writes them. The spec lists
what SOAP adds and what the platform does with a wrong SOAP definition.

## Decisions

### D1. One set of tools, one write path

SOAP is served by the same five tools, plus one read-only `read-wsdl` (slice C) whose output is
`register-web-service` input. Rejected: an `import-web-service` tool that saves straight from a WSDL. It would bypass the
agent's review of a WSDL that can produce dozens of methods (Studio lets the user deselect them), and it would be a
second write path with its own validator.

### D2. Typed SOAP members on the wire DTOs, preserved exactly

`ServiceSchemaDto.Namespaces` becomes `ServiceItemCollection<ServiceNamespaceDto>` (`uId`, `name`, `prefix`,
`isDefault` + extension bag); `ServiceMethodDto.SoapAction`, `ServiceParameterDto.IsSoapHeader` and
`ServiceParameterDto.SequenceElementName` become nullable typed members written only when not null. A schema read and
saved back is therefore unchanged whether or not the platform sent a member: a REST service read without them is not
given `soapAction: null` or `isSoapHeader: false` on save. `WebServiceDefinitionBuilder` carries them from the
previous parameter and method like the extension bags it already carries.

### D3. describe vocabulary (shared with register in slice B)

- `type`: `REST`, `SOAP 1.1` or `SOAP 1.2`, the strings `list-web-services` already returns. Rejected: lowercase
  tokens; they would change the REST value existing callers read.
- `namespaces` (SOAP only): `[{prefix, uri, default}]`, the default first.
- SOAP method: `message-element` and `soap-action`; `uri` is left out, because for SOAP it is not an address and an
  agent that reads it as one builds a wrong service. `http-method` (POST) and `content-type` (xml) stay: they are true.
- Parameter: `soap-header: true` only on a SOAP-header parameter, `sequence-element` only on an array of a SOAP
  service (the platform stores `item` everywhere, which carries no information on a scalar).
- Every new member is omitted when it does not apply, so a REST service reads exactly as before.

### D4. XML-aware masking, fail closed

The masker reads the request body the platform reports as sent: JSON as before; otherwise XML, parsed with DTD
processing prohibited and no resolver. A setting-bound body parameter (SOAP-header ones included, nested items
included) is found by the local name the platform gives its last `/` segment (Terrasoft.Services
`SoapBodyBuilder.ParseElementPath`: the first `prefix:name` match, so `tem:Api.Key` renders as `tem:Api`); every
element with that local name contributes its text. The response is withheld (status only) when the body is neither
JSON nor XML, when a top-level setting-bound element is not in the XML, or when a nested one is missing while its
parent element carries content (the platform renders every field of a parent it renders and skips a parent with no
value). Masked forms: plain, URL-, JSON- and XML-escaped (`&amp;`, `&lt;`, `&gt;`, `&quot;`, `&apos;`), the CRLF
form of a multi-line value (the envelope is written with CRLF) and each of its lines. An XML response body is masked
by its decoded text and attribute values, so numeric character references are covered, then as text. A Guid or a
number equal to a secret once both are parsed is masked whole (the platform converts parsed values to the parameter
type). A constant default on a SOAP-header element is hidden like a header constant. Rejected: keeping the clio#1900
stopgap (every SOAP call with a setting-bound element loses its body, which makes the test useless exactly for
WS-Security services); masking by path match only (the envelope adds `Envelope/Body/<message element>` above the
stored path, and a SOAP-header element sits under `Header`).

### D5. Parsed response values are returned, masked

`test-web-service-method` returns `response-values`: the platform's `ParameterValues` parsed as JSON and masked with
the same secrets as the body (values and property names, numbers and booleans whole). Besides, the value of every
response parameter that may carry a credential is masked by its code whatever it holds: a default from a system
setting or a secret constant (the platform fills the default when the API did not send the value: Terrasoft.Services
`ServiceParameterValueProvider` and the response parsers, measured on a .NET Framework stand on 2026-11-12), a cookie, and a header whose code or
name looks like a credential. They are withheld together with the body, and omitted
(`response-values-omitted: true`) past the body length limit. This is the only way an agent can see that a response
path reads nothing (an XML response read with `$.x` is null, measured); describe and the test call add a notice for
every response parameter of an XML response whose path is a JSONPath, because an isolated agent read that null as a
clio failure. The same defaults are converted to the
parameter type after the call; a conversion failure ends the call with an error that can quote the value and with
nothing reported as sent, so such an error is withheld when the method has a setting or secret response default. Rejected: withholding the values whenever any
response parameter has a default (it would hide them for most designer-made services).

### D6. REST XML: translate, do not pre-refuse

A REST method with `content-type: xml` is still called; when the platform answers "Rest:Xml" with nothing reported as
sent, the error says the method can be called only from a process that sends the whole body through the Call web
service element's `RequestBody`. This is checked before the fail-closed rule, because with nothing sent there is no
secret to locate. Rejected: refusing before the call. A future platform, or a custom request body builder, may make
the call work, and the tool should not decide that for the platform.

### D7. SOAP register/modify input (slice B)

`register-web-service` takes `type` (`rest` default, `soap11`, `soap12`; the describe strings are accepted too) and
`namespaces` (`[{prefix, uri, default?}]`). A SOAP method takes `message-element` (required) and `soap-action`
(optional; empty gives a notice that no `SOAPAction` header is sent) and no `http-method`, `uri` or `content-type`
(forced POST/XML). A SOAP parameter takes `in` body, header or cookie, `soap-header` (top-level body parameters only;
copied to its items as Studio does) and `sequence-element` (arrays; default `item`). `modify-web-service` lifts the SOAP
refusal and adds `setNamespaces` and `setType` (1.1 ↔ 1.2 only; REST ↔ SOAP refused, as Studio does). Both count as
changing every method for the usage guard, because they change every call.

### D8. The SOAP validator refuses what Studio only warns about

Exactly one default namespace when any exists; unique prefixes; non-empty URIs; every prefix in a message element or
in any path segment is declared (Studio warns and lets the user save; the call then fails with no HTTP request, so
clio refuses); request placements body, header, cookie; a response path starting with `$` is refused for XML; no
trailing slash is added to a SOAP base URI. The REST rule "an xml method has no body parameters" applies to REST only.

### D9. `read-wsdl` (slice C)

Read-only, `OpenWorld = true`. Inputs: `environment-name`, `wsdl-url` or `wsdl-files` (local paths, for a WSDL the
stand cannot reach or one with non-ASCII text, which the platform download turns into `?`), optional `port`. The URL
is fetched through `GetXmlSchemaFilesContent` (CanManageSolution, as Studio), never by clio itself, so clio adds no
network reach of its own; only URLs the user gave are fetched, and the platform's error message is returned without
its stack trace. The parser is a C# port of Studio's wizard parser with its unit-test fixtures as golden tests. Output:
one `register-web-service` argument object per SOAP port, plus `warnings` (dropped HTTP ports, recursion cap,
unsupported constructs).

### D10. Nested test values (slice D)

`parameters` accepts arrays and arrays of objects, sent through the platform's `nested` member of
`CallServiceSchemaService.Execute`. A single object stays refused, as the parameter model has none.

### D11. Open owner question: generated method codes in `read-wsdl`

Studio generates `prefix + shortened caption + _ + 8 hex characters of a GUID`, so a re-import creates new methods.
Recommended: stable codes (`UsrAdd`), with a suffix only on a collision. Decided before slice C starts.

### D12. Open owner question: which ports `read-wsdl` returns

Studio's section picks the SOAP 1.2 port when a WSDL has both. Recommended: return every SOAP port and mark the one
Studio would pick, so the agent asks the user. Decided before slice C starts.

## Consequences

- Slice A changes no write path: register still creates REST only and modify still refuses SOAP until slice B.
- Every SOAP rule is clio's to enforce: the platform save validates nothing, and a validator gap becomes a call-time
  failure with no HTTP request sent.
- The clio-knowledge guide keeps saying "a SOAP service stays in Studio" until slice B ships; the K0 wording fix
  (XML response paths, the REST XML test limit) can go earlier.

## Verification

Unit tests per decision; MCP e2e on stand `Creatio` per slice, creating the SOAP fixture through the platform's own
`ServiceDataContract` save until slice B can register one, and removing it with `ServiceSchemaRemoveRequest`. Details
in the test plan.
