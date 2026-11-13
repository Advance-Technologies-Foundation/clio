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

**Measured** on stand `Creatio` (.NET Framework, prefix `Usr`) on 2026-11-13 with raw `call-service` calls on a throwaway
REST service (echo `https://httpbin.org/anything`) and a SOAP 1.1 service (`http://localhost/<code>`), each with a Text
array, an Integer array and an Object array whose items hold a Text, an Integer, a setting-bound Text, a Text array
and an inner Object array (script and log: `C:\Projects\workspace\soap-research\evidence\slice-d-*`). The shape is the one
Studio's Send test request sends (creatio-ui `service-client-form.component.ts`, `_getParameterValue`):

| Parameter | Wire | REST body / SOAP envelope |
| --- | --- | --- |
| scalar | `{code, value: "text"}` | as before |
| array of scalars | `{code, value: ["a", "b"]}`, texts or numbers | `"tags":["a","b"]`; `<tem:Tags arrayType="xsd:array[]"><tem:Tag>a</tem:Tag>...`; converted to the item type (`"1"` → `1`) |
| array of objects | `{code, nested: [[{code, value}, ...], ...]}`, one list per item, no `value` | `"lines":[{"sku":"S1",...}]`; `<tem:Lines ...><tem:Line><tem:Sku>S1</tem:Sku>...` |
| inside an item | the same rules, recursively (a scalar array in `value`, an inner object array in `nested`) | rendered under the item |

What the platform does with anything else, which is why clio checks the shape first: a text for an array goes out one
item per character (`"ab"` → `["a","b"]`); objects in `value` go out as an empty array; `nested` on an array of scalars
sends the text `System.Collections.Generic.Dictionary...`; an unknown code inside an item is dropped silently; a code
given twice (top level or in an item) fails the call with "An item with the same key has already been added." and
nothing is sent; a null item of a scalar array goes out empty. An empty array is sent empty (`[]`, an empty element);
an array the call leaves out is not rendered at all, an inner one included, and a setting-bound item field is filled
in every item, also in an item given no fields.

Decisions taken from the measurement: values are checked against the method's parameter tree in the designer (the tool
has no schema), every problem is reported in one refusal with its location (`parameters.UsrLines[0].UsrSku`), a null
array item is refused rather than sent empty, and the masking judges a missing array element by whether the call gave
THAT array a value (the codes given a value at any depth), not its top-level ancestor, so a call that leaves an inner
array out is not withheld for it.

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
