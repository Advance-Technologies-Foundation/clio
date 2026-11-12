# Story 5: Guidance for SOAP web services

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R10
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md)
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-K-*
**Status**: ready-for-dev
**Size**: S
**Repo**: clio-knowledge
**Depends on**: stories 2 and 3 (the K0 part: story 1)
**Blocks**: nothing

---

## As a / I want / So that

As a coding agent, I want the guidance to tell me how to register, read and test a SOAP service, so that I choose
`read-wsdl` first, write element paths and namespaces correctly and know the platform limits.

## Acceptance criteria

1. K0 (with story 1): `web-service-registration` says XML response paths are element paths (`a/b`), a REST xml
   method cannot be test-called, and `response-values` shows the parsed outputs; SOAP services can be described
   and test-called.
2. K1 (with story 3): a SOAP section (read-wsdl first, hand-described fallback, namespaces and the default, message
   element, SOAP action and the empty-header pitfall, SOAP header and WS-Security through SecureText settings, sequence
   elements, 1.1 vs 1.2, no trailing slash, limits); `process-web-service` and `routing` point at it.
3. `libraryVersion` and `sequence` bumped; the fact-check gate passes.
