# Story 1: SOAP read side, XML-aware masking and parsed test outputs

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R1-R4
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D2-D6
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-A*, TC-I-A*
**Status**: in-progress (implementation started together with this BMAD set, branch
`feature/ENG-105475-soap-read-side`)
**Size**: M
**Depends on**: nothing (clio#1900 is merged)
**Blocks**: stories 2 and 4

---

## As a

coding agent working on a business process that calls a SOAP API registered in Studio > Web services

## I want

to read the SOAP service exactly (version, namespaces, message elements, SOAP actions, SOAP-header and sequence
elements) and to test-call its methods with credentials in the SOAP body masked, seeing the parsed outputs

## So that

I can map a Call web service element correctly and check its response paths without opening Studio

## Acceptance criteria

1. `describe-web-service` on a SOAP 1.1 service returns `type: "SOAP 1.1"`, on SOAP 1.2 `"SOAP 1.2"`; REST stays
   `"REST"` (D3).
2. A SOAP service's describe carries `namespaces` (`prefix`, `uri`, `default`), the default first; each SOAP method
   carries `message-element` and `soap-action` and no `uri`; a SOAP-header parameter carries `soap-header: true`; an
   array parameter of a SOAP service carries `sequence-element`. A REST service's describe has none of these members.
3. A schema read and saved back carries no SOAP member it did not have (D2); a REST `setMethod` keeps
   `soapAction`, `isSoapHeader` and `sequenceElementName` of the method and parameters it keeps.
4. `test-web-service-method` on a SOAP method whose SOAP-header or body element takes its value from a system setting
   returns the body with that value replaced by `[masked]` (also in its XML-escaped form), with
   `sent-secrets-masked: true` when it was echoed; the clio#1900 stopgap no longer withholds such a body (D4).
5. The response is withheld (status only) when the sent body is neither JSON nor XML, or a top-level setting-bound
   element is missing from the sent XML.
6. `test-web-service-method` returns `response-values`, the platform's parsed outputs by parameter code, masked like
   the body, masked by code where a parameter may carry a credential, and withheld with the body (D5).
7. A REST xml method's test call answers with an error that says the method can be called only from a process
   through `RequestBody`, not the bare "Rest:Xml" (D6).
8. The MCP descriptions of `describe-web-service` and `test-web-service-method` mention SOAP and `response-values`.

## Tasks

- `ServiceSchemaContracts.cs`: SOAP type codes, `ServiceNamespaceDto`, nullable `SoapAction`, `IsSoapHeader`,
  `SequenceElementName` written only when set.
- `WebServiceDefinitionBuilder`: carry the typed members from the previous method and parameter.
- `WebServiceModels.cs`: view members (omitted when null), `response-values` on the test result.
- `WebServiceResponseMasker`: XML body route, XML-escaped forms.
- `WebServiceDesignerClient`: read `ParameterValues`.
- `WebServiceDesigner.TestMethod`: response values, REST XML message, stopgap removed.
- `WebServiceTools.cs`: descriptions.
- Tests: TC-U-A01..A24 and TC-I-A01..A02 of the test plan.

## Definition of Done

- [ ] Acceptance criteria 1-8 covered by the listed tests, all green
- [ ] MCP e2e run on stand `Creatio` (TC-I-A01..A02) green, fixtures removed
- [ ] MCP reviewed; docs reviewed (MCP-only tools: no `help/en` or `docs/commands` page)
- [ ] Review gates of AGENTS.md passed (pre-PR comprehensive review)
- [ ] clio-knowledge K0 PR (XML response paths, REST XML test limit, `response-values`) opened or explicitly deferred
