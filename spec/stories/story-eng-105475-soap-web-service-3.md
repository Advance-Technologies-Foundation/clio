# Story 3: read-wsdl, from a WSDL to register-web-service arguments

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R8
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D1, D9, D11, D12
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-C*, TC-I-C*
**Status**: review (D11 and D12 decided by the owner on 2026-11-12)
**Size**: L
**Depends on**: story 2
**Blocks**: story 5

---

## As a / I want / So that

As a coding agent, I want to turn a WSDL into ready `register-web-service` arguments, so that I register a SOAP API
the way Studio's quick setup does, after the user trims the operations.

## Acceptance criteria

1. `read-wsdl` (read-only) takes `wsdl-url` (fetched through `GetXmlSchemaFilesContent`) or `wsdl-files`, and an
   optional `port`; it saves nothing (D9).
2. Output per SOAP port: type, base URI, namespaces, methods with message element, SOAP action and parameters (element
   paths, SOAP-header, sequence elements), codes per D11; `warnings` for dropped ports and unsupported constructs.
3. Golden tests from Studio's wizard fixtures (calculator, arrays, headers simple/complex/array, nested namespaces,
   by-ref, recursion, extension, long caption) match Studio's expected output.
4. The same WSDL through Studio's wizard and through `read-wsdl` + register gives equivalent saved services (stand).

## Delivery notes

- AC1-AC2: `read-wsdl` (`ReadWsdlTool`, `IWsdlReader`) with `wsdl-url` (platform download, route
  `DownloadWsdlSchemaFiles`) or `wsdl-files`, plus `port`, `service-code` and `operations`; the output carries
  `studio-pick` (D12), stable codes (D11) and `warnings`. Saves nothing.
- AC3: the parser is checked against Studio's own parser output. Studio's parser, run under jsdom, and clio's port give
  the same result on all 30 of the wizard's unit-test fixtures and on clio's synthetic fixtures, except where the ADR
  (D9) lists a deliberate difference; the golden files committed in `clio.tests/Examples/Wsdl` are Studio's output for
  the public calculator WSDL and for synthetic WSDLs written for clio (the wizard's fixtures are not public and are not
  committed). The wizard's own test expectations (calculator, arrays, headers simple/complex/array, nested namespaces,
  by-ref, recursion, extension, long caption, types) are restated in `WsdlParserTests`.
- The output of every port is sent through `register-web-service` itself in unit tests and on stand `Creatio` (e2e).
  That found a slice B gap: a prefixed `sequence-element` (Studio's form) was refused; ADR D13, fixed in slice B.
- A 3-lens review and an automated security review found that a hostile WSDL could end the MCP host (type cycles,
  exponential expansion, deep nesting, a file imported under many names), that `wsdl-files` read network paths and
  devices, and that WSDL names reached the agent's warnings unfiltered; all are closed (ADR D9) with tests.
- AC4: manual stand check, recorded in the test plan (TC-M-C01).
