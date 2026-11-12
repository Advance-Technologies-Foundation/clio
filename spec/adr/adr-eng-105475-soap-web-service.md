# ADR: SOAP web services in clio's web service tools (ENG-105475)

**Status**: Accepted for slice A (2026-11-12, owner chose the order A → B → C → D with K1 alongside C). Decisions
D6-D12 shape later slices and are re-confirmed when their story starts; the owner decided D11 and D12 on 2026-11-12,
before slice C started. D13 came out of slice C.
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
(optional; empty gives a notice that no `SOAPAction` header is sent) and no `uri`; `http-method` and `content-type` are
forced to POST and XML, and only those values are accepted back (describe prints them, so the round trip needs them).
A SOAP parameter takes `in` body, header or cookie, `soap-header` (top-level body request parameters; copied to its
items as Studio does, and accepted back on those items) and `sequence-element` (arrays; default `item`).
`modify-web-service` lifts the SOAP refusal and adds `setNamespaces` and `setType` (1.1 ↔ 1.2 only; REST ↔ SOAP
refused, as Studio does). Both count as changing every method for the usage guard when they really change the list or
the version (describe output sent back unchanged does not), because they change every call. A setNamespaces that drops a
prefix a kept method uses is refused; a kept method is not otherwise re-validated.

### D8. The SOAP validator refuses what Studio only warns about

Exactly one default namespace when any exists; unique prefixes, none reserved (`xml*`, and `soapenv`, which the platform
declares for the envelope); non-empty URIs without credentials or control characters; a message element of one
segment; every prefix in the message element or in a body request path segment is declared (Studio warns and lets the user save; the call then fails with no HTTP request, so
clio refuses); request placements body, header, cookie; a response path starting with `$` is refused for XML; no
trailing slash is added to a SOAP base URI; a `prefix:name` segment the platform would cut (its regex stops at the
first character outside letters, digits, `_` and `-`, so `tem:Api.Key` renders as `tem:Api`) is refused; a constant
default on a SOAP-header parameter is refused like a header constant (WS-Security and licence values travel there).
The REST rule "an xml method has no body parameters" applies to REST only.

### D9. `read-wsdl` (slice C)

Read-only, `OpenWorld = true`. Inputs: `environment-name`, `wsdl-url` or `wsdl-files` (absolute local paths, for a WSDL
the stand cannot reach or one with non-ASCII text, which the platform download turns into `?`; an import is matched by
its `schemaLocation` or by that location's file name; at most 100 files of 10 MB, the platform endpoint's own caps),
optional `port`, `service-code` (D11) and `operations` (names to return). The URL is fetched through
`GetXmlSchemaFilesContent` (CanManageSolution, as Studio; route `DownloadWsdlSchemaFiles`), never by clio itself, so
clio adds no network reach of its own; only the URL the user gave is fetched (the platform follows the
`schemaLocation`s), a URL with `user:password@` is refused, and the platform's error message is returned redacted and
without its stack trace. Local files are read with DTD processing prohibited and no resolver; a downloaded file is read
as the ASCII text it is.

The parser (`WsdlParser`) is a C# port of Studio's wizard parser (creatio-ui web-service-wizard:
`SoapWebServiceBuilder`, `SoapWebServiceMethodBuilder`, `WsdlElement`, `XmlSchemaTypeUtils`, `WsdlMergeService`, the
`xmlDataValueTypeNamesMap` type map), including its DOM selector semantics. Parity was measured, not assumed: Studio's
own parser, run under jsdom on every one of its 30 unit-test fixtures and on clio's synthetic ones, gives the same ports,
namespaces (with their `ns0..nN` order), operations, captions, paths, types, array flags, SOAP-header flags, item
elements and codes (method codes without the random suffix). The golden files in `clio.tests/Examples/Wsdl` are that
output. Where the wizard fails or writes something the platform cannot call, the port reads on and warns instead:

| Wizard | clio |
| --- | --- |
| an XSD built-in it does not map (`xs:anyURI`), or a type no schema defines: TypeError, nothing imported | Text, with a warning |
| an unresolved element `ref`: a parameter named `null` (`tem:Result/tem:null`) | the element is skipped, with a warning |
| a missing base type, message element or input: TypeError | what can be read is read, with a warning |
| every port is named after the first `wsdl:service` | each port is named after its own service |
| the message element is stored bare (`Add`; the platform qualifies it with the default namespace) | it carries the prefix of its own namespace (`tem:Add`) |
| an element of an anonymous type nested in another element is listed twice, once one level too high (`Fault/faultNumber` beside `Fault/detail/faultNumber`; Studio's own golden test expects the copy) | the parser keeps the copy for parity and marks it; the arguments leave it out, with a warning |

The mapper (`WsdlRegistrationMapper`) then applies clio's rules: codes per D11, and nothing `register-web-service`
would refuse - a request element or root element the platform's prefix regex would cut (`tem:blz.parameter`), an object
with no readable field, a repeated element without a wrapper (the wizard gives it an empty path) - each left out with a
warning; an element of no namespace loses its prefix, because an empty namespace cannot be declared. Output: per SOAP
port one `register-web-service` argument object without `package-name` (the agent adds it), `studio-pick` (D12), and
`existing-methods` with `service-code`; plus `warnings` (dropped HTTP ports, recursion cap, several SOAP headers, typed
or RPC parts, RPC/encoded, `wsdl:import`, MIME/MTOM, `xs:choice`/`xs:all`, XML attributes, and the leave-outs) and
`notices` (a service code already used on the environment). A result over 120,000 characters returns the operation
names per port instead, and the agent reads again with `operations`. Every port's output is checked in unit tests by
sending it through `register-web-service` itself.

### D10. Nested test values (slice D)

`parameters` accepts arrays and arrays of objects, sent through the platform's `nested` member of
`CallServiceSchemaService.Execute`. A single object stays refused, as the parameter model has none.

### D11. Stable codes in `read-wsdl` (owner, 2026-11-12)

A generated method or parameter code is the environment's `SchemaNamePrefix` (read with `IWebServicePrefixReader`,
exactly as `register-web-service` reads it; any value or empty; an unreadable setting refuses the call) plus a stable
tail from the WSDL name: Studio's shortening (`AddResult` → `AResult`, the capitalized words but the last cut to their
first letter) made a Latin identifier, so operation `Add` is `<prefix>Add` on every read. A number from 2 is added only
on a collision, compared case-insensitively, with another operation or parameter of the same list or with a method of
the target service (`<prefix>Add2`). There is no Studio-style `_<8 hex>` suffix. A code is capped at 200 characters by
keeping the end of the name as Studio does, but never the prefix (Studio's cap can drop it). The service code is the
prefix plus the WSDL service name, not shortened, as in Studio. Wire names (element paths with prefixes, the message
element, the SOAP action) go into `path`, `message-element` and `soap-action`, never into codes.

The target service is optional: `service-code` names an existing SOAP service the operations go into. An operation
whose root element (local name) and SOAP action match one of its methods takes that method's code and is listed in
`existing-methods` (the agent replaces it with `modify-web-service` `setMethod` and adds the rest with `addMethod`);
the namespaces start with that service's own prefixes, and a WSDL namespace it lacks gets a free `nsN` (the agent sends
the list with `setNamespaces`). Without it, a notice says when `<prefix><ServiceName>` already exists. Rejected:
Studio's GUID suffix (a re-import creates new methods instead of matching the old ones).

### D12. Every SOAP port, Studio's pick marked (owner, 2026-11-12)

`read-wsdl` returns every SOAP 1.1 and SOAP 1.2 port, each as one ready argument object, and marks with `studio-pick`
the one Studio's quick setup would create: the first SOAP 1.2 port when there is one, else the first port. HTTP
(GET/POST) ports are dropped with a warning. `port` narrows the result to one port.

### D13. A SOAP item element is a `prefix:name` (slice C)

The platform names array items by `sequenceElementName` and reads it like a path segment (Terrasoft.Services
`SoapRequestBodyBuilder.GetArrayElementNodeName`, `SoapBodyBuilder.ParseElementPath`: `prefix:name` with the prefix
looked up in the namespaces, else the default namespace), and Studio's wizard writes it prefixed (`ns0:string`). The
slice B validator accepted only a plain name, which refused every WSDL-made array and the describe → `setMethod` round
trip of a Studio-made one. `sequence-element` now takes a plain name or `prefix:name`; on a request body item the
prefix must be declared and the name must survive the platform's regex (checked with the paths), and it counts as a
used prefix for `setNamespaces`; on a response item (matched by local name) only the form is checked.

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
