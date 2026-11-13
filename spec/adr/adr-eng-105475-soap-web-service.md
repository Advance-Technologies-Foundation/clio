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
optional `port`, `service-code` (D11) and `operations` (names to return; a port with none of them is left out). The URL is fetched through
`GetXmlSchemaFilesContent` (CanManageSolution, as Studio; route `DownloadWsdlSchemaFiles`), never by clio itself, so
clio adds no network reach of its own; only the URL the user gave is fetched (the platform follows the
`schemaLocation`s), a URL with `user:password@` or a key or token in its query is refused, and the platform's error
message is returned redacted, without its stack trace and with a pointer to `wsdl-files`. Local files are read with DTD
processing prohibited and no resolver, through one handle each so the size checked is the size read; a network path
(`\\host`, `//host`), a directory, a device, a link and over 50 MB in all are refused, and over HTTP (`mcp-http`)
`wsdl-files` is refused because the caller is not on that machine. A downloaded file is read as the ASCII text it is:
its byte values are read straight into bytes, and a file past 50 MB is refused while it is read (a reply of one-digit
values carries twice the bytes its text cap was sized for, and as 4-byte integers they took four times the memory).
A drive letter mapped to a share, a directory link in a parent folder, or a file swapped after the check gets past the
path checks; that is an accepted limit, since the user names the files.
An import is found by its `schemaLocation`, by that location relative to the importing file, by its file name, or for a
WCF location (`Service.svc?xsd=xsd0`) by the query value (`xsd0.xsd`); each file is merged once whatever the spelling.

`read-wsdl` runs in the MCP host process (it is not in the worker cohort), so a WSDL must not be able to end it: a file
nested deeper than 256 levels is refused before it is copied (copying recurses), elements deeper than 64 levels below a
message part are not read, type chains (`type=` on a type, `extension base=`) stop at a cycle or 32 steps, a parse
reads at most 200,000 elements, and at most 100 warnings are listed. Names from the WSDL reach the agent in warnings,
so operation, port and element names must be XML names (NCName, as WSDL 1.1 and XSD type them; anything else is
skipped with a warning) and every WSDL value in a warning or error is flattened by rune (so an invisible Unicode tag
is caught too) and cut to 100 characters. A value that reaches the arguments the agent forwards is left out instead,
with a warning, unless it is printable ASCII: a base URI register would refuse, or with a fragment
(`#access_token=...`, checked before register's own error, which repeats the address), a namespace URI that is not
printable ASCII, carries credentials or is over 2,048 characters (its elements keep the prefix, so register refuses
them until the user gives the URI), and a soapAction that is not printable ASCII once trimmed or carries credentials (a
soapAction is a URI). A service name that is not an XML name of at most 1,024 characters is replaced by the port name
for the caption, since a sentence there would reach the arguments.

The work is bounded by what is built, not only by what is read: an operation whose messages have no parts reads no
element, so a WSDL repeating many of them over many ports escaped the element budget (the final review measured 4
million operations from a 269 KB file). So the operations of a binding are built once and shared by every port on it,
without `port` the first 20 SOAP ports and Studio's pick are built (21 at most, with a warning), at most 10,000
operations are built, and `port` and `operations` are applied inside the parse, before anything is built, so they also
narrow the element budget; Studio's pick is still decided over every port. A type definition, its extension and an
element's simple type are scanned once, not once per element that uses them; a namespace's prefix is a dictionary
lookup; import and include directives are removed in one pass per schema; a binding keeps at most 8 operations of one
name; names longer than 1,024 characters are not read; a repeated code is numbered from the last number used for it;
and more than 20,000 parameters over the ports read are listed by operation name before anything is mapped or
serialized, and so is a result whose kept parameters' paths and captions alone pass the result limit (ports on one
binding share their operations, so mapping each port copied the same long paths once per port). A parameter's path and caption, which join
the names of its ancestors up to the nearest array, are counted from the names, charged against a budget of
100,000,000 characters per parse, and only then built once (the review measured 42 s and 199 GB of allocation for an
84 KB WSDL whose paths were joined level by level). A nested `schema` element of an imported file is cut out of its
parent before anything is copied (copying each with its content copied a file nested 250 levels deep once per level),
the imported files of one WSDL hold at most 1,000 schema elements, each schema inherits at most 256 namespace
declarations, and the schemas of an import are inserted in one step. A code given back by the mapper lowers only its own
name's next number, and an item's name is kept to the end a code can reach. None of these grows quadratically. As a
backstop for an input none of them foresaw, one parse stops after 30 seconds, counted from the first file loaded, so
loading and merging count too. Running read-wsdl in the MCP worker cohort, which would isolate the host from any input, was considered and
left out: with `service-code` it reads the service through the schema designer, and the cohort keeps schema-designer
reads host-resident, because a worker's separate Creatio session can read a schema that a host-resident write has just
changed and get it stale. Moving read-wsdl means moving that read out of it first. A local path on Windows must be a drive path (`C:\...`): an NT path such as `\??\UNC\host\share` counts as
fully qualified and would open an SMB connection with the user's credentials. A `wsdl-url` naming a loopback, link-local or cloud metadata address (also in IPv4-mapped, IPv4-compatible or NAT64 form; also `fd00:ec2::254` and `100.100.100.200`) is refused before the stand is asked to fetch it; the check is advisory, because the stand resolves the name and follows every `schemaLocation` itself, and only its download endpoint could enforce it.

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
| a name that is not an XML name (an operation, a port, an element) is used as written | it is skipped, with a warning |
| an `extension` inside a nested element's anonymous type makes the outer type read as that base, losing the outer type's own fields | only the type's own `complexContent`/`simpleContent` extension counts |
| a `ref`'s `maxOccurs` is written onto the shared global element, so a later direct use of it depends on what was read before | the `maxOccurs` travels with that read; the global element is not changed |
| an element of an anonymous type nested in another element is listed twice, once one level too high (`Fault/faultNumber` beside `Fault/detail/faultNumber`; Studio's own golden test expects the copy) | the parser keeps the copy for parity and marks it; the arguments leave it out, with a warning |
| an object array inside an item of another array gets the outer array's path (`tem:order/tem:lines` for the `subs` of each `lines` item), so the platform would nest it wrongly | the parser keeps it for parity and marks it; the arguments leave it out, with a warning |

The mapper (`WsdlRegistrationMapper`) then applies clio's rules: codes per D11, and nothing `register-web-service`
would refuse - a request element or root element the platform's prefix regex would cut (`tem:blz.parameter`), an object
with no readable field, a repeated element without a wrapper (the wizard gives it an empty path) - each left out with a
warning, and so is an array whose item element register would refuse; an element of no namespace loses its prefix,
because an empty namespace cannot be declared; a port address or a namespace URI register would refuse is kept with a
warning to replace it (only the user knows the real value), and a soapAction with a line break or credentials, or longer
than 2,048 characters, is left out. Output: per SOAP
port one `register-web-service` argument object without `package-name` (the agent adds it), `studio-pick` (D12), and
`existing-methods` with `service-code`; plus `warnings` (dropped HTTP ports, recursion cap, several SOAP headers, the
rpc style, RPC/encoded, `wsdl:import`, MIME/MTOM, `xs:choice`/`xs:all`, XML attributes, a message element whose type
extends another, a binding that splits the input message between body and header, and the leave-outs) and `notices` (a
service code already used on the environment, an endpoint on another host than the WSDL URL). A result over 120,000
characters, filtered or not, returns at most 500 operation names over all ports instead, and the agent reads again with
`operations`; when the result is already one operation of one port, the notice says that narrowing cannot help and the
method is described by hand. Every port's output is checked in unit tests by
sending it through `register-web-service` itself.

### D10. Nested test values (slice D)

`parameters` accepts arrays and arrays of objects, sent through the platform's `nested` member of
`CallServiceSchemaService.Execute`. A single object stays refused, as the parameter model has none.

### D11. Stable codes in `read-wsdl` (owner, 2026-11-12; full names, owner, 2026-11-13)

A generated method or parameter code is the environment's `SchemaNamePrefix` (read with `IWebServicePrefixReader`,
exactly as `register-web-service` reads it; any value or empty; an unreadable setting refuses the call) plus the full
WSDL name in PascalCase made a Latin identifier: the name is split into words at every character that is not a Latin
letter or digit (so `short_description` and `customer name` are two words each), each word gets a capital first letter,
and the words are joined. Operation `GetCustomerOrders` is `<prefix>GetCustomerOrders`, element `intA` is
`<prefix>IntA`, `AddResult` is `<prefix>AddResult`, on every read. A parameter's name is the local names of its whole
element path (`customer/name` → `<prefix>CustomerName`), not Studio's caption: the caption drops a first word that
every top-level parameter shares, a wrapper element, so `tem:order/tem:id` would be a bare `<prefix>Id` and a code
would change when a sibling is added. An array's code adds the local name of the element that repeats, its
sequence element: sibling arrays that repeat directly inside one element share the path Studio gives them, so
`order/lines*` and `order/notes*` are `<prefix>OrderLines` and `<prefix>OrderNotes`, not `<prefix>Order` and
`<prefix>Order2` in WSDL order. An item's code starts with its array's (the items of `tem:msg/ns0:roles` repeating
`RoleObject` → `<prefix>MsgRolesRoleObjectIdentifier`), so fields of different branches differ without a number, and a
wrapper element or a SOAP header's root is part of every code under it (`<prefix>MsgOwner`,
`<prefix>AuthHeaderUserName`). A letter outside Latin is dropped from its word (`Größe` → `Gre`);
a name with no Latin letter or digit, such as an all-Cyrillic one, falls back to `Param` or `Method`, numbered in
reading order as before (nothing is transliterated). A number from 2 is added
only on a real collision, compared case-insensitively, with another operation or parameter of the same list or with a
method of the target service (`<prefix>Add2`). There is no Studio-style `_<8 hex>` suffix. Studio's method and
parameter pages check only that a code is a Latin letter followed by Latin letters, digits or underscores, and the
platform's service schema checks no length; the 200 characters of Studio's wizard are its generator's own choice, so a
method code is not cut. One limit is real for a parameter code: the Call web service element names its process
parameters `<code>_In` and `<code>_Out`, and a compiled process declares a C# field `_<name>` for each, where C#
allows at most 512 characters (CS0645), so a code over 507 characters breaks the compile of every process that calls
the method. A parameter code is therefore cut at 500 characters, keeping the prefix and the end of the name as the wizard
does (a collision's number stays at the end). Without a prefix, a code that would start with a digit starts with
`Param` or `Method`. The parser bounds the names and their nesting, and the reader bounds the whole result. The service
code is the prefix plus
the WSDL service name, not shortened, as in Studio, kept within 200 characters as a schema name by keeping the end of
the name, but never without the prefix (Studio's cap can drop it). Wire names (element paths with prefixes, the message
element, the SOAP action) go into `path`, `message-element` and `soap-action`, never into codes.

The target service is optional: `service-code` names an existing SOAP service the operations go into. An operation
whose root element (local name) and SOAP action match one of its methods takes that method's code and is listed in
`existing-methods` (the agent replaces it with `modify-web-service` `setMethod` and adds the rest with `addMethod`), and
each of its parameters at the place of one of that method's parameters (its path, and for an array its sequence
element) keeps that parameter's code, so `setMethod` keeps its UId (a Studio-made method's codes are not always the
ones clio would generate). An array whose stored item element differs (Studio's default `item`, a bare name) is
matched when it is the only array at its path; only SOAP body and soap-header parameters are matched, so an HTTP
header, query or cookie parameter is never taken for a WSDL element. `setMethod` replaces the method, so what Studio set
on the matched one is carried into the arguments: its timeout and authentication (`use-auth` false too, which register
would otherwise take from the service), and each matched parameter's default (system setting, or a constant except on
a soap header) and required flag; its parameters the WSDL lacks (an HTTP header added in Studio, an object whose
fields were all left out) are named in a warning, or setMethod would remove them. A soapAction the arguments leave out is compared as left out, so such a method matches again;
the namespaces start with that service's own prefixes, and a WSDL namespace it lacks gets a free `nsN` (the agent sends
the list with `setNamespaces`). Without it, a notice says when `<prefix><ServiceName>` already exists. Rejected:
Studio's GUID suffix (a re-import creates new methods instead of matching the old ones). Rejected on 2026-11-13:
Studio's shortening (`AddResult` → `AResult`, the capitalized words but the last cut to their first letter), which
clio used until then. It makes codes unreadable, and names that shorten alike (`GetByRoles` and `GetBankRoles` are
both `GBRoles`, `AddResult` and `AverageResult` both `AResult`) are told apart only by a number given in reading
order, so a fresh read of a WSDL that adds or reorders an operation renumbers them. (A re-import with `service-code`
matches methods by root element and SOAP action and parameters by path, never by code, so it keeps the old codes
either way.)

### D12. Every SOAP port, Studio's pick marked (owner, 2026-11-12)

`read-wsdl` returns every SOAP 1.1 and SOAP 1.2 port, each as one ready argument object, and marks with `studio-pick`
the one Studio's quick setup would create: the first SOAP 1.2 port when there is one, else the first port. HTTP
(GET/POST) ports are dropped with a warning. `port` narrows the result to one port.

### D13. A SOAP item element is a `prefix:name` (found in slice C, implemented in slice B)

The platform names array items by `sequenceElementName` and reads it like a path segment (Terrasoft.Services
`SoapRequestBodyBuilder.GetArrayElementNodeName`, `SoapBodyBuilder.ParseElementPath`: `prefix:name` with the prefix
looked up in the namespaces, else the default namespace), and Studio's wizard writes it prefixed (`ns0:string`). The
slice B validator accepted only a plain name, which refused every WSDL-made array and the describe → `setMethod` round
trip of a Studio-made one. Slice B took the fix (its commits "Accept a prefixed sequence-element and keep its prefix
declared" and "Check a response array's sequence-element by local name only"): `sequence-element` takes a plain name or
`prefix:name`; on a request array the prefix must be declared and the name must survive the platform's regex, and it
counts as a used prefix for `setNamespaces`; on a response array only the local name is checked. `read-wsdl` leaves out
an array whose item element would be refused.

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
