# Story 2: Register and modify a hand-described SOAP service

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R5-R7
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D7, D8
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-B*, TC-I-B*
**Status**: done (clio PR #1928 merged to master on 2026-11-13)
**Size**: L
**Depends on**: story 1
**Blocks**: stories 3 and 5

---

## As a / I want / So that

As a coding agent, I want to register and change a SOAP 1.1 or 1.2 service from a description, so that a process can
call a SOAP API that has no usable WSDL, and so that `read-wsdl` (story 3) has a write path.

## Acceptance criteria

1. `register-web-service` with `type` `soap11` or `soap12` and `namespaces` saves a SOAP service; methods take
   `message-element` and `soap-action`, parameters `soap-header` and `sequence-element` (D7).
2. The validator refuses, all in one answer: no or several default namespaces, duplicate prefixes, an empty URI, an
   undeclared prefix in a message element or a path segment, path or query placements, `soap-header` below the top
   level or on a non-body parameter, a `$` response path, REST-only members on a SOAP method (D8).
3. No trailing slash is added to a SOAP base URI; an empty `soap-action` gives a notice.
4. `modify-web-service` changes a SOAP service; `setNamespaces` and `setType` (1.1 ↔ 1.2) count as changing every method
   for the usage guard; REST ↔ SOAP is refused.
5. describe output of a SOAP service sent back as `setMethod` / `setNamespaces` changes nothing (round trip).
6. A service registered by clio opens and saves unchanged in Studio (stand check, SOAP 1.1 and 1.2).
