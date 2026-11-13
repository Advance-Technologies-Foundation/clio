# Story 3: read-wsdl, from a WSDL to register-web-service arguments

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R8
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D1, D9, D11, D12
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-C*, TC-I-C*
**Status**: ready-for-dev (the owner decides D11 and D12 before it starts)
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
